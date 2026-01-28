using Dapper;
using Consumo_App.Data.Sql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PermisosController : ControllerBase
    {
        private readonly SqlConnectionFactory _connectionFactory;

        public PermisosController(SqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        /// <summary>
        /// GET /api/permisos
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<PermisoDto>>> GetAll()
        {
            using var connection = _connectionFactory.Create();

            const string sql = @"
                SELECT Id, Codigo, Nombre, Ruta
                FROM Permisos
                ORDER BY Codigo";

            var data = await connection.QueryAsync<PermisoDto>(sql);
            return Ok(data);
        }

        /// <summary>
        /// GET /api/permisos/{id}
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<ActionResult<PermisoDto>> GetById(int id)
        {
            using var connection = _connectionFactory.Create();

            const string sql = "SELECT Id, Codigo, Nombre, Ruta FROM Permisos WHERE Id = @Id";
            var permiso = await connection.QueryFirstOrDefaultAsync<PermisoDto>(sql, new { Id = id });

            if (permiso == null)
                return NotFound(new { message = "Permiso no encontrado." });

            return Ok(permiso);
        }

        /// <summary>
        /// GET /api/permisos/por-rol/{rolId}
        /// </summary>
        [HttpGet("por-rol/{rolId:int}")]
        public async Task<ActionResult<IEnumerable<PermisoDto>>> GetByRol(int rolId)
        {
            using var connection = _connectionFactory.Create();

            const string sql = @"
                SELECT p.Id, p.Codigo, p.Nombre, p.Ruta
                FROM Permisos p
                INNER JOIN RolesPermisos rp ON p.Id = rp.PermisoId
                WHERE rp.RolId = @RolId
                ORDER BY p.Codigo";

            var data = await connection.QueryAsync<PermisoDto>(sql, new { RolId = rolId });
            return Ok(data);
        }
    }

    public record PermisoDto(int Id, string Codigo, string Nombre, string? Ruta);
}