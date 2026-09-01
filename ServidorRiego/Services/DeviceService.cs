using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ServidorRiego.Data;
using ServidorRiego.Dtos;
using ServidorRiego.Models;
using ServidorRiego.Realtime;
using ServidorRiego.Utils;

namespace ServidorRiego.Services
{
    public interface IDeviceService
    {
        Task<DeviceListResponse> GetAllForUserAsync(int userId);
        Task<DeviceResponse> GetByIdAsync(int id, int userId);
        Task<DeviceResponse> GetByMacAsync(string macAddress, int userId);
        Task<DeviceResponse> CreateAsync(CreateDeviceRequest request, int creatorUserId);
        Task<DeviceResponse> UpdateAsync(int id, UpdateDeviceRequest request, int userId);
        Task<DeviceResponse> DeleteAsync(int id, int userId);
        Task<DeviceResponse> RegenerateTokenAsync(int id, int userId);
        Task<DeviceUsersResponse> GetAssociatedUsersAsync(int id, int userId);
        Task<DeviceResponse> AssociateUserAsync(int id, string username, int callerUserId);
        Task<DeviceResponse> RemoveUserAsync(int id, string username, int callerUserId);

        // Usados por la capa de WebSockets (no pasan por el filtro de propiedad basado en JWT de usuario)
        Task<Device?> ValidateDeviceTokenAsync(string rawToken);
        Task<bool> IsUserAssociatedAsync(int deviceId, int userId);
        Task<List<int>> GetAssociatedUserIdsAsync(int deviceId);
        Task UpdateLastSeenAsync(int deviceId);
    }

    public class DeviceService : IDeviceService
    {
        // El dispositivo manda la MAC sin separadores (12 caracteres hexadecimales seguidos),
        // no en el formato clásico AA:BB:CC:DD:EE:FF.
        private static readonly Regex MacAddressRegex = new(
            @"^[0-9A-Fa-f]{12}$",
            RegexOptions.Compiled);

        private readonly RiegoDbContext _context;
        private readonly ILogger<DeviceService> _logger;
        private readonly WebSocketConnectionManager _connectionManager;

        public DeviceService(RiegoDbContext context, ILogger<DeviceService> logger, WebSocketConnectionManager connectionManager)
        {
            _context = context;
            _logger = logger;
            _connectionManager = connectionManager;
        }

        public async Task<DeviceListResponse> GetAllForUserAsync(int userId)
        {
            var devices = await _context.UserDevices
                .Where(ud => ud.UserId == userId)
                .Select(ud => ud.Device)
                .OrderBy(d => d.Name)
                .ToListAsync();

            return new DeviceListResponse
            {
                Success = true,
                Message = $"{devices.Count} dispositivo(s) encontrado(s)",
                Devices = devices.Select(MapDeviceToDto).ToList()
            };
        }

        public async Task<DeviceResponse> GetByIdAsync(int id, int userId)
        {
            if (!await IsUserAssociatedAsync(id, userId))
            {
                return NotFoundResponse();
            }

            var device = await _context.Devices.FindAsync(id);
            if (device == null)
            {
                return NotFoundResponse();
            }

            return new DeviceResponse
            {
                Success = true,
                Message = "Dispositivo encontrado",
                Device = MapDeviceToDto(device)
            };
        }

        public async Task<DeviceResponse> GetByMacAsync(string macAddress, int userId)
        {
            var normalizedMac = NormalizeMac(macAddress);
            var device = await _context.Devices
                .FirstOrDefaultAsync(d => d.MacAddress == normalizedMac);

            if (device == null || !await IsUserAssociatedAsync(device.Id, userId))
            {
                return NotFoundResponse();
            }

            return new DeviceResponse
            {
                Success = true,
                Message = "Dispositivo encontrado",
                Device = MapDeviceToDto(device)
            };
        }

        /// <summary>
        /// "Upsert por MAC": si no existe un dispositivo con esa MAC se crea y el usuario que
        /// llama queda asociado como primer propietario (igual que antes). Si ya existe, NO se
        /// vuelve a crear ni se toca su Name/ConfigJson: simplemente se asocia el usuario que
        /// llama a ese dispositivo existente (o se informa si ya estaba asociado).
        /// </summary>
        public async Task<DeviceResponse> CreateAsync(CreateDeviceRequest request, int creatorUserId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.MacAddress))
                {
                    return new DeviceResponse
                    {
                        Success = false,
                        Message = "MacAddress es requerido"
                    };
                }

