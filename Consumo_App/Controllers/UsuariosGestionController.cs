// Controllers/UsuariosGestionController.cs
using Consumo_App.Data.Sql;
using Consumo_App.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/usuarios-gestion")]
    [Authorize]
    public class UsuariosGestionController : ControllerBase
    {
        private readonly SqlConnectionFactory _db;
        private readonly IUserContext _user;
        private readonly IPasswordHasher _hasher;

        public UsuariosGestionController(SqlConnectionFactory db, IUserContext user, IPasswordHasher hasher)
        {
            _db = db;
            _user = user;
            _hasher = hasher;
        }

        #region Cambio de Contraseña

        [HttpPost("cambiar-contrasena")]
        public async Task<IActionResult> CambiarContrasena([FromBody] CambiarContrasenaDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.ContrasenaActual))
                return BadRequest(new { message = "La contraseña actual es requerida." });

            if (string.IsNullOrWhiteSpace(dto.ContrasenaNueva) || dto.ContrasenaNueva.Length < 6)
                return BadRequest(new { message = "La nueva contraseña debe tener al menos 6 caracteres." });

            if (dto.ContrasenaNueva != dto.ConfirmarContrasena)
                return BadRequest(new { message = "Las contraseñas no coinciden." });

            using var conn = _db.Create();

            var contrasenaHash = await conn.QueryFirstOrDefaultAsync<string>(
                "SELECT Contrasena FROM Usuarios WHERE Id = @Id",
                new { Id = _user.Id });

            if (string.IsNullOrEmpty(contrasenaHash))
                return NotFound(new { message = "Usuario no encontrado." });

            if (!_hasher.Verify(dto.ContrasenaActual, contrasenaHash))
                return BadRequest(new { message = "La contraseña actual es incorrecta." });

            var nuevaHash = _hasher.Hash(dto.ContrasenaNueva);

            await conn.ExecuteAsync(@"
                UPDATE Usuarios 
                SET Contrasena = @Contrasena, UltimaModificacionUtc = @Fecha
                WHERE Id = @Id",
                new { Contrasena = nuevaHash, Fecha = DateTime.UtcNow, Id = _user.Id });

            return Ok(new { mensaje = "Contraseña actualizada exitosamente." });
        }

        #endregion

        #region Reseteo de Contraseña (Admin)

        [HttpPost("{usuarioId:int}/resetear-contrasena")]
        public async Task<IActionResult> ResetearContrasena(int usuarioId, [FromBody] ResetearContrasenaDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.NuevaContrasena) || dto.NuevaContrasena.Length < 6)
                return BadRequest(new { message = "La nueva contraseña debe tener al menos 6 caracteres." });

            using var conn = _db.Create();

            var nombreUsuario = await conn.QueryFirstOrDefaultAsync<string>(
                "SELECT Nombre FROM Usuarios WHERE Id = @Id",
                new { Id = usuarioId });

            if (nombreUsuario == null)
                return NotFound(new { message = "Usuario no encontrado." });

            var nuevaHash = _hasher.Hash(dto.NuevaContrasena);

            await conn.ExecuteAsync(@"
                UPDATE Usuarios 
                SET Contrasena = @Contrasena, 
                    UltimaModificacionUtc = @Fecha,
                    RequiereCambioContrasena = 1
                WHERE Id = @Id",
                new { Contrasena = nuevaHash, Fecha = DateTime.UtcNow, Id = usuarioId });

            return Ok(new
            {
                mensaje = $"Contraseña reseteada para el usuario '{nombreUsuario}'.",
                requiereCambio = true
            });
        }

        #endregion

        #region Editar Usuario

        [HttpPatch("{usuarioId:int}")]
        public async Task<IActionResult> EditarUsuario(int usuarioId, [FromBody] EditarUsuarioDto dto)
        {
            var esAdmin = User.HasClaim("perm", "admin_usuarios");
            if (!esAdmin && _user.Id != usuarioId)
                return Forbid();

            if (string.IsNullOrWhiteSpace(dto.Nombre))
                return BadRequest(new { message = "El nombre es requerido." });

            using var conn = _db.Create();

            var existe = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT 1 FROM Usuarios WHERE Id = @Id", new { Id = usuarioId });

            if (!existe.HasValue)
                return NotFound(new { message = "Usuario no encontrado." });

            var nombreDuplicado = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT 1 FROM Usuarios WHERE Nombre = @Nombre AND Id != @Id",
                new { Nombre = dto.Nombre.Trim(), Id = usuarioId });

            if (nombreDuplicado.HasValue)
                return BadRequest(new { message = $"Ya existe un usuario con el nombre '{dto.Nombre}'." });

            await conn.ExecuteAsync(@"
                UPDATE Usuarios 
                SET Nombre = @Nombre, UltimaModificacionUtc = @Fecha
                WHERE Id = @Id",
                new { Nombre = dto.Nombre.Trim(), Fecha = DateTime.UtcNow, Id = usuarioId });

            return Ok(new { mensaje = "Usuario actualizado exitosamente." });
        }

        #endregion

        #region Tracking de Login

        [HttpPost("registrar-login")]
        public async Task<IActionResult> RegistrarLogin()
        {
            using var conn = _db.Create();

            await conn.ExecuteAsync(@"
                UPDATE Usuarios 
                SET UltimoLoginUtc = @Fecha,
                    ContadorLogins = ISNULL(ContadorLogins, 0) + 1
                WHERE Id = @Id",
                new { Fecha = DateTime.UtcNow, Id = _user.Id });

            return Ok();
        }

        #endregion

        #region Estadísticas de Cajero

        [HttpGet("mis-estadisticas")]
        public async Task<IActionResult> MisEstadisticas([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta)
        {
            var fechaDesde = desde ?? DateTime.UtcNow.Date.AddDays(-30);
            var fechaHasta = hasta ?? DateTime.UtcNow.Date.AddDays(1);

            using var conn = _db.Create();

            var infoUsuario = await conn.QueryFirstOrDefaultAsync<UsuarioInfoDto>(@"
                SELECT u.Nombre, u.UltimoLoginUtc AS UltimoLogin, u.ContadorLogins AS TotalLogins, 
                       u.CreadoUtc AS FechaCreacion, r.Nombre AS Rol
                FROM Usuarios u
                LEFT JOIN Roles r ON u.RolId = r.Id
                WHERE u.Id = @Id",
                new { Id = _user.Id });

            var resumen = await conn.QueryFirstOrDefaultAsync<ResumenConsumosDto>(@"
                SELECT 
                    COUNT(*) AS TotalConsumos,
                    ISNULL(SUM(Monto), 0) AS MontoTotal,
                    SUM(CASE WHEN Reversado = 1 THEN 1 ELSE 0 END) AS ConsumosReversados
                FROM Consumos
                WHERE UsuarioRegistradorId = @UserId
                  AND Fecha >= @Desde AND Fecha < @Hasta",
                new { UserId = _user.Id, Desde = fechaDesde, Hasta = fechaHasta });

            var consumosPorDia = await conn.QueryAsync<ConsumoDiaDto>(@"
                SELECT 
                    CAST(Fecha AS DATE) AS Fecha,
                    COUNT(*) AS Cantidad,
                    ISNULL(SUM(CASE WHEN Reversado = 0 THEN Monto ELSE 0 END), 0) AS Monto
                FROM Consumos
                WHERE UsuarioRegistradorId = @UserId
                  AND Fecha >= @Desde
                GROUP BY CAST(Fecha AS DATE)
                ORDER BY CAST(Fecha AS DATE) DESC",
                new { UserId = _user.Id, Desde = DateTime.UtcNow.Date.AddDays(-7) });

            var hoy = await conn.QueryFirstOrDefaultAsync<(int Consumos, decimal Monto)>(@"
                SELECT COUNT(*), ISNULL(SUM(CASE WHEN Reversado = 0 THEN Monto ELSE 0 END), 0)
                FROM Consumos
                WHERE UsuarioRegistradorId = @UserId
                  AND CAST(Fecha AS DATE) = CAST(GETUTCDATE() AS DATE)",
                new { UserId = _user.Id });

            return Ok(new
            {
                Usuario = infoUsuario,
                Periodo = new { Desde = fechaDesde, Hasta = fechaHasta },
                Resumen = new
                {
                    resumen?.TotalConsumos,
                    resumen?.MontoTotal,
                    resumen?.ConsumosReversados,
                    PromedioPoConsumo = resumen?.TotalConsumos > 0 ? resumen.MontoTotal / resumen.TotalConsumos : 0
                },
                Hoy = new { hoy.Consumos, hoy.Monto },
                ConsumosPorDia = consumosPorDia
            });
        }

        [HttpGet("{usuarioId:int}/estadisticas")]
        [Authorize(Policy = "perm:admin_usuarios")]
        public async Task<IActionResult> EstadisticasUsuario(int usuarioId, [FromQuery] DateTime? desde, [FromQuery] DateTime? hasta)
        {
            var fechaDesde = desde ?? DateTime.UtcNow.Date.AddDays(-30);
            var fechaHasta = hasta ?? DateTime.UtcNow.Date.AddDays(1);

            using var conn = _db.Create();

            var infoUsuario = await conn.QueryFirstOrDefaultAsync<UsuarioInfoExtendidoDto>(@"
                SELECT u.Id, u.Nombre, u.Activo, u.UltimoLoginUtc AS UltimoLogin, 
                       u.ContadorLogins AS TotalLogins, u.CreadoUtc AS FechaCreacion, r.Nombre AS Rol
                FROM Usuarios u
                LEFT JOIN Roles r ON u.RolId = r.Id
                WHERE u.Id = @Id",
                new { Id = usuarioId });

            if (infoUsuario == null)
                return NotFound(new { message = "Usuario no encontrado." });

            var asignaciones = await conn.QueryAsync<AsignacionDto>(@"
                SELECT pa.Id, pa.Rol, pa.Activo,
                       p.Nombre AS Proveedor,
                       t.Nombre AS Tienda,
                       c.Nombre AS Caja
                FROM ProveedorAsignaciones pa
                INNER JOIN Proveedores p ON pa.ProveedorId = p.Id
                LEFT JOIN ProveedorTiendas t ON pa.TiendaId = t.Id
                LEFT JOIN ProveedorCajas c ON pa.CajaId = c.Id
                WHERE pa.UsuarioId = @UserId",
                new { UserId = usuarioId });

            var resumen = await conn.QueryFirstOrDefaultAsync<ResumenConsumosDto>(@"
                SELECT 
                    COUNT(*) AS TotalConsumos,
                    ISNULL(SUM(Monto), 0) AS MontoTotal,
                    SUM(CASE WHEN Reversado = 1 THEN 1 ELSE 0 END) AS ConsumosReversados
                FROM Consumos
                WHERE UsuarioRegistradorId = @UserId
                  AND Fecha >= @Desde AND Fecha < @Hasta",
                new { UserId = usuarioId, Desde = fechaDesde, Hasta = fechaHasta });

            var consumosPorDia = await conn.QueryAsync<ConsumoDiaDto>(@"
                SELECT 
                    CAST(Fecha AS DATE) AS Fecha,
                    COUNT(*) AS Cantidad,
                    ISNULL(SUM(CASE WHEN Reversado = 0 THEN Monto ELSE 0 END), 0) AS Monto
                FROM Consumos
                WHERE UsuarioRegistradorId = @UserId
                  AND Fecha >= @Desde AND Fecha < @Hasta
                GROUP BY CAST(Fecha AS DATE)
                ORDER BY CAST(Fecha AS DATE) DESC",
                new { UserId = usuarioId, Desde = fechaDesde, Hasta = fechaHasta });

            return Ok(new
            {
                Usuario = infoUsuario,
                Asignaciones = asignaciones,
                Periodo = new { Desde = fechaDesde, Hasta = fechaHasta },
                Resumen = new
                {
                    resumen?.TotalConsumos,
                    resumen?.MontoTotal,
                    resumen?.ConsumosReversados,
                    PromedioPoConsumo = resumen?.TotalConsumos > 0 ? resumen.MontoTotal / resumen.TotalConsumos : 0
                },
                ConsumosPorDia = consumosPorDia
            });
        }

        #endregion

        #region Listado de Usuarios (Admin)

        [HttpGet("lista")]
        public async Task<IActionResult> ListarUsuarios(
            [FromQuery] string? busqueda,
            [FromQuery] bool? soloActivos,
            [FromQuery] bool? soloCajeros,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            using var conn = _db.Create();

            var whereClause = "WHERE 1=1";
            if (!string.IsNullOrWhiteSpace(busqueda))
                whereClause += " AND u.Nombre LIKE @Busqueda";
            if (soloActivos == true)
                whereClause += " AND u.Activo = 1";
            if (soloCajeros == true)
                whereClause += " AND EXISTS (SELECT 1 FROM ProveedorAsignaciones pa WHERE pa.UsuarioId = u.Id)";

            var totalCount = await conn.QueryFirstAsync<int>(
                $"SELECT COUNT(*) FROM Usuarios u {whereClause}",
                new { Busqueda = $"%{busqueda}%" });

            var usuarios = await conn.QueryAsync<UsuarioListaDto>($@"
                SELECT 
                    u.Id,
                    u.Nombre,
                    u.Activo,
                    u.UltimoLoginUtc AS UltimoLogin,
                    u.ContadorLogins AS TotalLogins,
                    u.CreadoUtc AS FechaCreacion,
                    r.Nombre AS Rol,
                    (SELECT COUNT(*) FROM ProveedorAsignaciones pa WHERE pa.UsuarioId = u.Id AND pa.Activo = 1) AS Asignaciones,
                    (SELECT COUNT(*) FROM Consumos c WHERE c.UsuarioRegistradorId = u.Id) AS TotalConsumos
                FROM Usuarios u
                LEFT JOIN Roles r ON u.RolId = r.Id
                {whereClause}
                ORDER BY u.Nombre
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY",
                new
                {
                    Busqueda = $"%{busqueda}%",
                    Offset = (page - 1) * pageSize,
                    PageSize = pageSize
                });

            return Ok(new
            {
                Data = usuarios.Select(u => new
                {
                    u.Id,
                    u.Nombre,
                    u.Activo,
                    u.UltimoLogin,
                    u.TotalLogins,
                    u.FechaCreacion,
                    u.Rol,
                    u.Asignaciones,
                    u.TotalConsumos,
                    EsCajero = u.Asignaciones > 0
                }),
                Pagination = new
                {
                    Total = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
                }
            });
        }

        #endregion
    }

    #region DTOs

    public class CambiarContrasenaDto
    {
        public string ContrasenaActual { get; set; } = null!;
        public string ContrasenaNueva { get; set; } = null!;
        public string ConfirmarContrasena { get; set; } = null!;
    }

    public class ResetearContrasenaDto
    {
        public string NuevaContrasena { get; set; } = null!;
    }

    public class EditarUsuarioDto
    {
        public string Nombre { get; set; } = null!;
    }

    public class UsuarioInfoDto
    {
        public string Nombre { get; set; } = "";
        public DateTime? UltimoLogin { get; set; }
        public int TotalLogins { get; set; }
        public DateTime? FechaCreacion { get; set; }
        public string? Rol { get; set; }
    }

    public class UsuarioInfoExtendidoDto : UsuarioInfoDto
    {
        public int Id { get; set; }
        public bool Activo { get; set; }
    }

    public class ResumenConsumosDto
    {
        public int TotalConsumos { get; set; }
        public decimal MontoTotal { get; set; }
        public int ConsumosReversados { get; set; }
    }

    public class ConsumoDiaDto
    {
        public DateTime Fecha { get; set; }
        public int Cantidad { get; set; }
        public decimal Monto { get; set; }
    }

    public class AsignacionDto
    {
        public int Id { get; set; }
        public string? Rol { get; set; }
        public bool Activo { get; set; }
        public string Proveedor { get; set; } = "";
        public string? Tienda { get; set; }
        public string? Caja { get; set; }
    }

    public class UsuarioListaDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
        public bool Activo { get; set; }
        public DateTime? UltimoLogin { get; set; }
        public int TotalLogins { get; set; }
        public DateTime? FechaCreacion { get; set; }
        public string? Rol { get; set; }
        public int Asignaciones { get; set; }
        public int TotalConsumos { get; set; }
    }

    #endregion
}