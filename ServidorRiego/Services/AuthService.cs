using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ServidorRiego.Data;
using ServidorRiego.Dtos;
using ServidorRiego.Models;
using ServidorRiego.Utils;

namespace ServidorRiego.Services
{
    public interface IAuthService
    {
        Task<LoginResponse> LoginAsync(LoginRequest request);
        Task<RegisterResponse> RegisterAsync(RegisterRequest request);
        Task<UserDto?> GetUserByIdAsync(int userId);
        Task<LoginResponse> RefreshTokenAsync(RefreshTokenRequest request);
        Task<bool> RevokeTokenAsync(string refreshToken);
    }

    public class AuthService : IAuthService
    {
        private readonly RiegoDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthService> _logger;
        private readonly int _refreshTokenExpirationDays;

        public AuthService(RiegoDbContext context, IConfiguration configuration, ILogger<AuthService> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
            _refreshTokenExpirationDays = int.Parse(_configuration["Jwt:RefreshTokenExpirationDays"] ?? "30");
        }

        public async Task<LoginResponse> LoginAsync(LoginRequest request)
        {
            try
            {
                // Validar entrada
                if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                {
                    return new LoginResponse
                    {
                        Success = false,
                        Message = "Usuario y contraseña son requeridos"
                    };
                }

                // Buscar usuario
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == request.Username);

                if (user == null || !VerifyPassword(request.Password, user.PasswordHash))
                {
                    return new LoginResponse
                    {
                        Success = false,
                        Message = "Usuario o contraseña inválidos"
                    };
                }

                if (!user.IsActive)
                {
                    return new LoginResponse
                    {
                        Success = false,
                        Message = "La cuenta de usuario está desactivada"
                    };
                }

                // Generar token JWT de acceso y refresh token
                var (token, tokenExpire) = GenerateJwtToken(user);
                var (rawRefreshToken, refreshTokenEntity) = CreateRefreshToken(user.Id);

                // Actualizar último login
                user.LastLogin = DateTime.UtcNow;
                _context.Users.Update(user);
                await _context.SaveChangesAsync();

                return new LoginResponse
                {
                    Success = true,
                    Message = "Login exitoso",
                    Token = token,
                    TokenExpire = tokenExpire,
                    RefreshToken = rawRefreshToken,
                    RefreshTokenExpire = refreshTokenEntity.ExpiresAt,
                    User = MapUserToDto(user)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error en login: {ex.Message}");
                return new LoginResponse
                {
                    Success = false,
                    Message = "Error al procesar login"
                };
            }
        }

        public async Task<RegisterResponse> RegisterAsync(RegisterRequest request)
        {
            try
            {
                // Validar entrada
                if (string.IsNullOrWhiteSpace(request.Username) || 
                    string.IsNullOrWhiteSpace(request.Email) || 
                    string.IsNullOrWhiteSpace(request.Password))
                {
                    return new RegisterResponse
                    {
                        Success = false,
                        Message = "Usuario, email y contraseña son requeridos"
                    };
                }

                // Validar que el usuario no exista
                var existingUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username == request.Username || u.Email == request.Email);

                if (existingUser != null)
                {
                    return new RegisterResponse
                    {
                        Success = false,
                        Message = "El usuario o email ya existe"
                    };
                }

                // Crear nuevo usuario
                var newUser = new User
                {
                    Username = request.Username,
                    Email = request.Email,
                    PasswordHash = HashPassword(request.Password),
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };

                _context.Users.Add(newUser);
                await _context.SaveChangesAsync();

                return new RegisterResponse
                {
                    Success = true,
                    Message = "Registro exitoso",
                    User = MapUserToDto(newUser)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error en registro: {ex.Message}");
                return new RegisterResponse
                {
                    Success = false,
                    Message = "Error al procesar registro"
                };
            }
        }

