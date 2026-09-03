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
            logger.LogInformation($"Recibida una nueva conexión de dispositivo por WebSocket");
            var token = context.Request.Query["token"].ToString();
            logger.LogDebug($"Intentando conectar con el token {token}");
            var device = await deviceService.ValidateDeviceTokenAsync(token);
            if (device == null)
            {
                logger.LogWarning($"No se ha encontrado ningún dispositivo con el accesstoken indicado");
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
                    var incomingCorrelationId = TryExtractCorrelationId(payload);

                    if (connectionManager.TryResolveCommand(device.Id, incomingCorrelationId, out var pendingUserId, out var resolvedCorrelationId))
                    {
                        // Hay un comando en curso de este dispositivo: este mensaje es su respuesta,
                        // se reenvía SOLO al usuario que lo envió (no se difunde a los demás asociados).
                        var responseEnvelope = BuildEnvelope(WsMessageType.CommandResponse, device.Id, device.MacAddress, device.Name, payload, resolvedCorrelationId);
                        await connectionManager.SendToUserAsync(pendingUserId, responseEnvelope);
                    }
                    else
                    {
                        // Sin comando pendiente: es telemetría/estado espontáneo, se difunde a todos los asociados.
                        await NotifyAssociatedUsersAsync(
                            deviceService, connectionManager, device.Id, device.MacAddress, device.Name,
                            WsMessageType.DeviceStatus, payload);
                    }
                });
            }
            finally
            {
                // RemoveDeviceConnection deja al dispositivo como "desconectado" en memoria de inmediato,
                // se pierda la conexión de la forma que se pierda (cierre limpio, error de red, etc.).
                connectionManager.RemoveDeviceConnection(device.Id, connectionId);
                // Si había comandos esperando respuesta y el dispositivo se desconecta sin
                // responder, se descartan: evita que una futura reconexión con telemetría espontánea
                // se enrute por error como respuesta a un comando ya perdido.
                connectionManager.ClearPendingCommandsForDevice(device.Id);
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

            logger.LogInformation($"Recibida una nueva conexión de usuario por WebSocket");

            var userIdClaim = context.User.FindFirst("userId");
            if (!int.TryParse(userIdClaim?.Value, out var userId))
            {
                logger.LogWarning($"No se puede obtener el id de usuario");
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
                    await HandleUserMessageAsync(rawMessage, userId, deviceService, connectionManager);
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
            WebSocketConnectionManager connectionManager)
        {
            DeviceCommandRequest? command;
            try
            {
                command = JsonSerializer.Deserialize<DeviceCommandRequest>(rawMessage, JsonOptions);
            }
            catch (JsonException)
            {
                await SendErrorToUserAsync(connectionManager, userId, "Mensaje no es un JSON válido");
                return;
            }

            if (command == null || command.DeviceId <= 0 || string.IsNullOrWhiteSpace(command.Comando))
            {
                await SendErrorToUserAsync(connectionManager, userId, "El mensaje debe incluir DeviceId y Comando");
                return;
            }

            var isAssociated = await deviceService.IsUserAssociatedAsync(command.DeviceId, userId);
            if (!isAssociated)
            {
                await SendErrorToUserAsync(connectionManager, userId, "No tienes acceso a ese dispositivo");
                return;
            }

            // Se genera un CorrelationId nuevo y se recuerda a qué usuario reenviarle la respuesta
            // del dispositivo cuando llegue (ver HandleDeviceConnectionAsync).
            var correlationId = connectionManager.RegisterPendingCommand(command.DeviceId, userId);

            // Al dispositivo se le manda el comando tal cual, SIN el DeviceId (ya sabe quién es),
            // pero CON el CorrelationId para que lo devuelva en su respuesta.
            var commandForDevice = JsonSerializer.Serialize(new DeviceCommandForward
            {
                Comando = command.Comando,
                Parametros = command.Parametros,
                CorrelationId = correlationId
            }, JsonOptions);

            var delivered = await connectionManager.SendToDeviceAsync(command.DeviceId, commandForDevice);
            if (!delivered)
            {
                connectionManager.ClearPendingCommand(correlationId);
                await SendErrorToUserAsync(connectionManager, userId, "El dispositivo no está conectado en este momento");
                return;
            }

            // Confirmación inmediata con el CorrelationId, para que la app pueda casarlo más
            // tarde con el "command_response" que llegue (el servidor lo genera, el cliente no
            // lo conoce hasta este punto).
            var ackEnvelope = BuildEnvelope(WsMessageType.CommandSent, command.DeviceId, null, null, null, correlationId);
            await connectionManager.SendToUserAsync(userId, ackEnvelope);
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

        private static string BuildEnvelope(string type, int? deviceId, string? macAddress, string? deviceName, JsonElement? payload, string? correlationId = null)
        {
            var envelope = new Dictionary<string, object?>
            {
                ["type"] = type,
                ["deviceId"] = deviceId,
                ["macAddress"] = macAddress,
                ["deviceName"] = deviceName,
                ["payload"] = payload,
                ["correlationId"] = correlationId,
                ["timestamp"] = DateTime.UtcNow
            };
            return JsonSerializer.Serialize(envelope, JsonOptions);
        }

        /// <summary>
        /// Busca un CorrelationId ("CorrelationId" o "correlationId") en el objeto JSON que mandó
        /// el dispositivo como respuesta. Devuelve null si el payload no es un objeto o no lo trae.
        /// </summary>
        private static string? TryExtractCorrelationId(JsonElement? payload)
        {
            if (payload == null || payload.Value.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (payload.Value.TryGetProperty("CorrelationId", out var pascalValue) && pascalValue.ValueKind == JsonValueKind.String)
            {
                return pascalValue.GetString();
            }

            if (payload.Value.TryGetProperty("correlationId", out var camelValue) && camelValue.ValueKind == JsonValueKind.String)
            {
                return camelValue.GetString();
            }

            return null;
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

        /// <summary>
        /// Manda un "error" al usuario indicado a través de WebSocketConnectionManager (no
        /// escribiendo directo al WebSocket), para que el envío quede serializado por el
        /// SendLock de la conexión igual que cualquier otro mensaje y no choque con un envío
        /// concurrente a ese mismo usuario desde otro punto (p. ej. una notificación de otro dispositivo).
        /// </summary>
        private static async Task SendErrorToUserAsync(WebSocketConnectionManager connectionManager, int userId, string message)
        {
            var envelope = BuildEnvelope(WsMessageType.Error, null, null, null, JsonSerializer.SerializeToElement(message));
            await connectionManager.SendToUserAsync(userId, envelope);
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
