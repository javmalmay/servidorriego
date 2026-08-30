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
