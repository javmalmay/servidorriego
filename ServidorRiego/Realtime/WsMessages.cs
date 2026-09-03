using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServidorRiego.Realtime
{
    /// <summary>
    /// Comando recibido por WebSocket desde un usuario para controlar uno de sus dispositivos
    /// asociados. Es prácticamente el único tipo de mensaje que se espera del lado usuario.
    /// Parametros se deja como JsonElement para aceptar cualquier estructura (string, objeto,
    /// número, etc.) definida por el comando concreto; el servidor no la interpreta.
    /// </summary>
    public class DeviceCommandRequest
    {
        [JsonPropertyName("deviceId")]
        public int DeviceId { get; set; }

        [JsonPropertyName("comando")]
        public string Comando { get; set; } = string.Empty;

        [JsonPropertyName("parametros")]
        public JsonElement? Parametros { get; set; }
    }

    /// <summary>
    /// Lo que se reenvía de verdad al dispositivo: el mismo comando pero sin DeviceId, más un
    /// CorrelationId generado por el servidor. El dispositivo debe devolver ese mismo
    /// CorrelationId en su respuesta para que el servidor sepa a qué comando (y a qué usuario)
    /// corresponde; si no lo devuelve, el servidor hace un mejor esfuerzo (ver
    /// <see cref="WebSocketConnectionManager.TryResolveCommand"/>). Los nombres de campo se fijan
    /// explícitamente en PascalCase mediante [JsonPropertyName], para que no cambien aunque la
    /// política de serialización por defecto sea camelCase.
    /// </summary>
    public class DeviceCommandForward
    {
        [JsonPropertyName("Comando")]
        public string Comando { get; set; } = string.Empty;

        [JsonPropertyName("Parametros")]
        public JsonElement? Parametros { get; set; }

        [JsonPropertyName("CorrelationId")]
        public string CorrelationId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Tipos de mensaje usados en el protocolo WebSocket de ServidorRiego.
    ///
    /// Usuario -&gt; Servidor (ver <see cref="DeviceCommandRequest"/>):
    ///   { "DeviceId": 5, "Comando": "abrir_valvula", "Parametros": ... }
    ///   El servidor localiza el dispositivo por DeviceId, comprueba que el usuario está
    ///   asociado a él, genera un CorrelationId nuevo y le reenvía al dispositivo (ver
    ///   <see cref="DeviceCommandForward"/>): { "Comando": ..., "Parametros": ..., "CorrelationId": "..." }
    ///
    /// Dispositivo -&gt; Servidor: se espera que la respuesta a un comando incluya el mismo
    /// CorrelationId (como "CorrelationId" o "correlationId" en el JSON, en cualquier nivel del
    /// objeto raíz) que se le mandó. Con eso el servidor sabe exactamente a qué comando y a qué
    /// usuario corresponde, incluso si hay varios comandos en curso a la vez para ese dispositivo.
    ///   - Si el CorrelationId es reconocible -&gt; se reenvía SOLO al usuario que mandó ese comando,
    ///     como "command_response" (incluye el mismo correlationId).
    ///   - Si el dispositivo no devuelve ningún CorrelationId pero hay comandos en curso para él,
    ///     se asume (mejor esfuerzo) que responde al más antiguo todavía pendiente.
    ///   - Si no hay ningún comando en curso para ese dispositivo, se trata como telemetría/estado
    ///     espontáneo y se difunde a TODOS los usuarios asociados, como "device_status".
    ///
    /// Servidor -&gt; Usuario (envueltos con type/deviceId/macAddress/deviceName/payload/correlationId/timestamp):
    ///   "command_sent"     - confirmación inmediata de que el comando se reenvió al dispositivo,
    ///                         con el CorrelationId a guardar para casarlo luego con su respuesta
    ///   "command_response" - respuesta del dispositivo a un comando que este usuario envió
    ///   "device_status"    - telemetría/estado espontáneo de un dispositivo asociado
    ///   "device_online"    - un dispositivo asociado se ha conectado
    ///   "device_offline"   - un dispositivo asociado se ha desconectado
    ///
    /// Cualquier dirección:
    ///   "error" { message } - la operación solicitada no se pudo completar
    /// </summary>
    public static class WsMessageType
    {
        public const string CommandSent = "command_sent";
        public const string CommandResponse = "command_response";
        public const string DeviceStatus = "device_status";
        public const string DeviceOnline = "device_online";
        public const string DeviceOffline = "device_offline";
        public const string Error = "error";
    }
}
