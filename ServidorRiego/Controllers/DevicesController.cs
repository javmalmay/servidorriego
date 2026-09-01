using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServidorRiego.Dtos;
using ServidorRiego.Services;

namespace ServidorRiego.Controllers
{
    /// <summary>
    /// Controlador CRUD para la gestión de dispositivos de riego.
    /// Todos los endpoints operan sobre los dispositivos asociados al usuario autenticado.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    [Authorize]
    public class DevicesController : ControllerBase
    {
        private readonly IDeviceService _deviceService;
        private readonly ILogger<DevicesController> _logger;

        /// <summary>
        /// Constructor del controlador de dispositivos
        /// </summary>
        public DevicesController(IDeviceService deviceService, ILogger<DevicesController> logger)
        {
            _deviceService = deviceService;
            _logger = logger;
        }

        /// <summary>
        /// Obtener el id del usuario autenticado a partir del token JWT, o null si no está presente/es inválido
        /// </summary>
        private int? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst("userId");
            return int.TryParse(userIdClaim?.Value, out var userId) ? userId : null;
        }

        /// <summary>
        /// Obtener el listado de dispositivos asociados al usuario autenticado
        /// </summary>
        /// <returns>Listado de dispositivos</returns>
        /// <response code="200">Listado de dispositivos</response>
        [HttpGet]
        [ProducesResponseType(typeof(DeviceListResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<DeviceListResponse>> GetAll()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var response = await _deviceService.GetAllForUserAsync(userId.Value);
            return Ok(response);
        }

        /// <summary>
        /// Obtener un dispositivo por su identificador (debe estar asociado al usuario autenticado)
        /// </summary>
        /// <param name="id">Identificador del dispositivo</param>
        /// <returns>Datos del dispositivo</returns>
        /// <response code="200">Dispositivo encontrado</response>
        /// <response code="404">Dispositivo no encontrado o no asociado al usuario</response>
        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(DeviceResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(DeviceResponse), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<DeviceResponse>> GetById(int id)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var response = await _deviceService.GetByIdAsync(id, userId.Value);
            if (!response.Success)
            {
                return NotFound(response);
            }

            return Ok(response);
        }

        /// <summary>
        /// Obtener un dispositivo por su dirección MAC (debe estar asociado al usuario autenticado)
        /// </summary>
        /// <param name="mac">Dirección MAC del dispositivo, 12 caracteres hexadecimales sin separadores (ej. AABBCCDDEEFF)</param>
        /// <returns>Datos del dispositivo</returns>
        /// <response code="200">Dispositivo encontrado</response>
        /// <response code="404">Dispositivo no encontrado o no asociado al usuario</response>
        [HttpGet("by-mac/{mac}")]
        [ProducesResponseType(typeof(DeviceResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(DeviceResponse), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<DeviceResponse>> GetByMac(string mac)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var response = await _deviceService.GetByMacAsync(mac, userId.Value);
            if (!response.Success)
            {
                return NotFound(response);
            }

            return Ok(response);
        }

        /// <summary>
        /// Registrar un dispositivo, o asociarse a uno ya existente ("upsert" por MAC).
        /// Si la MAC no está registrada todavía, se crea el dispositivo (con Name/ConfigJson) y el
        /// usuario autenticado queda asociado automáticamente como primer propietario, devolviendo
        /// el token secreto (solo se muestra esta vez). Si la MAC ya existe, no se crea nada nuevo
        /// ni se modifica el dispositivo: simplemente se asocia el usuario autenticado a él (Name/
        /// ConfigJson del request se ignoran, y DeviceToken vuelve null porque ya se entregó antes).
        /// </summary>
        /// <param name="request">Datos del dispositivo (MAC obligatoria; nombre y configuración JSON solo si es nuevo)</param>
        /// <returns>Dispositivo creado o asociado</returns>
        /// <response code="201">Dispositivo creado, o ya existente y ahora asociado a tu cuenta</response>
        /// <response code="400">Datos inválidos (p. ej. falta el nombre al crear uno nuevo)</response>
        [HttpPost]
        [ProducesResponseType(typeof(DeviceResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(DeviceResponse), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<DeviceResponse>> Create([FromBody] CreateDeviceRequest request)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var response = await _deviceService.CreateAsync(request, userId.Value);
            if (!response.Success)
            {
                return BadRequest(response);
            }

            return CreatedAtAction(nameof(GetById), new { id = response.Device!.Id }, response);
        }

        /// <summary>
        /// Actualizar el nombre y/o la configuración de un dispositivo existente
        /// </summary>
        /// <param name="id">Identificador del dispositivo</param>
        /// <param name="request">Nuevo nombre y/o configuración JSON (la MAC no es modificable)</param>
        /// <returns>Dispositivo actualizado</returns>
        /// <response code="200">Dispositivo actualizado correctamente</response>
        /// <response code="400">Datos inválidos</response>
        /// <response code="404">Dispositivo no encontrado o no asociado al usuario</response>
        [HttpPut("{id:int}")]
        [ProducesResponseType(typeof(DeviceResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(DeviceResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(DeviceResponse), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<DeviceResponse>> Update(int id, [FromBody] UpdateDeviceRequest request)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var response = await _deviceService.UpdateAsync(id, request, userId.Value);
            if (!response.Success)
            {
                return response.Message == "Dispositivo no encontrado"
                    ? NotFound(response)
                    : BadRequest(response);
            }

            return Ok(response);
        }

        /// <summary>
        /// Desasociar el dispositivo de la cuenta del usuario autenticado. Si tras esto no queda
        /// ningún otro usuario asociado, el dispositivo se elimina de la base de datos por completo.
        /// </summary>
        /// <param name="id">Identificador del dispositivo</param>
        /// <returns>Resultado de la operación</returns>
        /// <response code="200">Dispositivo desasociado (y, si era el último usuario, eliminado)</response>
        /// <response code="404">Dispositivo no encontrado o no asociado al usuario</response>
        [HttpDelete("{id:int}")]
        [ProducesResponseType(typeof(DeviceResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(DeviceResponse), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<DeviceResponse>> Delete(int id)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var response = await _deviceService.DeleteAsync(id, userId.Value);
            if (!response.Success)
            {
                return NotFound(response);
            }

            return Ok(response);
        }

        /// <summary>
        /// Regenerar el token secreto del dispositivo (invalida el anterior). El dispositivo deberá
        /// reconectarse al WebSocket /ws/device con el nuevo token.
        /// </summary>
        /// <param name="id">Identificador del dispositivo</param>
        /// <returns>Nuevo token del dispositivo (solo se muestra una vez)</returns>
        /// <response code="200">Token regenerado correctamente</response>
        /// <response code="404">Dispositivo no encontrado o no asociado al usuario</response>
        [HttpPost("{id:int}/token/regenerate")]
        [ProducesResponseType(typeof(DeviceResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(DeviceResponse), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<DeviceResponse>> RegenerateToken(int id)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var response = await _deviceService.RegenerateTokenAsync(id, userId.Value);
            if (!response.Success)
            {
                return NotFound(response);
            }

            return Ok(response);
        }

        /// <summary>
        /// Listar los usuarios asociados (con acceso) a un dispositivo
        /// </summary>
        /// <param name="id">Identificador del dispositivo</param>
        /// <returns>Listado de usuarios asociados</returns>
        /// <response code="200">Listado de usuarios</response>
        /// <response code="404">Dispositivo no encontrado o no asociado al usuario</response>
        [HttpGet("{id:int}/users")]
        [ProducesResponseType(typeof(DeviceUsersResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(DeviceUsersResponse), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<DeviceUsersResponse>> GetUsers(int id)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var response = await _deviceService.GetAssociatedUsersAsync(id, userId.Value);
            if (!response.Success)
            {
                return NotFound(response);
            }

            return Ok(response);
        }

        /// <summary>
        /// Asociar (compartir acceso con) otro usuario a un dispositivo. Solo un usuario ya
        /// asociado al dispositivo puede invitar a otros.
        /// </summary>
        /// <param name="id">Identificador del dispositivo</param>
        /// <param name="request">Nombre de usuario a asociar</param>
        /// <returns>Resultado de la operación</returns>
        /// <response code="200">Usuario asociado correctamente</response>
        /// <response code="400">El usuario no existe o ya está asociado</response>
        /// <response code="404">Dispositivo no encontrado o no asociado al usuario que hace la petición</response>
        [HttpPost("{id:int}/users")]
        [ProducesResponseType(typeof(DeviceResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(DeviceResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(DeviceResponse), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<DeviceResponse>> AddUser(int id, [FromBody] AssociateUserRequest request)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var response = await _deviceService.AssociateUserAsync(id, request.Username, userId.Value);
            if (!response.Success)
            {
                return response.Message == "Dispositivo no encontrado"
                    ? NotFound(response)
                    : BadRequest(response);
            }

            return Ok(response);
        }

        /// <summary>
        /// Quitar la asociación de un usuario con un dispositivo (no se puede quitar al último usuario asociado)
        /// </summary>
        /// <param name="id">Identificador del dispositivo</param>
        /// <param name="username">Nombre del usuario a desasociar</param>
        /// <returns>Resultado de la operación</returns>
        /// <response code="200">Usuario desasociado correctamente</response>
        /// <response code="400">El usuario no está asociado, o es el último asociado</response>
        /// <response code="404">Dispositivo no encontrado o no asociado al usuario que hace la petición</response>
        [HttpDelete("{id:int}/users/{username}")]
        [ProducesResponseType(typeof(DeviceResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(DeviceResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(DeviceResponse), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<DeviceResponse>> RemoveUser(int id, string username)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var response = await _deviceService.RemoveUserAsync(id, username, userId.Value);
            if (!response.Success)
            {
                return response.Message == "Dispositivo no encontrado"
                    ? NotFound(response)
                    : BadRequest(response);
            }

            return Ok(response);
        }
    }
}
