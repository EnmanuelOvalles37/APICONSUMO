// Controllers/AdminRolesController.cs
using Consumo_App.Data.Sql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using static Consumo_App.DTOs.UsuarioDtos;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/admin/roles")]
    [Authorize]
    public class AdminRolesController : ControllerBase
    {
        private readonly SqlConnectionFactory _db;

        public AdminRolesController(SqlConnectionFactory db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            using var conn = _db.Create();
            var list = await conn.QueryAsync<RolListDto>(@"
                SELECT Id, Nombre, Descripcion
                FROM Roles
                ORDER BY Nombre");
            return Ok(list);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] RolCreateDto dto)
        {
            using var conn = _db.Create();
            var id = await conn.QuerySingleAsync<int>(@"
                INSERT INTO Roles (Nombre, Descripcion)
                OUTPUT INSERTED.Id
                VALUES (@Nombre, @Descripcion)",
                new { Nombre = dto.Nombre.Trim(), Descripcion = dto.Descripcion.Trim() });

            return CreatedAtAction(nameof(GetById), new { id }, new { Id = id });
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            using var conn = _db.Create();
            var r = await conn.QueryFirstOrDefaultAsync<RolListDto>(
                "SELECT Id, Nombre, Descripcion FROM Roles WHERE Id = @Id",
                new { Id = id });

            if (r == null) return NotFound();
            return Ok(r);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] RolUpdateDto dto)
        {
            using var conn = _db.Create();

            var exists = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT 1 FROM Roles WHERE Id = @Id", new { Id = id });

            if (!exists.HasValue) return NotFound();

            await conn.ExecuteAsync(@"
                UPDATE Roles SET
                    Nombre = COALESCE(@Nombre, Nombre),
                    Descripcion = COALESCE(@Descripcion, Descripcion)
                WHERE Id = @Id",
                new
                {
                    Id = id,
                    Nombre = dto.Nombre?.Trim(),
                    Descripcion = dto.Descripcion?.Trim()
                });

            return NoContent();
        }

        // Permisos por rol
        [HttpGet("{id:int}/permisos")]
        public async Task<IActionResult> GetPermisos(int id)
        {
            using var conn = _db.Create();

            var exists = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT 1 FROM Roles WHERE Id = @Id", new { Id = id });

            if (!exists.HasValue) return NotFound();

            var ids = await conn.QueryAsync<int>(
                "SELECT PermisoId FROM RolesPermisos WHERE RolId = @RolId",
                new { RolId = id });

            return Ok(ids);
        }

        [HttpPut("{id:int}/permisos")]
        public async Task<IActionResult> SetPermisos(int id, [FromBody] RolPermisosDto dto)
        {
            if (id != dto.RolId)
                return BadRequest("RolId inconsistente.");

            using var conn = _db.Create();

            var exists = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT 1 FROM Roles WHERE Id = @Id", new { Id = id });

            if (!exists.HasValue) return NotFound();

            // Eliminar permisos actuales
            await conn.ExecuteAsync(
                "DELETE FROM RolesPermisos WHERE RolId = @RolId",
                new { RolId = id });

            // Insertar nuevos permisos
            var permisosUnicos = dto.PermisoIds.Distinct().ToList();
            if (permisosUnicos.Any())
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO RolesPermisos (RolId, PermisoId)
                    VALUES (@RolId, @PermisoId)",
                    permisosUnicos.Select(pid => new { RolId = id, PermisoId = pid }));
            }

            return NoContent();
        }
    }
}