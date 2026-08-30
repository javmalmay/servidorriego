using Microsoft.EntityFrameworkCore;
using ServidorRiego.Models;

namespace ServidorRiego.Data
{
    public class RiegoDbContext : DbContext
    {
        public RiegoDbContext(DbContextOptions<RiegoDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<Device> Devices { get; set; }
        public DbSet<UserDevice> UserDevices { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configuración de la tabla Users
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Username)
                    .IsRequired()
                    .HasMaxLength(50);
                entity.Property(e => e.Email)
                    .IsRequired()
                    .HasMaxLength(100);
                entity.Property(e => e.PasswordHash)
                    .IsRequired();
                entity.Property(e => e.CreatedAt)
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");

                // Índice único en Username
                entity.HasIndex(e => e.Username)
                    .IsUnique();
                entity.HasIndex(e => e.Email)
                    .IsUnique();
            });

            // Configuración de la tabla RefreshTokens
            modelBuilder.Entity<RefreshToken>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.TokenHash)
                    .IsRequired();
                entity.Property(e => e.CreatedAt)
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");

                // El hash del token debe ser único para poder buscarlo directamente
                entity.HasIndex(e => e.TokenHash)
                    .IsUnique();

                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Configuración de la tabla Devices
            modelBuilder.Entity<Device>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.MacAddress)
                    .IsRequired()
                    .HasMaxLength(17); // AA:BB:CC:DD:EE:FF
                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasMaxLength(100);
                entity.Property(e => e.ConfigJson)
                    .IsRequired();
                entity.Property(e => e.CreatedAt)
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");

                // La MAC identifica de forma única al dispositivo físico
                entity.HasIndex(e => e.MacAddress)
                    .IsUnique();
            });

            // Configuración de la tabla UserDevices (asociación muchos-a-muchos)
            modelBuilder.Entity<UserDevice>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.CreatedAt)
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");

                // Un usuario solo puede estar asociado una vez al mismo dispositivo
                entity.HasIndex(e => new { e.UserId, e.DeviceId })
                    .IsUnique();

                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Device)
                    .WithMany()
                    .HasForeignKey(e => e.DeviceId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
