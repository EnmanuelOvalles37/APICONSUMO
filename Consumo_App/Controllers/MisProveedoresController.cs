using Dapper;
using Consumo_App.Data.Sql;
using Consumo_App.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/mis/proveedores")]
    [Authorize]
    public class MisProveedoresController : ControllerBase
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly IUserContext _user;

        public MisProveedoresController(SqlConnectionFactory connectionFactory, IUserContext user)
        {
            _connectionFactory = connectionFactory;
            _user = user;
        }

        [HttpGet]
        public async Task<IActionResult> Proveedores()
        {
            using var connection = _connectionFactory.Create();

            const string sql = @"
                SELECT DISTINCT p.Id, p.Nombre
                FROM ProveedorAsignaciones a
                INNER JOIN Proveedores p ON a.ProveedorId = p.Id
                WHERE a.UsuarioId = @UsuarioId 
                  AND a.Activo = 1 
                  AND p.Activo = 1
                ORDER BY p.Nombre";

            var list = await connection.QueryAsync<dynamic>(sql, new { UsuarioId = _user.Id });
            return Ok(list);
        }
    }

    [ApiController]
    [Route("api/mis/tiendas")]
    [Authorize]
    public class MisTiendasController : ControllerBase
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly IUserContext _user;

        public MisTiendasController(SqlConnectionFactory connectionFactory, IUserContext user)
        {
            _connectionFactory = connectionFactory;
            _user = user;
        }

        /// <summary>
        /// GET /api/mis/tiendas?proveedorId=#
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Tiendas([FromQuery] int proveedorId)
        {
            using var connection = _connectionFactory.Create();
            var uid = _user.Id;

            // Verificar si tiene nivel proveedor (TiendaId == null)
            var tieneNivelProveedor = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM ProveedorAsignaciones 
                WHERE UsuarioId = @UsuarioId 
                  AND ProveedorId = @ProveedorId 
                  AND TiendaId IS NULL 
                  AND Activo = 1",
                new { UsuarioId = uid, ProveedorId = proveedorId }) > 0;

            string sql;
            object parameters;

            if (tieneNivelProveedor)
            {
                // Todas las tiendas del proveedor
                sql = @"
                    SELECT t.Id, t.Nombre
                    FROM ProveedorTiendas t
                    WHERE t.ProveedorId = @ProveedorId AND t.Activo = 1
                    ORDER BY t.Nombre";
                parameters = new { ProveedorId = proveedorId };
            }
            else
            {
                // Solo tiendas explícitamente asignadas
                sql = @"
                    SELECT DISTINCT t.Id, t.Nombre
                    FROM ProveedorTiendas t
                    INNER JOIN ProveedorAsignaciones a ON t.Id = a.TiendaId
                    WHERE t.ProveedorId = @ProveedorId 
                      AND t.Activo = 1
                      AND a.UsuarioId = @UsuarioId 
                      AND a.ProveedorId = @ProveedorId 
                      AND a.TiendaId IS NOT NULL 
                      AND a.Activo = 1
                    ORDER BY t.Nombre";
                parameters = new { ProveedorId = proveedorId, UsuarioId = uid };
            }

            var list = await connection.QueryAsync<dynamic>(sql, parameters);
            return Ok(list);
        }
    }

    [ApiController]
    [Route("api/mis/cajas")]
    [Authorize]
    public class MisCajasController : ControllerBase
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly IUserContext _user;

        public MisCajasController(SqlConnectionFactory connectionFactory, IUserContext user)
        {
            _connectionFactory = connectionFactory;
            _user = user;
        }

        /// <summary>
        /// GET /api/mis/cajas?proveedorId=#&amp;tiendaId=#
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Cajas([FromQuery] int proveedorId, [FromQuery] int tiendaId)
        {
            using var connection = _connectionFactory.Create();
            var uid = _user.Id;

            // Verificar nivel tienda (CajaId == null para esa tienda)
            var tieneNivelTienda = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM ProveedorAsignaciones 
                WHERE UsuarioId = @UsuarioId 
                  AND ProveedorId = @ProveedorId 
                  AND TiendaId = @TiendaId 
                  AND CajaId IS NULL 
                  AND Activo = 1",
                new { UsuarioId = uid, ProveedorId = proveedorId, TiendaId = tiendaId }) > 0;

            // Verificar nivel proveedor (TiendaId == null)
            var tieneNivelProveedor = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM ProveedorAsignaciones 
                WHERE UsuarioId = @UsuarioId 
                  AND ProveedorId = @ProveedorId 
                  AND TiendaId IS NULL 
                  AND CajaId IS NULL 
                  AND Activo = 1",
                new { UsuarioId = uid, ProveedorId = proveedorId }) > 0;

            string sql;
            object parameters;

            if (tieneNivelProveedor || tieneNivelTienda)
            {
                // Todas las cajas de la tienda
                sql = @"
                    SELECT c.Id, c.Nombre
                    FROM ProveedorCajas c
                    INNER JOIN ProveedorTiendas t ON c.TiendaId = t.Id
                    WHERE t.ProveedorId = @ProveedorId 
                      AND c.TiendaId = @TiendaId 
                      AND c.Activo = 1
                    ORDER BY c.Nombre";
                parameters = new { ProveedorId = proveedorId, TiendaId = tiendaId };
            }
            else
            {
                // Solo cajas explícitamente asignadas
                sql = @"
                    SELECT DISTINCT c.Id, c.Nombre
                    FROM ProveedorCajas c
                    INNER JOIN ProveedorTiendas t ON c.TiendaId = t.Id
                    INNER JOIN ProveedorAsignaciones a ON c.Id = a.CajaId
                    WHERE t.ProveedorId = @ProveedorId 
                      AND c.TiendaId = @TiendaId 
                      AND c.Activo = 1
                      AND a.UsuarioId = @UsuarioId 
                      AND a.ProveedorId = @ProveedorId 
                      AND a.TiendaId = @TiendaId 
                      AND a.CajaId IS NOT NULL 
                      AND a.Activo = 1
                    ORDER BY c.Nombre";
                parameters = new { ProveedorId = proveedorId, TiendaId = tiendaId, UsuarioId = uid };
            }

            var list = await connection.QueryAsync<dynamic>(sql, parameters);
            return Ok(list);
        }
    }
}