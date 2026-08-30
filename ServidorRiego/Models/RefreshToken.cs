namespace ServidorRiego.Models
{
    /// <summary>
    /// Token de renovación asociado a un usuario, usado para obtener nuevos
    /// JWT sin requerir que el usuario vuelva a introducir sus credenciales.
    /// Se almacena en la base de datos el hash del token, nunca el valor en claro.
    /// </summary>
    public class RefreshToken
    {
        public int Id { get; set; }

        /// <summary>Hash SHA-256 del token (el valor en claro solo se entrega al cliente una vez)</summary>
        public string TokenHash { get; set; } = string.Empty;

        public int UserId { get; set; }
        public User User { get; set; } = null!;

        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }

        /// <summary>Fecha en la que el token fue revocado (logout o rotación), null si sigue vivo</summary>
        public DateTime? RevokedAt { get; set; }

        /// <summary>Hash del token que reemplazó a este al rotarlo, para trazabilidad</summary>
        public string? ReplacedByTokenHash { get; set; }

        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
        public bool IsRevoked => RevokedAt != null;
        public bool IsActive => !IsRevoked && !IsExpired;
    }
}
