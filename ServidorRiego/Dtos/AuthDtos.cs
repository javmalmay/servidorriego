namespace ServidorRiego.Dtos
{
    /// <summary>
    /// Solicitud de login
    /// </summary>
    public class LoginRequest
    {
        /// <summary>Nombre de usuario</summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>Contraseña del usuario</summary>
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>
    /// Respuesta de login
    /// </summary>
    public class LoginResponse
    {
        /// <summary>Indica si el login fue exitoso</summary>
        public bool Success { get; set; }

        /// <summary>Mensaje descriptivo de la respuesta</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Token JWT de acceso, de corta duración (null si no fue exitoso)</summary>
        public string? Token { get; set; }

        /// <summary>Fecha y hora (UTC) en la que caduca el token de acceso (null si no fue exitoso)</summary>
        public DateTime? TokenExpire { get; set; }

        /// <summary>Token de renovación, de larga duración, para obtener un nuevo token de acceso sin re-loguearse (null si no fue exitoso)</summary>
        public string? RefreshToken { get; set; }

        /// <summary>Fecha y hora (UTC) en la que caduca el refresh token (null si no fue exitoso)</summary>
        public DateTime? RefreshTokenExpire { get; set; }

        /// <summary>Datos del usuario autenticado (null si no fue exitoso)</summary>
        public UserDto? User { get; set; }
    }

    /// <summary>
    /// Solicitud para renovar el token de acceso usando un refresh token válido
    /// </summary>
    public class RefreshTokenRequest
    {
        /// <summary>Refresh token obtenido previamente en login o en una renovación anterior</summary>
        public string RefreshToken { get; set; } = string.Empty;
    }

    /// <summary>
    /// Respuesta genérica de operaciones sin datos adicionales (p. ej. logout)
    /// </summary>
    public class ApiResponse
    {
        /// <summary>Indica si la operación fue exitosa</summary>
        public bool Success { get; set; }

        /// <summary>Mensaje descriptivo de la respuesta</summary>
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Información de usuario (sin datos sensibles)
    /// </summary>
    public class UserDto
    {
        /// <summary>Identificador único del usuario</summary>
        public int Id { get; set; }

        /// <summary>Nombre de usuario</summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>Email del usuario</summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>Fecha de creación de la cuenta</summary>
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Solicitud de registro
    /// </summary>
    public class RegisterRequest
    {
        /// <summary>Nombre de usuario (debe ser único, 3-50 caracteres)</summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>Email del usuario (debe ser único y válido)</summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>Contraseña del usuario (se almacenará hasheada con BCrypt)</summary>
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>
    /// Respuesta de registro
    /// </summary>
    public class RegisterResponse
    {
        /// <summary>Indica si el registro fue exitoso</summary>
        public bool Success { get; set; }

        /// <summary>Mensaje descriptivo de la respuesta</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Datos del usuario creado (null si no fue exitoso)</summary>
        public UserDto? User { get; set; }
    }
}
