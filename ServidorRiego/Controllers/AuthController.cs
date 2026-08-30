using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServidorRiego.Dtos;
using ServidorRiego.Security;
using ServidorRiego.Services;

namespace ServidorRiego.Controllers
{
    /// <summary>
    /// Controlador para autenticación de usuarios
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly ILogger<AuthController> _logger;

        /// <summary>
        /// Constructor del controlador de autenticación
        /// </summary>
        public AuthController(IAuthService authService, ILogger<AuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        /// <summary>
        /// Registrar un nuevo usuario
        /// </summary>
        /// <param name="request">Datos de registro (username, email, password)</param>
        /// <returns>Información del usuario creado o error</returns>
        /// <response code="201">Usuario creado exitosamente</response>
        /// <response code="400">Error en los datos o usuario ya existe</response>
        /// <response code="401">Falta o no coincide la cabecera X-App-Secret (no aplica en Development)</response>
        /// <response code="429">Demasiados intentos de registro desde esta IP (no aplica en Development)</response>
        /// <response code="500">Error interno del servidor</response>
        [HttpPost("register")]
        [AllowAnonymous]
        [RequireAppSecret]
        [EnableRateLimiting("register")]
        [ProducesResponseType(typeof(RegisterResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(RegisterResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        public async Task<ActionResult<RegisterResponse>> Register([FromBody] RegisterRequest request)
        {
            _logger.LogInformation($"Intento de registro para usuario: {request.Username}");
            var response = await _authService.RegisterAsync(request);

            if (!response.Success)
            {
                return BadRequest(response);
            }

            return CreatedAtAction(nameof(GetProfile), response);
        }

        /// <summary>
        /// Iniciar sesión y obtener token JWT
        /// </summary>
        /// <param name="request">Credenciales de usuario (username, password)</param>
        /// <returns>Token JWT y datos del usuario</returns>
        /// <response code="200">Login exitoso, retorna token y usuario</response>
        /// <response code="401">Credenciales inválidas</response>
        /// <response code="500">Error interno del servidor</response>
        [HttpPost("login")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
        {
            _logger.LogInformation($"Intento de login para usuario: {request.Username}");
            var response = await _authService.LoginAsync(request);

            if (!response.Success)
            {
                return Unauthorized(response);
            }

            return Ok(response);
        }

        /// <summary>
        /// Renovar el token de acceso usando un refresh token válido
        /// </summary>
        /// <param name="request">Refresh token obtenido en login o en una renovación previa</param>
        /// <returns>Nuevo token JWT y nuevo refresh token (rotado)</returns>
        /// <response code="200">Renovación exitosa, retorna nuevo token y refresh token</response>
        /// <response code="401">Refresh token inválido, expirado o revocado</response>
        /// <response code="500">Error interno del servidor</response>
        [HttpPost("refresh")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<LoginResponse>> Refresh([FromBody] RefreshTokenRequest request)
        {
            var response = await _authService.RefreshTokenAsync(request);

            if (!response.Success)
            {
                return Unauthorized(response);
            }

            return Ok(response);
        }

        /// <summary>
        /// Cerrar sesión revocando el refresh token indicado
        /// </summary>
        /// <param name="request">Refresh token a revocar</param>
        /// <returns>Resultado de la operación</returns>
        /// <response code="200">Refresh token revocado (o ya no válido)</response>
        [HttpPost("logout")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<ApiResponse>> Logout([FromBody] RefreshTokenRequest request)
        {
            var revoked = await _authService.RevokeTokenAsync(request.RefreshToken);

            return Ok(new ApiResponse
            {
                Success = revoked,
                Message = revoked ? "Sesión cerrada correctamente" : "El refresh token no existe o ya estaba revocado"
            });
        }

        /// <summary>
        /// Obtener el perfil del usuario autenticado
        /// </summary>
        /// <returns>Información del usuario actual</returns>
        /// <response code="200">Perfil del usuario</response>
        /// <response code="401">No autenticado o token inválido</response>
        /// <response code="404">Usuario no encontrado</response>
        /// <response code="500">Error interno del servidor</response>
        [HttpGet("profile")]
        [Authorize]
        [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(void), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<UserDto>> GetProfile()
        {
            var userIdClaim = User.FindFirst("userId");
            if (!int.TryParse(userIdClaim?.Value, out var userId))
            {
                return Unauthorized();
            }

            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null)
            {
                return NotFound();
            }

            return Ok(user);
        }
    }
}
