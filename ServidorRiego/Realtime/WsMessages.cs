using System.Text.Json.Serialization;

namespace ServidorRiego.Realtime
{
    /// <summary>
    /// Mensaje entrante recibido por WebSocket (de un dispositivo o de un usuario).
    /// El campo Payload se deja como JsonElement para aceptar cualquier estructura definida
    /// por el cliente (firmware o app Android), el servidor solo la reenvía sin interpretarla.
    /// </summary>
    public class WsIncomingMessage
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        /// <summary>Id del dispositivo destino (solo aplica a mensajes tipo "command" enviados por un usuario)</summary>
        [JsonPropertyName("deviceId")]
        public int? DeviceId { get; set; }

        [JsonPropertyName("payload")]
        public System.Text.Json.JsonElement? Payload { get; set; }
    }

    /// <summary>
    /// Tipos de mensaje usados en el protocolo WebSocket de ServidorRiego.
    ///
    /// Dispositivo -&gt; Servidor:
    ///   "status"  - telemetría/estado del dispositivo, payload libre (ej: {"válvula":"abierta"})
    ///
    /// Servidor -&gt; Dispositivo:
    ///   "command" - orden a ejecutar, payload libre (ej: {"acción":"abrir_válvula"})
    ///
    /// Servidor -&gt; Usuario:
    ///   "device_status"  - reenvío del último "status" recibido de un dispositivo asociado
    ///   "device_online"  - un dispositivo asociado se ha conectado
    ///   "device_offline" - un dispositivo asociado se ha desconectado
    ///
    /// Usuario -&gt; Servidor:
    ///   "command" { deviceId, payload } - enviar una orden a uno de sus dispositivos asociados
    ///
    /// Cualquier dirección:
    ///   "error" { message } - la operación solicitada no se pudo completar
    /// </summary>
    public static class WsMessageType
    {
        public const string Status = "status";
        public const string Command = "command";
        public const string DeviceStatus = "device_status";
        public const string DeviceOnline = "device_online";
        public const string DeviceOffline = "device_offline";
        public const string Error = "error";
    }
}
