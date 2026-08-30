namespace ServidorRiego.Models
{
    /// <summary>
    /// Asociación muchos-a-muchos entre usuarios y dispositivos: qué usuarios tienen
    /// acceso (visibilidad y control) sobre un dispositivo de riego dado.
    /// </summary>
    public class UserDevice
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User User { get; set; } = null!;

        public int DeviceId { get; set; }
        public Device Device { get; set; } = null!;

        /// <summary>Fecha en la que se asoció el usuario al dispositivo</summary>
        public DateTime CreatedAt { get; set; }
    }
}
