// Controllers/UsuariosController.cs
using System.Security.Claims;
using Consumo_App.Data.Sql;
using Consumo_App.DTOs;
using Consumo_App.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using static Consumo_App.DTOs.UsuarioDtos;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UsuariosController : ControllerBase
    {
        private readonly SqlConnectionFactory _db;
        private readonly IPasswordHasher _hasher;

        public UsuariosController(SqlConnectionFactory db, IPasswordHasher hasher)
        {
            _db = db;
            _hasher = hasher;
        }

        // GET /api/usuarios
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] UsuarioQueryDto q)
        {
            using var conn = _db.Create();

            var whereClause = "WHERE 1=1";
            if (!string.IsNullOrWhiteSpace(q.q))
                whereClause += " AND u.Nombre LIKE @Busqueda";
            if (q.Activo.HasValue)
                whereClause += " AND u.Activo = @Activo";
            if (q.RolId.HasValue)
                whereClause += " AND u.RolId = @RolId";

            var total = await conn.QueryFirstAsync<int>(
                $"SELECT COUNT(*) FROM Usuarios u {whereClause}",
                new { Busqueda = $"%{q.q}%", q.Activo, q.RolId });

            var page = Math.Max(1, q.Page);
            var size = Math.Clamp(q.PageSize, 1, 100);

            var data = await conn.QueryAsync<UsuarioListDto>($@"
                SELECT u.Id, u.Nombre, r.Nombre AS RolNombre, u.Activo, u.CreadoUtc
                FROM Usuarios u
                LEFT JOIN Roles r ON u.RolId = r.Id
                {whereClause}
                ORDER BY u.Nombre
                OFFSET @Offset ROWS FETCH NEXT @Size ROWS ONLY",
                new
                {
                    Busqueda = $"%{q.q}%",
                    q.Activo,
                    q.RolId,
                    Offset = (page - 1) * size,
                    Size = size
                });

            return Ok(new PagedResult<UsuarioListDto>
            {
                Data = data.ToList(),
                Total = total,
                Page = page,
                PageSize = size
            });
        }

        // GET /api/usuarios/roles
        [HttpGet("roles")]
        public async Task<IActionResult> GetRoles()
        {
            using var conn = _db.Create();
            var roles = await conn.QueryAsync<object>(@"
                SELECT Id, Nombre, Descripcion
                FROM Roles
                ORDER BY Nombre");
            return Ok(roles);
        }

        // GET /api/usuarios/{id}
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            using var conn = _db.Create();
            var u = await conn.QueryFirstOrDefaultAsync<object>(@"
                SELECT u.Id, u.Nombre, u.RolId, r.Nombre AS Rol, u.Activo, u.CreadoUtc
                FROM Usuarios u
                LEFT JOIN Roles r ON u.RolId = r.Id
                WHERE u.Id = @Id",
                new { Id = id });

            if (u == null) return NotFound();
            return Ok(u);
        }

        // POST /api/usuarios
        [HttpPost]
        [Authorize(Policy = "perm:admin_usuarios")]
        public async Task<IActionResult> Create([FromBody] UsuarioCreateDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Nombre))
                return BadRequest("Nombre requerido.");
            if (string.IsNullOrWhiteSpace(dto.Contrasena))
                return BadRequest("Contraseña requerida.");

            using var conn = _db.Create();

            var dup = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT 1 FROM Usuarios WHERE Nombre = @Nombre",
                new { Nombre = dto.Nombre.Trim() });

            if (dup.HasValue)
                return BadRequest("El nombre de usuario ya existe.");

            var rolExists = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT 1 FROM Roles WHERE Id = @RolId",
                new { dto.RolId });

            if (!rolExists.HasValue)
                return BadRequest("Rol inválido.");

            var id = await conn.QuerySingleAsync<int>(@"
                INSERT INTO Usuarios (Nombre, Contrasena, RolId, Activo, CreadoUtc)
                OUTPUT INSERTED.Id
                VALUES (@Nombre, @Contrasena, @RolId, @Activo, GETUTCDATE())",
                new
                {
                    Nombre = dto.Nombre.Trim(),
                    Contrasena = _hasher.Hash(dto.Contrasena),
                    dto.RolId,
                    dto.Activo
                });

            return Created($"/api/usuarios/{id}", id);
        }

        // PUT /api/usuarios/{id}
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UsuarioUpdateDto dto)
        {
            using var conn = _db.Create();

            var exists = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT 1 FROM Usuarios WHERE Id = @Id", new { Id = id });

            if (!exists.HasValue) return NotFound();

            if (!string.IsNullOrWhiteSpace(dto.Nombre))
            {
                var dup = await conn.QueryFirstOrDefaultAsync<int?>(
                    "SELECT 1 FROM Usuarios WHERE Id != @Id AND Nombre = @Nombre",
                    new { Id = id, Nombre = dto.Nombre.Trim() });

                if (dup.HasValue)
                    return BadRequest("El nombre de usuario ya existe.");
            }

            if (dto.RolId.HasValue)
            {
                var rolExists = await conn.QueryFirstOrDefaultAsync<int?>(
                    "SELECT 1 FROM Roles WHERE Id = @RolId",
                    new { dto.RolId });

                if (!rolExists.HasValue)
                    return BadRequest("Rol inválido.");
            }

            await conn.ExecuteAsync(@"
                UPDATE Usuarios SET
                    Nombre = COALESCE(@Nombre, Nombre),
                    RolId = COALESCE(@RolId, RolId),
                    Activo = COALESCE(@Activo, Activo)
                WHERE Id = @Id",
                new
                {
                    Id = id,
                    Nombre = dto.Nombre?.Trim(),
                    dto.RolId,
                    dto.Activo
                });

            return NoContent();
        }

        // PATCH /api/usuarios/{id}/password
        [HttpPatch("{id:int}/password")]
        [Authorize(Policy = "perm:usuarios_password")]
        public async Task<IActionResult> ResetPassword(int id, [FromBody] UsuarioResetPassDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.NuevaContrasena))
                return BadRequest("Contraseña requerida.");

            using var conn = _db.Create();

            var rows = await conn.ExecuteAsync(
                "UPDATE Usuarios SET Contrasena = @Contrasena WHERE Id = @Id",
                new { Contrasena = _hasher.Hash(dto.NuevaContrasena), Id = id });

            if (rows == 0) return NotFound();
            return NoContent();
        }

        // PATCH /api/usuarios/{id}/toggle
        [HttpPatch("{id:int}/toggle")]
        public async Task<IActionResult> ToggleActivo(int id)
        {
            using var conn = _db.Create();

            var activo = await conn.QueryFirstOrDefaultAsync<bool?>(
                "SELECT Activo FROM Usuarios WHERE Id = @Id", new { Id = id });

            if (activo == null) return NotFound();

            var nuevoActivo = !activo.Value;
            await conn.ExecuteAsync(
                "UPDATE Usuarios SET Activo = @Activo WHERE Id = @Id",
                new { Activo = nuevoActivo, Id = id });

            return Ok(new { Id = id, Activo = nuevoActivo });
        }

        // DELETE /api/usuarios/{id}
        [HttpDelete("{id:int}")]
        [Authorize(Policy = "perm:usuarios_borrar")]
        [Authorize(Roles = "administrador")]
        public async Task<IActionResult> Delete(int id)
        {
            var callerIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(callerIdClaim, out var callerId))
                return Forbid();

            if (callerId == id)
                return BadRequest(new { message = "No puede eliminar su propia cuenta." });

            using var conn = _db.Create();

            var rows = await conn.ExecuteAsync(
                "UPDATE Usuarios SET Activo = 0 WHERE Id = @Id",
                new { Id = id });

            if (rows == 0)
                return NotFound(new { message = "Usuario no encontrado." });

            return NoContent();
        }

        // POST /api/usuarios/asociar-empresa
        [HttpPost("asociar-empresa")]
        public async Task<IActionResult> AsociarEmpresa([FromBody] AsociarEmpresaDto dto)
        {
            using var conn = _db.Create();

            var usuarioExists = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT 1 FROM Usuarios WHERE Id = @Id", new { Id = dto.UsuarioId });
            if (!usuarioExists.HasValue)
                return NotFound(new { message = "Usuario no encontrado" });

            var empresaExists = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT 1 FROM Empresas WHERE Id = @Id", new { Id = dto.EmpresaId });
            if (!empresaExists.HasValue)
                return NotFound(new { message = "Empresa no encontrada" });

            var existe = await conn.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM UsuarioEmpresa WHERE UsuarioId = @UsuarioId AND EmpresaId = @EmpresaId",
                new { dto.UsuarioId, dto.EmpresaId });

            if (existe > 0)
                return BadRequest(new { message = "El usuario ya está asociado a esta empresa" });

            await conn.ExecuteAsync(
                "INSERT INTO UsuarioEmpresa (UsuarioId, EmpresaId, CreadoUtc) VALUES (@UsuarioId, @EmpresaId, GETUTCDATE())",
                new { dto.UsuarioId, dto.EmpresaId });

            return Ok(new { message = "Usuario asociado a la empresa exitosamente" });
        }

        // POST /api/usuarios/asociar-proveedor
        [HttpPost("asociar-proveedor")]
        public async Task<IActionResult> AsociarProveedor([FromBody] AsociarProveedorDto dto)
        {
            using var conn = _db.Create();

            var usuarioExists = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT 1 FROM Usuarios WHERE Id = @Id", new { Id = dto.UsuarioId });
            if (!usuarioExists.HasValue)
                return NotFound(new { message = "Usuario no encontrado" });

            var proveedorRnc = await conn.QueryFirstOrDefaultAsync<string>(
                "SELECT Rnc FROM Proveedores WHERE Id = @Id", new { Id = dto.ProveedorId });
            if (proveedorRnc == null)
                return NotFound(new { message = "Proveedor no encontrado" });

            var existe = await conn.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM UsuarioProveedor WHERE UsuarioId = @UsuarioId",
                new { dto.UsuarioId });

            if (existe > 0)
                return BadRequest(new { message = "El usuario ya está asociado a un proveedor" });

            await conn.ExecuteAsync(
                "INSERT INTO UsuarioProveedor (UsuarioId, RncProveedor) VALUES (@UsuarioId, @RncProveedor)",
                new { dto.UsuarioId, RncProveedor = proveedorRnc });

            return Ok(new { message = "Usuario asociado al proveedor exitosamente" });
        }

        // GET /api/usuarios/{id}/asociaciones
        [HttpGet("{id:int}/asociaciones")]
        public async Task<IActionResult> GetAsociaciones(int id)
        {
            using var conn = _db.Create();

            var empresa = await conn.QueryFirstOrDefaultAsync<object>(@"
                SELECT e.Id, e.Nombre, e.Rnc
                FROM UsuarioEmpresa ue
                INNER JOIN Empresas e ON ue.EmpresaId = e.Id
                WHERE ue.UsuarioId = @UsuarioId",
                new { UsuarioId = id });

            var proveedor = await conn.QueryFirstOrDefaultAsync<object>(@"
                SELECT p.Id, p.Nombre, p.Rnc
                FROM UsuarioProveedor up
                INNER JOIN Proveedores p ON up.RncProveedor = p.Rnc
                WHERE up.UsuarioId = @UsuarioId",
                new { UsuarioId = id });

            return Ok(new { empresa, proveedor });
        }

        // DELETE /api/usuarios/{id}/asociacion-empresa
        [HttpDelete("{id:int}/asociacion-empresa")]
        public async Task<IActionResult> EliminarAsociacionEmpresa(int id)
        {
            using var conn = _db.Create();

            var deleted = await conn.ExecuteAsync(
                "DELETE FROM UsuarioEmpresa WHERE UsuarioId = @UsuarioId",
                new { UsuarioId = id });

            if (deleted == 0)
                return NotFound(new { message = "No se encontró asociación" });

            return Ok(new { message = "Asociación eliminada" });
        }

        // DELETE /api/usuarios/{id}/asociacion-proveedor
        [HttpDelete("{id:int}/asociacion-proveedor")]
        public async Task<IActionResult> EliminarAsociacionProveedor(int id)
        {
            using var conn = _db.Create();

            var deleted = await conn.ExecuteAsync(
                "DELETE FROM UsuarioProveedor WHERE UsuarioId = @UsuarioId",
                new { UsuarioId = id });

            if (deleted == 0)
                return NotFound(new { message = "No se encontró asociación" });

            return Ok(new { message = "Asociación eliminada" });
        }

        public class AsociarEmpresaDto
        {
            public int UsuarioId { get; set; }
            public int EmpresaId { get; set; }
        }

        public class AsociarProveedorDto
        {
            public int UsuarioId { get; set; }
            public int ProveedorId { get; set; }
        }
    }
}