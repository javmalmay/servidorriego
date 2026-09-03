using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace ServidorRiego.Realtime
{
    /// <summary>
    /// Registro en memoria de las conexiones WebSocket activas, separadas por dispositivo y por usuario.
    /// Es un singleton: vive mientras viva el proceso, no se persiste en base de datos.
    /// Cada WebSocket lleva asociado un SemaphoreSlim para serializar los envíos, ya que
    /// System.Net.WebSockets.WebSocket no admite llamadas a SendAsync concurrentes sobre la misma instancia.
    /// </summary>
    public class WebSocketConnectionManager
    {
        private class Connection
        {
            public required Guid Id { get; init; }
            public required WebSocket Socket { get; init; }
            public SemaphoreSlim SendLock { get; } = new(1, 1);
        }

        private readonly ConcurrentDictionary<int, ConcurrentDictionary<Guid, Connection>> _deviceConnections = new();
        private readonly ConcurrentDictionary<int, ConcurrentDictionary<Guid, Connection>> _userConnections = new();

        private class PendingCommand
        {
            public required int DeviceId { get; init; }
            public required int UserId { get; init; }
            public required DateTime SentAt { get; init; }
        }

        // Correlación comando -> respuesta, por CorrelationId (no por dispositivo): permite varios
        // comandos en curso a la vez para el mismo dispositivo sin que se pisen entre sí. Solo en
        // memoria; se pierde si el proceso se reinicia (los comandos en curso quedarían sin respuesta).
        private readonly ConcurrentDictionary<string, PendingCommand> _pendingCommands = new();

        private readonly ILogger<WebSocketConnectionManager> _logger;

        public WebSocketConnectionManager(ILogger<WebSocketConnectionManager> logger)
        {
            _logger = logger;
        }

        public Guid AddDeviceConnection(int deviceId, WebSocket socket)
        {
            var id = Guid.NewGuid();
            var bucket = _deviceConnections.GetOrAdd(deviceId, _ => new ConcurrentDictionary<Guid, Connection>());
            bucket[id] = new Connection { Id = id, Socket = socket };
            return id;
        }

        public void RemoveDeviceConnection(int deviceId, Guid connectionId)
        {
            if (_deviceConnections.TryGetValue(deviceId, out var bucket))
            {
                bucket.TryRemove(connectionId, out _);
                if (bucket.IsEmpty)
                {
                    _deviceConnections.TryRemove(deviceId, out _);
                }
            }
        }

        public Guid AddUserConnection(int userId, WebSocket socket)
        {
            var id = Guid.NewGuid();
            var bucket = _userConnections.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, Connection>());
            bucket[id] = new Connection { Id = id, Socket = socket };
            return id;
        }

        public void RemoveUserConnection(int userId, Guid connectionId)
        {
            if (_userConnections.TryGetValue(userId, out var bucket))
            {
                bucket.TryRemove(connectionId, out _);
                if (bucket.IsEmpty)
                {
                    _userConnections.TryRemove(userId, out _);
                }
            }
        }

        public bool IsDeviceConnected(int deviceId) =>
            _deviceConnections.TryGetValue(deviceId, out var bucket) && !bucket.IsEmpty;

        /// <summary>
        /// Genera un CorrelationId nuevo y registra que la respuesta a ese comando (cuando el
        /// dispositivo la mande) hay que reenviársela a este usuario. El CorrelationId generado
        /// es el que hay que incluir en el mensaje que se le manda al dispositivo.
        /// </summary>
        public string RegisterPendingCommand(int deviceId, int userId)
        {
            var correlationId = Guid.NewGuid().ToString("N");
            _pendingCommands[correlationId] = new PendingCommand
            {
                DeviceId = deviceId,
                UserId = userId,
                SentAt = DateTime.UtcNow
            };
            return correlationId;
        }

        /// <summary>
        /// Intenta averiguar a qué usuario hay que reenviarle un mensaje recibido de un dispositivo,
        /// y con qué CorrelationId. Si el dispositivo devolvió un correlationId reconocible (y
        /// corresponde a ESE dispositivo) se usa directamente. Si no devolvió ninguno pero hay
        /// comandos en curso para ese dispositivo, se asume (mejor esfuerzo) que responde al más
        /// antiguo todavía pendiente. Si no hay ningún comando pendiente para el dispositivo,
        /// devuelve false (el mensaje se trata como telemetría espontánea, no como respuesta).
        /// </summary>
        public bool TryResolveCommand(int deviceId, string? correlationId, out int userId, out string? resolvedCorrelationId)
        {
            if (!string.IsNullOrEmpty(correlationId)
                && _pendingCommands.TryGetValue(correlationId, out var exact)
                && exact.DeviceId == deviceId
                && _pendingCommands.TryRemove(correlationId, out _))
            {
                userId = exact.UserId;
                resolvedCorrelationId = correlationId;
                return true;
            }

            var oldestPending = _pendingCommands
                .Where(kv => kv.Value.DeviceId == deviceId)
                .OrderBy(kv => kv.Value.SentAt)
                .Select(kv => (Key: kv.Key, Value: kv.Value))
                .FirstOrDefault();

            if (oldestPending.Key != null && _pendingCommands.TryRemove(oldestPending.Key, out _))
            {
                userId = oldestPending.Value.UserId;
                resolvedCorrelationId = oldestPending.Key;
                return true;
            }

            userId = 0;
            resolvedCorrelationId = null;
            return false;
        }

        /// <summary>Descarta un comando pendiente concreto (p. ej. si no se pudo entregar al dispositivo)</summary>
        public void ClearPendingCommand(string correlationId) =>
            _pendingCommands.TryRemove(correlationId, out _);

        /// <summary>Descarta todos los comandos pendientes de un dispositivo, p. ej. al desconectarse</summary>
        public void ClearPendingCommandsForDevice(int deviceId)
        {
            foreach (var key in _pendingCommands.Where(kv => kv.Value.DeviceId == deviceId).Select(kv => kv.Key).ToList())
            {
                _pendingCommands.TryRemove(key, out _);
            }
        }

        /// <summary>Envía un mensaje de texto a todas las conexiones activas de un dispositivo. Devuelve true si había al menos una.</summary>
        public async Task<bool> SendToDeviceAsync(int deviceId, string jsonMessage, CancellationToken cancellationToken = default)
        {
            if (!_deviceConnections.TryGetValue(deviceId, out var bucket) || bucket.IsEmpty)
            {
                return false;
            }

            await BroadcastAsync(bucket.Values, jsonMessage, cancellationToken);
            return true;
        }

        /// <summary>Envía un mensaje de texto a todas las conexiones activas de un usuario (puede tener varias: varios dispositivos/pestañas).</summary>
        public async Task<bool> SendToUserAsync(int userId, string jsonMessage, CancellationToken cancellationToken = default)
        {
            if (!_userConnections.TryGetValue(userId, out var bucket) || bucket.IsEmpty)
            {
                return false;
            }

            await BroadcastAsync(bucket.Values, jsonMessage, cancellationToken);
            return true;
        }

        private async Task BroadcastAsync(IEnumerable<Connection> connections, string jsonMessage, CancellationToken cancellationToken)
        {
            var bytes = Encoding.UTF8.GetBytes(jsonMessage);
            foreach (var connection in connections)
            {
                if (connection.Socket.State != WebSocketState.Open)
                {
                    continue;
                }

                await connection.SendLock.WaitAsync(cancellationToken);
                try
                {
                    if (connection.Socket.State == WebSocketState.Open)
                    {
                        await connection.Socket.SendAsync(
                            new ArraySegment<byte>(bytes),
                            WebSocketMessageType.Text,
                            endOfMessage: true,
                            cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Error enviando mensaje por WebSocket: {ex.Message}");
                }
                finally
                {
                    connection.SendLock.Release();
                }
            }
        }
    }
}
