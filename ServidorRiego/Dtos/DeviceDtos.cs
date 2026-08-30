namespace ServidorRiego.Dtos
{
    /// <summary>
    /// Información de un dispositivo de riego
    /// </summary>
    public class DeviceDto
    {
        /// <summary>Identificador único del dispositivo</summary>
        public int Id { get; set; }

        /// <summary>Dirección MAC del dispositivo (formato AA:BB:CC:DD:EE:FF)</summary>
        public string MacAddress { get; set; } = string.Empty;

        /// <summary>Nombre descriptivo del dispositivo</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Configuración del dispositivo en formato JSON</summary>
        public string ConfigJson { get; set; } = string.Empty;

        /// <summary>Fecha de creación del registro</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>Fecha de la última actualización (null si nunca se ha modificado)</summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>Indica si el dispositivo tiene actualmente una conexión WebSocket activa</summary>
        public bool IsOnline { get; set; }

        /// <summary>Última vez que el dispositivo se conectó o desconectó (null si nunca se ha conectado)</summary>
        public DateTime? LastSeenAt { get; set; }
    }

    /// <summary>
    /// Solicitud para crear un nuevo dispositivo
    /// </summary>
    public class CreateDeviceRequest
    {
        /// <summary>Dirección MAC del dispositivo (formato AA:BB:CC:DD:EE:FF), debe ser única</summary>
        public string MacAddress { get; set; } = string.Empty;

        /// <summary>Nombre descriptivo del dispositivo</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Configuración inicial en formato JSON (opcional, por defecto "{}")</summary>
        public string? ConfigJson { get; set; }
    }

    /// <summary>
    /// Solicitud para actualizar un dispositivo existente (la MAC no se puede modificar)
    /// </summary>
    public class UpdateDeviceRequest
    {
        /// <summary>Nuevo nombre descriptivo del dispositivo</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Nueva configuración en formato JSON</summary>
        public string? ConfigJson { get; set; }
    }

    /// <summary>
    /// Respuesta con un único dispositivo
    /// </summary>
    public class DeviceResponse
    {
        /// <summary>Indica si la operación fue exitosa</summary>
        public bool Success { get; set; }

        /// <summary>Mensaje descriptivo de la respuesta</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Dispositivo afectado (null si la operación no fue exitosa)</summary>
        public DeviceDto? Device { get; set; }

        /// <summary>
        /// Token secreto del dispositivo, en claro. Solo se devuelve al crear el dispositivo
        /// o al regenerar el token; no se puede recuperar después (solo se guarda su hash).
        /// El dispositivo debe presentarlo para abrir su conexión WebSocket en /ws/device.
        /// </summary>
        public string? DeviceToken { get; set; }
    }

    /// <summary>
    /// Solicitud para asociar (compartir acceso con) otro usuario a un dispositivo
    /// </summary>
    public class AssociateUserRequest
    {
        /// <summary>Nombre de usuario a asociar</summary>
        public string Username { get; set; } = string.Empty;
    }

    /// <summary>
    /// Información básica de un usuario asociado a un dispositivo
    /// </summary>
    public class UserSummaryDto
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    /// <summary>
    /// Respuesta con el listado de usuarios asociados a un dispositivo
    /// </summary>
    public class DeviceUsersResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<UserSummaryDto> Users { get; set; } = new();
    }

    /// <summary>
    /// Respuesta con un listado de dispositivos
    /// </summary>
    public class DeviceListResponse
    {
        /// <summary>Indica si la operación fue exitosa</summary>
        public bool Success { get; set; }

        /// <summary>Mensaje descriptivo de la respuesta</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Listado de dispositivos</summary>
        public List<DeviceDto> Devices { get; set; } = new();
    }
}
