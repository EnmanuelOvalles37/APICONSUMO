// Controllers/RolesController.cs
using Consumo_App.Data.Sql;
using Consumo_App.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using static Consumo_App.DTOs.SeguridadDtos;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class RolesController : ControllerBase
    {
        private readonly SqlConnectionFactory _db;
        private readonly ISeguridadService _seg;

        public RolesController(SqlConnectionFactory db, ISeguridadService seg)
        {
            _db = db;
            _seg = seg;
        }

        // GET api/roles
        [HttpGet]
        public async Task<IActionResult> GetRoles()
        {
            using var conn = _db.Create();
            var roles = await conn.QueryAsync<RolDto>(@"
                SELECT Id, Nombre, Descripcion
                FROM Roles
                ORDER BY Nombre");
            return Ok(roles);
        }

        // GET api/roles/permisos (catálogo completo)
        [HttpGet("permisos")]
        public async Task<IActionResult> GetPermisos()
        {
            using var conn = _db.Create();
            var permisos = await conn.QueryAsync<PermisoDto>(@"
                SELECT Id, Codigo, Nombre, Ruta
                FROM Permisos
                ORDER BY Codigo");
            return Ok(permisos);
        }

        // GET api/roles/{id}/permisos (lista chequeable por rol)
        [HttpGet("{id:int}/permisos")]
        public async Task<IActionResult> GetPermisosDeRol(int id)
        {
            using var conn = _db.Create();

            var rolExists = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT 1 FROM Roles WHERE Id = @Id",
                new { Id = id });

            if (!rolExists.HasValue)
                return NotFound("Rol no encontrado.");

            var todos = await conn.QueryAsync<PermisoBasicoDto>(@"
                SELECT Id, Codigo, Nombre
                FROM Permisos
                ORDER BY Codigo");

            var asignados = await _seg.GetPermisoIdsPorRolAsync(id);
            var asignadosSet = asignados.ToHashSet();

            var result = todos.Select(p => new RolPermisoCheckDto(
                p.Id,
                p.Codigo,
                p.Nombre,
                asignadosSet.Contains(p.Id)
            )).ToList();

            return Ok(result);
        }

        // PUT api/roles/{id}/permisos (set completo por Ids)
        [HttpPut("{id:int}/permisos")]
        public async Task<IActionResult> UpdatePermisosDeRol(int id, [FromBody] RolPermisosUpdateDto dto)
        {
            using var conn = _db.Create();

            var rolExists = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT 1 FROM Roles WHERE Id = @Id",
                new { Id = id });

            if (!rolExists.HasValue)
                return NotFound("Rol no encontrado.");

            // Validar que existan los permisos
            var existentes = await conn.QueryAsync<int>(@"
                SELECT Id FROM Permisos WHERE Id IN @Ids",
                new { Ids = dto.PermisoIds });

            await _seg.SetPermisosDeRolAsync(id, existentes.ToList());

            return NoContent();
        }

        // POST api/roles (crear rol)
        [HttpPost]
        public async Task<IActionResult> CreateRol([FromBody] RolDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Nombre))
                return BadRequest("Nombre requerido.");

            using var conn = _db.Create();

            var exists = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT 1 FROM Roles WHERE Nombre = @Nombre",
                new { Nombre = dto.Nombre });

            if (exists.HasValue)
                return Conflict("Ya existe un rol con ese nombre.");

            var id = await conn.QuerySingleAsync<int>(@"
                INSERT INTO Roles (Nombre, Descripcion)
                OUTPUT INSERTED.Id
                VALUES (@Nombre, @Descripcion)",
                new
                {
                    Nombre = dto.Nombre.Trim(),
                    Descripcion = dto.Descripcion ?? ""
                });

            return Ok(new RolDto(id, dto.Nombre.Trim(), dto.Descripcion ?? ""));
        }
    }

    // DTO auxiliar
    public class PermisoBasicoDto
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = "";
        public string Nombre { get; set; } = "";
    }
}