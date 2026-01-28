// Controllers/AdminUsuariosController.cs
using Consumo_App.Data.Sql;
using Consumo_App.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using static Consumo_App.DTOs.SeguridadDtos;
using static Consumo_App.DTOs.UsuarioDtos;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/admin/usuarios")]
    [Authorize]
    public class AdminUsuariosController : ControllerBase
    {
        private readonly SqlConnectionFactory _db;
        private readonly IPasswordHasher _hasher;

        public AdminUsuariosController(SqlConnectionFactory db, IPasswordHasher hasher)
        {
            _db = db;
            _hasher = hasher;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            using var conn = _db.Create();
            var list = await conn.QueryAsync<UsuarioListDto>(@"
                SELECT 
                    u.Id,
                    u.Nombre,
                    r.Nombre AS RolNombre,
                    u.Activo,
                    u.CreadoUtc
                FROM Usuarios u
                LEFT JOIN Roles r ON u.RolId = r.Id
                ORDER BY u.Nombre");
            return Ok(list);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UsuarioCreateDto dto)
        {
            if (!PasswordPolicy.IsValid(dto.Contrasena, out var error))
                return BadRequest(error);

            using var conn = _db.Create();

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

            return CreatedAtAction(nameof(GetById), new { id }, new { Id = id });
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            using var conn = _db.Create();
            var u = await conn.QueryFirstOrDefaultAsync<UsuarioListDto>(@"
                SELECT 
                    u.Id,
                    u.Nombre,
                    r.Nombre AS RolNombre,
                    u.Activo,
                    u.CreadoUtc
                FROM Usuarios u
                LEFT JOIN Roles r ON u.RolId = r.Id
                WHERE u.Id = @Id",
                new { Id = id });

            if (u == null) return NotFound();
            return Ok(u);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UsuarioUpdateDto dto)
        {
            using var conn = _db.Create();

            var exists = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT 1 FROM Usuarios WHERE Id = @Id", new { Id = id });

            if (!exists.HasValue) return NotFound();

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

        [HttpPut("{id:int}/password")]
        public async Task<IActionResult> ChangePassword(int id, [FromBody] UsuarioChangePasswordDto dto)
        {
            using var conn = _db.Create();

            var u = await conn.QueryFirstOrDefaultAsync<UsuarioPasswordDto>(
                "SELECT Id, Contrasena FROM Usuarios WHERE Id = @Id",
                new { Id = id });

            if (u == null) return NotFound();

            // Validar contraseña actual
            if (!_hasher.Verify(dto.ContrasenaActual, u.Contrasena))
                return BadRequest("Contraseña actual incorrecta.");

            // Validar política de nueva contraseña
            if (!PasswordPolicy.IsValid(dto.ContrasenaNueva, out var error))
                return BadRequest(error);

            // Actualizar
            await conn.ExecuteAsync(
                "UPDATE Usuarios SET Contrasena = @Contrasena WHERE Id = @Id",
                new { Id = id, Contrasena = _hasher.Hash(dto.ContrasenaNueva) });

            return NoContent();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            using var conn = _db.Create();

            var rows = await conn.ExecuteAsync(
                "DELETE FROM Usuarios WHERE Id = @Id",
                new { Id = id });

            if (rows == 0) return NotFound();
            return NoContent();
        }
    }

    // DTO interno para el cambio de contraseña
    public class UsuarioPasswordDto
    {
        public int Id { get; set; }
        public string Contrasena { get; set; } = "";
    }
}