                var normalizedMac = NormalizeMac(request.MacAddress);
                if (!MacAddressRegex.IsMatch(normalizedMac))
                {
                    return new DeviceResponse
                    {
                        Success = false,
                        Message = "La dirección MAC no tiene un formato válido (esperado AABBCCDDEEFF)"
                    };
                }

                var existingDevice = await _context.Devices
                    .FirstOrDefaultAsync(d => d.MacAddress == normalizedMac);

                if (existingDevice != null)
                {
                    return await LinkUserToExistingDeviceAsync(existingDevice.Id, creatorUserId);
                }

                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    return new DeviceResponse
                    {
                        Success = false,
                        Message = "Name es requerido para crear un dispositivo nuevo"
                    };
                }

                var configJson = request.ConfigJson ?? "{}";
                if (!IsValidJson(configJson))
                {
                    return new DeviceResponse
                    {
                        Success = false,
                        Message = "ConfigJson no contiene un JSON válido"
                    };
                }

                var rawToken = TokenUtils.GenerateSecureToken();
                var device = new Device
                {
                    MacAddress = normalizedMac,
                    Name = request.Name.Trim(),
                    ConfigJson = configJson,
                    TokenHash = TokenUtils.HashToken(rawToken),
                    CreatedAt = DateTime.UtcNow
                };

                _context.Devices.Add(device);
                await _context.SaveChangesAsync();

                // El usuario que crea el dispositivo queda asociado automáticamente (primer propietario)
                _context.UserDevices.Add(new UserDevice
                {
                    UserId = creatorUserId,
                    DeviceId = device.Id,
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();

                return new DeviceResponse
                {
                    Success = true,
                    Message = "Dispositivo creado correctamente",
                    Device = MapDeviceToDto(device),
                    DeviceToken = rawToken
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al crear dispositivo: {ex.Message}");
                return new DeviceResponse
                {
                    Success = false,
                    Message = "Error al crear el dispositivo"
                };
            }
        }

        /// <summary>
        /// Asocia a un usuario a un dispositivo YA EXISTENTE sin exigir que el usuario que llama
        /// ya esté asociado (a diferencia de <see cref="AssociateUserAsync(int, string, int)"/>,
        /// que gestiona compartir acceso entre usuarios ya asociados). Se usa desde el "upsert"
        /// de CreateAsync: cualquier usuario autenticado puede vincularse a un dispositivo físico
        /// existente con solo conocer su MAC. Es idempotente: si ya estaba asociado, no falla.
        /// </summary>
        private async Task<DeviceResponse> LinkUserToExistingDeviceAsync(int deviceId, int userId)
        {
            var alreadyAssociated = await IsUserAssociatedAsync(deviceId, userId);

            var device = await _context.Devices.FindAsync(deviceId);
            if (device == null)
            {
                return NotFoundResponse();
            }

            if (alreadyAssociated)
            {
                return new DeviceResponse
                {
                    Success = true,
                    Message = "Ya estabas asociado a este dispositivo",
                    Device = MapDeviceToDto(device)
                };
            }

            _context.UserDevices.Add(new UserDevice
            {
                UserId = userId,
                DeviceId = deviceId,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            return new DeviceResponse
            {
                Success = true,
                Message = "Dispositivo existente asociado a tu cuenta correctamente",
                Device = MapDeviceToDto(device)
            };
        }

        public async Task<DeviceResponse> UpdateAsync(int id, UpdateDeviceRequest request, int userId)
        {
            try
            {
                if (!await IsUserAssociatedAsync(id, userId))
                {
                    return NotFoundResponse();
                }

                var device = await _context.Devices.FindAsync(id);
                if (device == null)
                {
                    return NotFoundResponse();
                }

                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    return new DeviceResponse
                    {
                        Success = false,
                        Message = "Name es requerido"
                    };
                }

                if (request.ConfigJson != null && !IsValidJson(request.ConfigJson))
                {
                    return new DeviceResponse
                    {
                        Success = false,
                        Message = "ConfigJson no contiene un JSON válido"
                    };
                }

                device.Name = request.Name.Trim();
                if (request.ConfigJson != null)
                {
                    device.ConfigJson = request.ConfigJson;
                }
                device.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                return new DeviceResponse
                {
                    Success = true,
                    Message = "Dispositivo actualizado correctamente",
                    Device = MapDeviceToDto(device)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al actualizar dispositivo {id}: {ex.Message}");
                return new DeviceResponse
                {
                    Success = false,
                    Message = "Error al actualizar el dispositivo"
                };
            }
        }

        /// <summary>
        /// "Elimina" el dispositivo para el usuario que llama: en realidad desasocia al usuario
        /// del dispositivo. Solo se borra el dispositivo de la base de datos (y su token) si, tras
        /// quitar esta asociación, no queda ningún otro usuario asociado a él.
        /// </summary>
        public async Task<DeviceResponse> DeleteAsync(int id, int userId)
        {
            var association = await _context.UserDevices
                .FirstOrDefaultAsync(ud => ud.DeviceId == id && ud.UserId == userId);

            if (association == null)
            {
                return NotFoundResponse();
            }

            _context.UserDevices.Remove(association);

            // Se excluye explícitamente la propia asociación (userId) porque todavía no se ha
            // guardado el Remove de arriba: esta consulta va directa a la BD y no vería el borrado
            // pendiente en el ChangeTracker.
            var otherAssociationsRemain = await _context.UserDevices
                .AnyAsync(ud => ud.DeviceId == id && ud.UserId != userId);

            string message;
            if (otherAssociationsRemain)
            {
                message = "Dispositivo desasociado de tu cuenta correctamente";
            }
            else
            {
                var device = await _context.Devices.FindAsync(id);
                if (device != null)
                {
                    _context.Devices.Remove(device);
                }
                message = "Dispositivo desasociado y eliminado (no quedaban más usuarios asociados)";
            }

            await _context.SaveChangesAsync();

            return new DeviceResponse
            {
                Success = true,
                Message = message
            };
        }

        public async Task<DeviceResponse> RegenerateTokenAsync(int id, int userId)
        {
            if (!await IsUserAssociatedAsync(id, userId))
            {
                return NotFoundResponse();
            }

            var device = await _context.Devices.FindAsync(id);
            if (device == null)
            {
                return NotFoundResponse();
            }

            var rawToken = TokenUtils.GenerateSecureToken();
            device.TokenHash = TokenUtils.HashToken(rawToken);
            device.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return new DeviceResponse
            {
                Success = true,
                Message = "Token regenerado correctamente. El dispositivo deberá reconectarse con el nuevo token",
                Device = MapDeviceToDto(device),
                DeviceToken = rawToken
            };
        }

        public async Task<DeviceUsersResponse> GetAssociatedUsersAsync(int id, int userId)
        {
            if (!await IsUserAssociatedAsync(id, userId))
            {
                return new DeviceUsersResponse
                {
                    Success = false,
                    Message = "Dispositivo no encontrado"
                };
            }

            var users = await _context.UserDevices
                .Where(ud => ud.DeviceId == id)
                .Select(ud => ud.User)
                .OrderBy(u => u.Username)
                .Select(u => new UserSummaryDto { Id = u.Id, Username = u.Username, Email = u.Email })
                .ToListAsync();

            return new DeviceUsersResponse
            {
                Success = true,
                Message = $"{users.Count} usuario(s) asociado(s)",
                Users = users
            };
        }

        public async Task<DeviceResponse> AssociateUserAsync(int id, string username, int callerUserId)
        {
            if (!await IsUserAssociatedAsync(id, callerUserId))
            {
                return NotFoundResponse();
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                return new DeviceResponse { Success = false, Message = "Username es requerido" };
            }

            var targetUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (targetUser == null)
            {
                return new DeviceResponse { Success = false, Message = "El usuario indicado no existe" };
            }

            var alreadyAssociated = await _context.UserDevices
                .AnyAsync(ud => ud.DeviceId == id && ud.UserId == targetUser.Id);
            if (alreadyAssociated)
            {
                return new DeviceResponse { Success = false, Message = "Ese usuario ya está asociado al dispositivo" };
            }

            _context.UserDevices.Add(new UserDevice
            {
                UserId = targetUser.Id,
                DeviceId = id,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            var device = await _context.Devices.FindAsync(id);
            return new DeviceResponse
            {
                Success = true,
                Message = $"Usuario '{username}' asociado correctamente",
                Device = device != null ? MapDeviceToDto(device) : null
            };
        }

        public async Task<DeviceResponse> RemoveUserAsync(int id, string username, int callerUserId)
        {
            if (!await IsUserAssociatedAsync(id, callerUserId))
            {
                return NotFoundResponse();
            }

            var association = await _context.UserDevices
                .Include(ud => ud.User)
                .FirstOrDefaultAsync(ud => ud.DeviceId == id && ud.User.Username == username);

            if (association == null)
            {
                return new DeviceResponse { Success = false, Message = "Ese usuario no está asociado al dispositivo" };
            }

            var associatedCount = await _context.UserDevices.CountAsync(ud => ud.DeviceId == id);
            if (associatedCount <= 1)
            {
                return new DeviceResponse
                {
                    Success = false,
                    Message = "No se puede quitar al último usuario asociado; elimina el dispositivo en su lugar"
                };
            }

            _context.UserDevices.Remove(association);
            await _context.SaveChangesAsync();

            return new DeviceResponse
            {
                Success = true,
                Message = $"Usuario '{username}' desasociado correctamente"
            };
        }

        public async Task<Device?> ValidateDeviceTokenAsync(string rawToken)
        {
            if (string.IsNullOrWhiteSpace(rawToken))
            {
                return null;
            }

            var tokenHash = TokenUtils.HashToken(rawToken);
            return await _context.Devices.FirstOrDefaultAsync(d => d.TokenHash == tokenHash);
        }

        public async Task<bool> IsUserAssociatedAsync(int deviceId, int userId)
        {
            return await _context.UserDevices.AnyAsync(ud => ud.DeviceId == deviceId && ud.UserId == userId);
        }

        public async Task<List<int>> GetAssociatedUserIdsAsync(int deviceId)
        {
            return await _context.UserDevices
                .Where(ud => ud.DeviceId == deviceId)
                .Select(ud => ud.UserId)
                .ToListAsync();
        }

        /// <summary>
        /// Actualiza LastSeenAt a la hora actual sin cargar la entidad completa (UPDATE directo),
        /// pensado para llamarse con mucha frecuencia: al conectar, al desconectar y en cada
        /// mensaje que llegue del dispositivo por WebSocket.
        /// </summary>
        public async Task UpdateLastSeenAsync(int deviceId)
        {
            await _context.Devices
                .Where(d => d.Id == deviceId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(d => d.LastSeenAt, DateTime.UtcNow));
        }

        private static DeviceResponse NotFoundResponse() => new()
        {
            Success = false,
            Message = "Dispositivo no encontrado"
        };

        /// <summary>Quita separadores (":" o "-", por si acaso) y deja la MAC en mayúsculas, 12 hex seguidos</summary>
        private static string NormalizeMac(string mac)
        {
            mac = mac.Trim().ToUpperInvariant();
            mac = mac.Replace(":", "").Replace("-", "");
            return mac;
        }

        private static bool IsValidJson(string value)
        {
            try
            {
                using var _ = JsonDocument.Parse(value);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>
        /// Mapea la entidad a DTO. IsOnline se consulta en vivo al WebSocketConnectionManager
        /// (estado en memoria de las conexiones activas, no sobrevive a un reinicio del servicio).
        /// LastSeenAt viene de la base de datos (device.LastSeenAt), así que sí sobrevive a un
        /// reinicio: refleja la última vez que el dispositivo estuvo activo.
        /// </summary>
        private DeviceDto MapDeviceToDto(Device device)
        {
            return new DeviceDto
            {
                Id = device.Id,
                MacAddress = device.MacAddress,
                Name = device.Name,
                ConfigJson = device.ConfigJson,
                CreatedAt = device.CreatedAt,
                UpdatedAt = device.UpdatedAt,
                IsOnline = _connectionManager.IsDeviceConnected(device.Id),
                LastSeenAt = device.LastSeenAt
            };
        }
    }
}
