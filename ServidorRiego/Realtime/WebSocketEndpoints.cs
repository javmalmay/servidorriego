using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ServidorRiego.Services;

namespace ServidorRiego.Realtime
{
    /// <summary>
    /// Registra los dos endpoints WebSocket crudos (RFC 6455) de la aplicación:
    ///
    ///   GET /ws/device?token={deviceToken}    - canal permanente para un dispositivo de riego
    ///   GET /ws/user?access_token={jwt}       - canal permanente para un usuario (o via header Authorization: Bearer)
    ///
    /// Ver <see cref="WsMessageType"/> para el formato de los mensajes intercambiados.
    /// </summary>
    public static class WebSocketEndpoints
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public static void MapRiegoWebSockets(this IEndpointRouteBuilder app)
        {
            app.MapGet("/ws/device", HandleDeviceConnectionAsync);
            app.MapGet("/ws/user", HandleUserConnectionAsync).RequireAuthorization();
        }

        private static async Task HandleDeviceConnectionAsync(
            HttpContext context,
            IDeviceService deviceService,
            WebSocketConnectionManager connectionManager,
            ILogger<WebSocketConnectionManager> logger)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var token = context.Request.Query["token"].ToString();
            var device = await deviceService.ValidateDeviceTokenAsync(token);
            if (device == null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            // AddDeviceConnection deja al dispositivo como "conectado" en memoria (IsOnline, WebSocketConnectionManager).
            var connectionId = connectionManager.AddDeviceConnection(device.Id, socket);
            // LastSeenAt sí se persiste, para saber cuándo estuvo activo aunque el servicio se reinicie.
            await deviceService.UpdateLastSeenAsync(device.Id);
            logger.LogInformation($"Dispositivo {device.Id} ({device.MacAddress}) conectado por WebSocket");

            await NotifyAssociatedUsersAsync(deviceService, connectionManager, device.Id, device.MacAddress, device.Name, WsMessageType.DeviceOnline, null);

            try
            {
                await ReceiveLoopAsync(socket, async rawMessage =>
                {
                    // Cada mensaje recibido del dispositivo cuenta como actividad: se actualiza LastSeenAt.
                    await deviceService.UpdateLastSeenAsync(device.Id);

                    var payload = TryParsePayload(rawMessage);
                    await NotifyAssociatedUsersAsync(
                        deviceService, connectionManager, device.Id, device.MacAddress, device.Name,
                        WsMessageType.DeviceStatus, payload);
                });
            }
            finally
            {
                // RemoveDeviceConnection deja al dispositivo como "desconectado" en memoria de inmediato,
                // se pierda la conexión de la forma que se pierda (cierre limpio, error de red, etc.).
                connectionManager.RemoveDeviceConnection(device.Id, connectionId);
                await deviceService.UpdateLastSeenAsync(device.Id);
                await NotifyAssociatedUsersAsync(deviceService, connectionManager, device.Id, device.MacAddress, device.Name, WsMessageType.DeviceOffline, null);
                logger.LogInformation($"Dispositivo {device.Id} ({device.MacAddress}) desconectado del WebSocket");
            }
        }

        private static async Task HandleUserConnectionAsync(
            HttpContext context,
            IDeviceService deviceService,
            WebSocketConnectionManager connectionManager,
            ILogger<WebSocketConnectionManager> logger)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var userIdClaim = context.User.FindFirst("userId");
            if (!int.TryParse(userIdClaim?.Value, out var userId))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var connectionId = connectionManager.AddUserConnection(userId, socket);
            logger.LogInformation($"Usuario {userId} conectado por WebSocket");

            try
            {
                await ReceiveLoopAsync(socket, async rawMessage =>
                {
                    await HandleUserMessageAsync(rawMessage, userId, deviceService, connectionManager, socket);
                });
            }
            finally
            {
                connectionManager.RemoveUserConnection(userId, connectionId);
                logger.LogInformation($"Usuario {userId} desconectado del WebSocket");
            }
        }

        private static async Task HandleUserMessageAsync(
            string rawMessage,
            int userId,
            IDeviceService deviceService,
            WebSocketConnectionManager connectionManager,
            WebSocket socket)
        {
            WsIncomingMessage? message;
            try
            {
                message = JsonSerializer.Deserialize<WsIncomingMessage>(rawMessage, JsonOptions);
            }
            catch (JsonException)
            {
                await SendErrorAsync(socket, "Mensaje no es un JSON válido");
                return;
            }

            if (message == null || message.Type != WsMessageType.Command || message.DeviceId == null)
            {
                await SendErrorAsync(socket, "Mensaje debe ser de tipo 'command' con un deviceId");
                return;
            }

            var isAssociated = await deviceService.IsUserAssociatedAsync(message.DeviceId.Value, userId);
            if (!isAssociated)
            {
                await SendErrorAsync(socket, "No tienes acceso a ese dispositivo");
                return;
            }

            var envelope = BuildEnvelope(WsMessageType.Command, message.DeviceId, null, null, message.Payload);
            var delivered = await connectionManager.SendToDeviceAsync(message.DeviceId.Value, envelope);
            if (!delivered)
            {
                await SendErrorAsync(socket, "El dispositivo no está conectado en este momento");
            }
        }

        private static async Task NotifyAssociatedUsersAsync(
            IDeviceService deviceService,
            WebSocketConnectionManager connectionManager,
            int deviceId,
            string macAddress,
            string deviceName,
            string type,
            JsonElement? payload)
        {
            var envelope = BuildEnvelope(type, deviceId, macAddress, deviceName, payload);
            var userIds = await deviceService.GetAssociatedUserIdsAsync(deviceId);
            foreach (var userId in userIds)
            {
                await connectionManager.SendToUserAsync(userId, envelope);
            }
        }

        private static string BuildEnvelope(string type, int? deviceId, string? macAddress, string? deviceName, JsonElement? payload)
        {
            var envelope = new Dictionary<string, object?>
            {
                ["type"] = type,
                ["deviceId"] = deviceId,
                ["macAddress"] = macAddress,
                ["deviceName"] = deviceName,
                ["payload"] = payload,
                ["timestamp"] = DateTime.UtcNow
            };
            return JsonSerializer.Serialize(envelope, JsonOptions);
        }

        private static JsonElement? TryParsePayload(string rawMessage)
        {
            try
            {
                using var doc = JsonDocument.Parse(rawMessage);
                return doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                // El dispositivo no mandó JSON: se envuelve como string en el payload
                return JsonSerializer.SerializeToElement(rawMessage);
            }
        }

        private static async Task SendErrorAsync(WebSocket socket, string message)
        {
            if (socket.State != WebSocketState.Open)
            {
                return;
            }

            var envelope = BuildEnvelope(WsMessageType.Error, null, null, null, JsonSerializer.SerializeToElement(message));
            var bytes = Encoding.UTF8.GetBytes(envelope);
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        /// <summary>
        /// Bucle de lectura de un WebSocket: acumula frames hasta completar un mensaje de texto,
        /// invoca el callback por cada mensaje completo, y termina limpiamente al recibir Close.
        /// </summary>
        private static async Task ReceiveLoopAsync(WebSocket socket, Func<string, Task> onMessage)
        {
            var buffer = new byte[8192];
            while (socket.State == WebSocketState.Open)
            {
                using var messageStream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Cierre solicitado por el cliente", CancellationToken.None);
                        return;
                    }
                    messageStream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var text = Encoding.UTF8.GetString(messageStream.ToArray());
                    await onMessage(text);
                }
            }
        }
    }
}
