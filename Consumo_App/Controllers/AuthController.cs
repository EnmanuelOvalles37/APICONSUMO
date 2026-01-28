using Consumo_App.DTOs;
using Consumo_App.Models;
using Consumo_App.Servicios;
using Consumo_App.Servicios.Sql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Security.Claims;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous]
    public class AuthController : ControllerBase
    {
        private readonly IJwtService _jwtService;
        private readonly ISeguridadService _seguridadService;
        private readonly IUserContext _user;
        private readonly AuthSqlService _authSqlService;
        private readonly ProveedorAsignacionSqlService _asignacionSql;
        private readonly string _connectionString;

        // Configuración de seguridad
        private const int MAX_INTENTOS_FALLIDOS = 5;
        private const int MINUTOS_BLOQUEO = 15;
        private const string CONTRASENA_INICIAL = "123456";

        public AuthController(
            ISeguridadService seguridadService,
            IJwtService jwtService,
            IUserContext user,
            AuthSqlService authSqlService,
            ProveedorAsignacionSqlService asignacionSql,
            IConfiguration configuration)
        {
            _jwtService = jwtService;
            _seguridadService = seguridadService;
            _user = user;
            _authSqlService = authSqlService;
            _asignacionSql = asignacionSql;
            _connectionString = configuration.GetConnectionString("DefaultConnection")!;
        }

        // ============================
        // LOGIN UNIFICADO
        // ============================
        [HttpPost("login")]
        public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginDTO request)
        {
            // 1. Primero intentar como Usuario del sistema
            var usuario = await _authSqlService.AuthenticateAsync(
                request.Usuario,
                request.Contrasena
            );

            if (usuario != null)
            {
                return await LoginUsuarioSistema(usuario);
            }

            // 2. Si no es usuario del sistema, intentar como Cliente (por cédula)
            return await LoginCliente(request.Usuario, request.Contrasena);
        }

        // ============================
        // LOGIN USUARIO DEL SISTEMA
        // ============================
        private async Task<ActionResult<LoginResponse>> LoginUsuarioSistema(Usuario usuario)
        {
            var permisos = await _seguridadService.GetPermisosCodigoPorUsuarioAsync(usuario.Id);
            var token = _jwtService.GenerateToken(usuario, permisos);
            var asignacion = await _asignacionSql.GetAsignacionActivaPorUsuarioAsync(usuario.Id);

            var rolNombre = usuario.Rol?.Nombre ?? string.Empty;
            var rolId = usuario.RolId;
            var esCajero = asignacion != null || rolId == 6;

            return Ok(new LoginResponse
            {
                Token = token,
                Id = usuario.Id,
                RolId = rolId,
                Usuario = usuario.Nombre,
                Rol = rolNombre,
                Expiracion = DateTime.UtcNow.AddHours(8),
                Permisos = permisos.ToList(),
                EsCajero = esCajero,
                TipoUsuario = DeterminarTipoUsuario(rolNombre, rolId, asignacion),
                RequiereCambioContrasena = false,
                Asignacion = asignacion != null
                    ? new AsignacionInfo
                    {
                        ProveedorId = asignacion.ProveedorId,
                        ProveedorNombre = asignacion.Proveedor?.Nombre,
                        TiendaId = asignacion.TiendaId,
                        TiendaNombre = asignacion.Tienda?.Nombre,
                        CajaId = asignacion.CajaId,
                        CajaNombre = asignacion.Caja?.Nombre,
                        Rol = asignacion.Rol
                    }
                    : null
            });
        }

        // ============================
        // LOGIN CLIENTE (EMPLEADO)
        // ============================
        private async Task<ActionResult<LoginResponse>> LoginCliente(string cedula, string contrasena)
        {
            using var conn = new SqlConnection(_connectionString);

            // Buscar cliente por cédula
            var cliente = await conn.QueryFirstOrDefaultAsync<ClienteAuthDto>(@"
                SELECT 
                    c.Id,
                    c.Codigo,
                    c.Nombre,
                    c.Cedula,
                    c.Contrasena,
                    c.RequiereCambioContrasena,
                    c.ContadorLoginsFallidos,
                    c.BloqueadoHasta,
                    c.AccesoHabilitado,
                    c.Activo,
                    c.EmpresaId,
                    e.Nombre AS EmpresaNombre
                FROM Clientes c
                INNER JOIN Empresas e ON c.EmpresaId = e.Id
                WHERE c.Cedula = @Cedula",
                new { Cedula = cedula });

            if (cliente == null)
                return Unauthorized(new { message = "Usuario o contraseña incorrectos" });

            if (!cliente.Activo)
                return Unauthorized(new { message = "Esta cuenta está desactivada" });

            if (!cliente.AccesoHabilitado)
                return Unauthorized(new { message = "El acceso ha sido deshabilitado. Contacte al administrador." });

            // Verificar bloqueo temporal
            if (cliente.BloqueadoHasta.HasValue && cliente.BloqueadoHasta > DateTime.UtcNow)
            {
                var minutos = (int)(cliente.BloqueadoHasta.Value - DateTime.UtcNow).TotalMinutes + 1;
                return Unauthorized(new
                {
                    message = $"Cuenta bloqueada. Intente en {minutos} minutos.",
                    bloqueado = true,
                    minutosRestantes = minutos
                });
            }

            // Verificar contraseña
            bool contrasenaValida;
            bool esPrimerLogin = string.IsNullOrEmpty(cliente.Contrasena);

            if (esPrimerLogin)
            {
                // Primer login: verificar contraseña inicial
                contrasenaValida = contrasena == CONTRASENA_INICIAL;
            }
            else
            {
                // Verificar hash BCrypt
                contrasenaValida = BCrypt.Net.BCrypt.Verify(contrasena, cliente.Contrasena);
            }

            if (!contrasenaValida)
            {
                await IncrementarIntentosFallidos(conn, cliente.Id, cliente.ContadorLoginsFallidos);
                var restantes = MAX_INTENTOS_FALLIDOS - (cliente.ContadorLoginsFallidos + 1);

                if (restantes <= 0)
                    return Unauthorized(new { message = $"Cuenta bloqueada por {MINUTOS_BLOQUEO} minutos." });

                return Unauthorized(new { message = $"Contraseña incorrecta. {restantes} intentos restantes." });
            }

            // Login exitoso
            await RegistrarLoginExitoso(conn, cliente.Id);

            // Generar token
            var token = GenerarTokenCliente(cliente);

            return Ok(new LoginResponse
            {
                Token = token,
                Id = cliente.Id,
                RolId = 0,
                Usuario = cliente.Nombre,
                Rol = "empleado",
                Expiracion = DateTime.UtcNow.AddHours(8),
                Permisos = new List<string>(),
                EsCajero = false,
                TipoUsuario = "empleado",
                RequiereCambioContrasena = esPrimerLogin || cliente.RequiereCambioContrasena,
                ClienteInfo = new ClienteLoginInfo
                {
                    ClienteId = cliente.Id,
                    Codigo = cliente.Codigo,
                    EmpresaId = cliente.EmpresaId,
                    EmpresaNombre = cliente.EmpresaNombre
                }
            });
        }

        // ============================
        // CAMBIAR CONTRASEÑA
        // ============================
        [HttpPost("cambiar-contrasena")]
        [Authorize]
        public async Task<IActionResult> CambiarContrasena([FromBody] CambiarContrasenaRequest request)
        {
            var tipoUsuario = User.FindFirst("TipoUsuario")?.Value;
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(request.NuevaContrasena) || request.NuevaContrasena.Length < 6)
                return BadRequest(new { message = "La contraseña debe tener al menos 6 caracteres" });

            if (request.NuevaContrasena != request.ConfirmarContrasena)
                return BadRequest(new { message = "Las contraseñas no coinciden" });

            if (request.NuevaContrasena == CONTRASENA_INICIAL)
                return BadRequest(new { message = "Debe elegir una contraseña diferente a la inicial" });

            using var conn = new SqlConnection(_connectionString);

            if (tipoUsuario == "empleado")
            {
                var cliente = await conn.QueryFirstOrDefaultAsync<ClienteAuthDto>(@"
                    SELECT Id, Contrasena FROM Clientes WHERE Id = @Id",
                    new { Id = userId });

                if (cliente == null)
                    return NotFound();

                // Si ya tiene contraseña, verificar la actual
                if (!string.IsNullOrEmpty(cliente.Contrasena) && !string.IsNullOrEmpty(request.ContrasenaActual))
                {
                    if (!BCrypt.Net.BCrypt.Verify(request.ContrasenaActual, cliente.Contrasena))
                        return BadRequest(new { message = "La contraseña actual es incorrecta" });
                }

                var hash = BCrypt.Net.BCrypt.HashPassword(request.NuevaContrasena);

                await conn.ExecuteAsync(@"
                    UPDATE Clientes 
                    SET Contrasena = @Hash, 
                        RequiereCambioContrasena = 0,
                        ContadorLoginsFallidos = 0
                    WHERE Id = @Id",
                    new { Hash = hash, Id = userId });

                return Ok(new { message = "Contraseña actualizada exitosamente" });
            }

            return BadRequest(new { message = "Use el endpoint correspondiente" });
        }

        // ============================
        // VALIDAR TOKEN
        // ============================
        [HttpGet("validate")]
        public async Task<IActionResult> ValidateToken([FromHeader] string authorization)
        {
            if (string.IsNullOrWhiteSpace(authorization) || !authorization.StartsWith("Bearer "))
                return Unauthorized();

            var token = authorization.Replace("Bearer ", "");
            var valido = await _jwtService.ValidateTokenAsync(token);
            return valido ? Ok() : Unauthorized();
        }

        // ============================
        // INFO DEL USUARIO
        // ============================
        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> GetMe()
        {
            var tipoUsuario = User.FindFirst("TipoUsuario")?.Value;
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            if (tipoUsuario == "empleado")
            {
                using var conn = new SqlConnection(_connectionString);
                var cliente = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT c.Id, c.Codigo, c.Nombre, c.Cedula, e.Nombre AS EmpresaNombre,
                           c.RequiereCambioContrasena
                    FROM Clientes c
                    INNER JOIN Empresas e ON c.EmpresaId = e.Id
                    WHERE c.Id = @Id",
                    new { Id = userId });

                return Ok(new
                {
                    Id = cliente?.Id,
                    Usuario = cliente?.Nombre,
                    TipoUsuario = "empleado",
                    Codigo = cliente?.Codigo,
                    EmpresaNombre = cliente?.EmpresaNombre,
                    RequiereCambioContrasena = cliente?.RequiereCambioContrasena ?? false
                });
            }

            return Ok(new
            {
                Id = _user.Id,
                Usuario = _user.Nombre,
                RolId = _user.RolId,
                RolNombre = _user.Rol,
                TipoUsuario = tipoUsuario,
                Permisos = _user.Permisos
            });
        }

        // ============================
        // LOGOUT
        // ============================
        [HttpPost("logout")]
        [Authorize]
        public IActionResult Logout() => Ok(new { message = "Sesión cerrada" });

        // ============================
        // HELPERS
        // ============================
        private async Task IncrementarIntentosFallidos(SqlConnection conn, int clienteId, int intentosActuales)
        {
            var nuevosIntentos = intentosActuales + 1;
            DateTime? bloqueadoHasta = null;

            if (nuevosIntentos >= MAX_INTENTOS_FALLIDOS)
            {
                bloqueadoHasta = DateTime.UtcNow.AddMinutes(MINUTOS_BLOQUEO);
                nuevosIntentos = 0;
            }

            await conn.ExecuteAsync(@"
                UPDATE Clientes 
                SET ContadorLoginsFallidos = @Intentos, BloqueadoHasta = @Bloqueado
                WHERE Id = @Id",
                new { Intentos = nuevosIntentos, Bloqueado = bloqueadoHasta, Id = clienteId });
        }

        private async Task RegistrarLoginExitoso(SqlConnection conn, int clienteId)
        {
            await conn.ExecuteAsync(@"
                UPDATE Clientes 
                SET ContadorLoginsFallidos = 0, BloqueadoHasta = NULL, UltimoLoginUtc = @Ahora
                WHERE Id = @Id",
                new { Ahora = DateTime.UtcNow, Id = clienteId });
        }

        private string GenerarTokenCliente(ClienteAuthDto cliente)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, cliente.Id.ToString()),
                new(ClaimTypes.Name, cliente.Nombre),
                new("TipoUsuario", "empleado"),
                new("ClienteId", cliente.Id.ToString()),
                new("EmpresaId", cliente.EmpresaId.ToString()),
                new("Codigo", cliente.Codigo ?? "")
            };

            return _jwtService.GenerateTokenFromClaims(claims);
        }

        private static string DeterminarTipoUsuario(string rol, int rolId, ProveedorAsignacion? asignacion)
        {
            return rolId switch
            {
                4 => "empleador",
                5 => "backoffice",
                6 => "cajero",
                _ => asignacion != null
                    ? asignacion.Rol?.ToLower() switch
                    {
                        "admin" => "proveedor_admin",
                        "supervisor" => "proveedor_supervisor",
                        _ => "cajero"
                    }
                    : rol.ToLower() switch
                    {
                        "admin" or "administrador" => "admin",
                        "contabilidad" => "contabilidad",
                        _ => "usuario"
                    }
            };
        }
    }

    // ============================
    // DTOs
    // ============================
    public class LoginResponse
    {
        public string Token { get; set; } = "";
        public int Id { get; set; }
        public int RolId { get; set; }
        public string Usuario { get; set; } = "";
        public string Rol { get; set; } = "";
        public DateTime Expiracion { get; set; }
        public List<string> Permisos { get; set; } = new();
        public bool EsCajero { get; set; }
        public string TipoUsuario { get; set; } = "";
        public bool RequiereCambioContrasena { get; set; }
        public AsignacionInfo? Asignacion { get; set; }
        public ClienteLoginInfo? ClienteInfo { get; set; }
    }

    public class AsignacionInfo
    {
        public int ProveedorId { get; set; }
        public string? ProveedorNombre { get; set; }
        public int? TiendaId { get; set; }
        public string? TiendaNombre { get; set; }
        public int? CajaId { get; set; }
        public string? CajaNombre { get; set; }
        public string? Rol { get; set; }
    }

    public class ClienteLoginInfo
    {
        public int ClienteId { get; set; }
        public string Codigo { get; set; } = "";
        public int EmpresaId { get; set; }
        public string EmpresaNombre { get; set; } = "";
    }

    public class ClienteAuthDto
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string Cedula { get; set; } = "";
        public string? Contrasena { get; set; }
        public bool RequiereCambioContrasena { get; set; }
        public int ContadorLoginsFallidos { get; set; }
        public DateTime? BloqueadoHasta { get; set; }
        public bool AccesoHabilitado { get; set; }
        public bool Activo { get; set; }
        public int EmpresaId { get; set; }
        public string EmpresaNombre { get; set; } = "";
    }

    public class CambiarContrasenaRequest
    {
        public string? ContrasenaActual { get; set; }
        public string NuevaContrasena { get; set; } = "";
        public string ConfirmarContrasena { get; set; } = "";
    }
}