        public async Task<UserDto?> GetUserByIdAsync(int userId)
        {
            var user = await _context.Users.FindAsync(userId);
            return user != null ? MapUserToDto(user) : null;
        }

        public async Task<LoginResponse> RefreshTokenAsync(RefreshTokenRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.RefreshToken))
                {
                    return new LoginResponse
                    {
                        Success = false,
                        Message = "Refresh token requerido"
                    };
                }

                var tokenHash = TokenUtils.HashToken(request.RefreshToken);
                var storedToken = await _context.RefreshTokens
                    .Include(rt => rt.User)
                    .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);

                if (storedToken == null || !storedToken.IsActive)
                {
                    return new LoginResponse
                    {
                        Success = false,
                        Message = "Refresh token inválido o expirado"
                    };
                }

                var user = storedToken.User;
                if (!user.IsActive)
                {
                    return new LoginResponse
                    {
                        Success = false,
                        Message = "La cuenta de usuario está desactivada"
                    };
                }

                // Rotar el refresh token: se revoca el usado y se emite uno nuevo
                storedToken.RevokedAt = DateTime.UtcNow;
                var (rawRefreshToken, newTokenEntity) = CreateRefreshToken(user.Id);
                storedToken.ReplacedByTokenHash = newTokenEntity.TokenHash;

                var (newAccessToken, newTokenExpire) = GenerateJwtToken(user);

                await _context.SaveChangesAsync();

                return new LoginResponse
                {
                    Success = true,
                    Message = "Token renovado",
                    Token = newAccessToken,
                    TokenExpire = newTokenExpire,
                    RefreshToken = rawRefreshToken,
                    RefreshTokenExpire = newTokenEntity.ExpiresAt,
                    User = MapUserToDto(user)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al renovar token: {ex.Message}");
                return new LoginResponse
                {
                    Success = false,
                    Message = "Error al procesar la renovación del token"
                };
            }
        }

        public async Task<bool> RevokeTokenAsync(string refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return false;
            }

            var tokenHash = TokenUtils.HashToken(refreshToken);
            var storedToken = await _context.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);

            if (storedToken == null || !storedToken.IsActive)
            {
                return false;
            }

            storedToken.RevokedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Crea una entidad de refresh token para el usuario y la registra en el contexto
        /// (sin guardar cambios todavía). Devuelve el valor en claro para entregar al cliente
        /// junto con la entidad para poder enlazarla (p. ej. rotación).
        /// </summary>
        private (string rawToken, RefreshToken entity) CreateRefreshToken(int userId)
        {
            var rawToken = TokenUtils.GenerateSecureToken();
            var entity = new RefreshToken
            {
                TokenHash = TokenUtils.HashToken(rawToken),
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(_refreshTokenExpirationDays)
            };

            _context.RefreshTokens.Add(entity);
            return (rawToken, entity);
        }

        private (string token, DateTime expiresAt) GenerateJwtToken(User user)
        {
            var jwtKey = _configuration["Jwt:Key"];
            var jwtIssuer = _configuration["Jwt:Issuer"];
            var jwtAudience = _configuration["Jwt:Audience"];
            var jwtExpirationMinutes = int.Parse(_configuration["Jwt:ExpirationMinutes"] ?? "60");

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(jwtKey ?? throw new InvalidOperationException("JWT Key no configurado"));
            var expiresAt = DateTime.UtcNow.AddMinutes(jwtExpirationMinutes);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim("userId", user.Id.ToString()),
                    new Claim("username", user.Username),
                    new Claim("email", user.Email)
                }),
                Expires = expiresAt,
                Issuer = jwtIssuer,
                Audience = jwtAudience,
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return (tokenHandler.WriteToken(token), expiresAt);
        }

        private static string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password);
        }

        private static bool VerifyPassword(string password, string hash)
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }

        private static UserDto MapUserToDto(User user)
        {
            return new UserDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                CreatedAt = user.CreatedAt
            };
        }
    }
}
