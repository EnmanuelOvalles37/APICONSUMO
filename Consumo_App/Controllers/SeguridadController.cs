// Controllers/SeguridadController.cs
using Consumo_App.Data.Sql;
using Consumo_App.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/seguridad")]
    [Authorize]
    public class SeguridadController : ControllerBase
    {
        private readonly SqlConnectionFactory _db;

        public SeguridadController(SqlConnectionFactory db) => _db = db;

        // GET /api/seguridad/roles
        [HttpGet("roles")]
        public async Task<IActionResult> ListRoles()
        {
            using var conn = _db.Create();
            var roles = await conn.QueryAsync<SeguridadDtos.RolDto>(@"
                SELECT Id, Nombre, Descripcion
                FROM Roles
                ORDER BY Nombre");
            return Ok(roles);
        }

        // GET /api/seguridad/permisos
        [HttpGet("permisos")]
        public async Task<IActionResult> ListPermisos()
        {
            using var conn = _db.Create();
            var permisos = await conn.QueryAsync<SeguridadDtos.PermisoDto>(@"
                SELECT Id, Codigo, Nombre, Ruta
                FROM Permisos
                ORDER BY Nombre");
            return Ok(permisos);
        }

        // GET /api/seguridad/roles/{rolId}/permisos
        [HttpGet("roles/{rolId:int}/permisos")]
        public async Task<IActionResult> GetPermisosDeRol(int rolId)
        {
            using var conn = _db.Create();

            // Obtener IDs de permisos asignados al rol
            var asignados = await conn.QueryAsync<int>(
                "SELECT PermisoId FROM RolesPermisos WHERE RolId = @RolId",
                new { RolId = rolId });

            var asignadosSet = asignados.ToHashSet();

            // Obtener todos los permisos
            var permisos = await conn.QueryAsync<PermisoBaseDto>(@"
                SELECT Id, Codigo, Nombre
                FROM Permisos
                ORDER BY Nombre");

            // Mapear con el check de asignación
            var data = permisos.Select(p => new SeguridadDtos.RolPermisoCheckDto(
                p.Id,
                p.Codigo,
                p.Nombre,
                asignadosSet.Contains(p.Id)
            )).ToList();

            return Ok(data);
        }

        // PUT /api/seguridad/roles/{rolId}/permisos
        [HttpPut("roles/{rolId:int}/permisos")]
        public async Task<IActionResult> UpdatePermisosDeRol(int rolId, [FromBody] SeguridadDtos.RolPermisosUpdateDto body)
        {
            using var conn = _db.Create();

            // Obtener permisos actuales del rol
            var actuales = await conn.QueryAsync<int>(
                "SELECT PermisoId FROM RolesPermisos WHERE RolId = @RolId",
                new { RolId = rolId });

            var idsActuales = actuales.ToHashSet();
            var target = body.PermisoIds.ToHashSet();

            // Calcular diferencias
            var paraAgregar = target.Except(idsActuales).ToList();
            var paraQuitar = idsActuales.Except(target).ToList();

            // Agregar nuevos
            if (paraAgregar.Count > 0)
            {
                await conn.ExecuteAsync(
                    "INSERT INTO RolesPermisos (RolId, PermisoId) VALUES (@RolId, @PermisoId)",
                    paraAgregar.Select(pid => new { RolId = rolId, PermisoId = pid }));
            }

            // Quitar no deseados
            if (paraQuitar.Count > 0)
            {
                await conn.ExecuteAsync(
                    "DELETE FROM RolesPermisos WHERE RolId = @RolId AND PermisoId IN @PermisoIds",
                    new { RolId = rolId, PermisoIds = paraQuitar });
            }

            return NoContent();
        }
    }

    // DTO auxiliar para el mapeo
    public class PermisoBaseDto
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = "";
        public string Nombre { get; set; } = "";
    }
}