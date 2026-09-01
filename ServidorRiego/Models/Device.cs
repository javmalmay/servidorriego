namespace ServidorRiego.Models
{
    /// <summary>
    /// Dispositivo físico de riego registrado en el sistema (válvula, controlador, sensor, etc.)
    /// </summary>
    public class Device
    {
        public int Id { get; set; }

        /// <summary>Dirección MAC del dispositivo, 12 caracteres hexadecimales sin separadores, ej. AABBCCDDEEFF (identificador único de hardware)</summary>
        public string MacAddress { get; set; } = string.Empty;

        /// <summary>Nombre descriptivo asignado al dispositivo</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Configuración del dispositivo almacenada como JSON en crudo</summary>
        public string ConfigJson { get; set; } = "{}";

        /// <summary>Hash SHA-256 del token secreto que usa el dispositivo para autenticar su conexión WebSocket</summary>
        public string? TokenHash { get; set; }

        /// <summary>
        /// Última vez que el dispositivo estuvo activo: al conectar, al desconectar, o al recibir
        /// cualquier mensaje suyo por WebSocket. Se persiste para que sobreviva a un reinicio del
        /// servicio (a diferencia de IsOnline, que es puramente en memoria).
        /// </summary>
        public DateTime? LastSeenAt { get; set; }

        // Nota: IsOnline (si el dispositivo tiene ahora mismo una conexión WebSocket activa) NO
        // se persiste; vive solo en memoria en WebSocketConnectionManager.

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
