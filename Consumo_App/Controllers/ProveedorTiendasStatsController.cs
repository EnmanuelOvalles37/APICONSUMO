using Dapper;
using Consumo_App.Data.Sql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/proveedores/{proveedorId}/tiendas/{tiendaId}")]
    [Authorize]
    public class ProveedorTiendaStatsController : ControllerBase
    {
        private readonly SqlConnectionFactory _connectionFactory;

        public ProveedorTiendaStatsController(SqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        /// <summary>
        /// GET /api/proveedores/{proveedorId}/tiendas/{tiendaId}/cajas
        /// </summary>
        [HttpGet("cajas")]
        public async Task<IActionResult> GetCajas(int proveedorId, int tiendaId)
        {
            using var connection = _connectionFactory.Create();

            const string sql = @"
                SELECT c.Id, c.Nombre, c.Activo
                FROM ProveedorCajas c
                INNER JOIN ProveedorTiendas t ON c.TiendaId = t.Id
                WHERE c.TiendaId = @TiendaId 
                  AND t.ProveedorId = @ProveedorId 
                  AND c.Activo = 1
                ORDER BY c.Nombre";

            var cajas = await connection.QueryAsync<dynamic>(sql, new { ProveedorId = proveedorId, TiendaId = tiendaId });
            return Ok(cajas);
        }

        /// <summary>
        /// GET /api/proveedores/{proveedorId}/tiendas/{tiendaId}/usuarios
        /// </summary>
        [HttpGet("usuarios")]
        public async Task<IActionResult> GetUsuariosAsignados(int proveedorId, int tiendaId)
        {
            using var connection = _connectionFactory.Create();

            const string sql = @"
                SELECT DISTINCT
                    u.Id AS UsuarioId,
                    ISNULL(u.Nombre, 'Sin nombre') AS Nombre,
                    CASE 
                        WHEN a.TiendaId IS NULL AND a.CajaId IS NULL THEN 'Proveedor'
                        WHEN a.CajaId IS NULL THEN 'Tienda'
                        ELSE 'Caja'
                    END AS NivelAcceso
                FROM ProveedorAsignaciones a
                INNER JOIN Usuarios u ON a.UsuarioId = u.Id
                WHERE a.ProveedorId = @ProveedorId
                  AND a.Activo = 1
                  AND (a.TiendaId IS NULL OR a.TiendaId = @TiendaId)";

            var usuarios = await connection.QueryAsync<dynamic>(sql, new { ProveedorId = proveedorId, TiendaId = tiendaId });
            return Ok(usuarios);
        }

        /// <summary>
        /// GET /api/proveedores/{proveedorId}/tiendas/{tiendaId}/stats
        /// </summary>
        [HttpGet("stats")]
        public async Task<IActionResult> GetStats(int proveedorId, int tiendaId,
            [FromQuery] DateTime? desde, [FromQuery] DateTime? hasta)
        {
            using var connection = _connectionFactory.Create();

            var whereClause = "WHERE c.ProveedorId = @ProveedorId AND c.TiendaId = @TiendaId AND c.Reversado = 0";
            var parameters = new DynamicParameters();
            parameters.Add("ProveedorId", proveedorId);
            parameters.Add("TiendaId", tiendaId);

            if (desde.HasValue)
            {
                whereClause += " AND c.Fecha >= @Desde";
                parameters.Add("Desde", desde.Value);
            }

            if (hasta.HasValue)
            {
                whereClause += " AND c.Fecha <= @Hasta";
                parameters.Add("Hasta", hasta.Value);
            }

            // Totales
            var sqlTotales = $@"
                SELECT 
                    COUNT(*) AS TotalConsumos,
                    ISNULL(SUM(c.Monto), 0) AS MontoTotal
                FROM Consumos c
                {whereClause}";

            var totales = await connection.QueryFirstAsync<dynamic>(sqlTotales, parameters);

            // Por caja
            var sqlPorCaja = $@"
                SELECT 
                    c.CajaId,
                    ca.Nombre AS CajaNombre,
                    COUNT(*) AS Cantidad,
                    SUM(c.Monto) AS Total
                FROM Consumos c
                LEFT JOIN ProveedorCajas ca ON c.CajaId = ca.Id
                {whereClause}
                GROUP BY c.CajaId, ca.Nombre
                ORDER BY SUM(c.Monto) DESC";

            var porCaja = await connection.QueryAsync<dynamic>(sqlPorCaja, parameters);

            return Ok(new
            {
                TotalConsumos = (int)totales.TotalConsumos,
                MontoTotal = (decimal)totales.MontoTotal,
                PorCaja = porCaja
            });
        }

        /// <summary>
        /// POST /api/proveedores/{proveedorId}/tiendas/{tiendaId}/cajas
        /// </summary>
        [HttpPost("cajas")]
        public async Task<IActionResult> CreateCaja(int proveedorId, int tiendaId, [FromBody] CreateCajaDto dto)
        {
            using var connection = _connectionFactory.Create();

            // Verificar que la tienda existe y pertenece al proveedor
            var tiendaExiste = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM ProveedorTiendas 
                WHERE Id = @TiendaId AND ProveedorId = @ProveedorId",
                new { TiendaId = tiendaId, ProveedorId = proveedorId }) > 0;

            if (!tiendaExiste)
                return NotFound(new { message = "Tienda no encontrada" });

            const string sql = @"
                INSERT INTO ProveedorCajas (TiendaId, Nombre, Activo)
                OUTPUT INSERTED.Id, INSERTED.Nombre, INSERTED.Activo
                VALUES (@TiendaId, @Nombre, @Activo)";

            var caja = await connection.QueryFirstAsync<dynamic>(sql, new
            {
                TiendaId = tiendaId,
                Nombre = dto.Nombre.Trim(),
                dto.Activo
            });

            return Ok(new { caja.Id, caja.Nombre, caja.Activo });
        }

        /// <summary>
        /// PUT /api/proveedores/{proveedorId}/tiendas/{tiendaId}/cajas/{cajaId}
        /// </summary>
        [HttpPut("cajas/{cajaId}")]
        public async Task<IActionResult> UpdateCaja(int proveedorId, int tiendaId, int cajaId, [FromBody] UpdateCajaDto dto)
        {
            using var connection = _connectionFactory.Create();

            // Verificar que la caja existe y pertenece a la tienda/proveedor
            var cajaExiste = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM ProveedorCajas c
                INNER JOIN ProveedorTiendas t ON c.TiendaId = t.Id
                WHERE c.Id = @CajaId AND c.TiendaId = @TiendaId AND t.ProveedorId = @ProveedorId",
                new { CajaId = cajaId, TiendaId = tiendaId, ProveedorId = proveedorId }) > 0;

            if (!cajaExiste)
                return NotFound(new { message = "Caja no encontrada" });

            const string sql = @"
                UPDATE ProveedorCajas 
                SET Nombre = @Nombre, Activo = @Activo
                WHERE Id = @CajaId;

                SELECT Id, Nombre, Activo FROM ProveedorCajas WHERE Id = @CajaId";

            var caja = await connection.QueryFirstAsync<dynamic>(sql, new
            {
                CajaId = cajaId,
                Nombre = dto.Nombre.Trim(),
                dto.Activo
            });

            return Ok(new { caja.Id, caja.Nombre, caja.Activo });
        }

        /// <summary>
        /// DELETE /api/proveedores/{proveedorId}/tiendas/{tiendaId}/cajas/{cajaId}
        /// </summary>
        [HttpDelete("cajas/{cajaId}")]
        public async Task<IActionResult> DeleteCaja(int proveedorId, int tiendaId, int cajaId)
        {
            using var connection = _connectionFactory.Create();

            // Verificar que la caja existe y pertenece a la tienda/proveedor
            var cajaExiste = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM ProveedorCajas c
                INNER JOIN ProveedorTiendas t ON c.TiendaId = t.Id
                WHERE c.Id = @CajaId AND c.TiendaId = @TiendaId AND t.ProveedorId = @ProveedorId",
                new { CajaId = cajaId, TiendaId = tiendaId, ProveedorId = proveedorId }) > 0;

            if (!cajaExiste)
                return NotFound(new { message = "Caja no encontrada" });

            // Verificar si tiene consumos
            var tieneConsumos = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM Consumos WHERE CajaId = @CajaId",
                new { CajaId = cajaId }) > 0;

            if (tieneConsumos)
                return BadRequest(new { message = "No se puede eliminar una caja con consumos registrados." });

            await connection.ExecuteAsync(
                "DELETE FROM ProveedorCajas WHERE Id = @CajaId",
                new { CajaId = cajaId });

            return Ok(new { message = "Caja eliminada exitosamente" });
        }
    }

    // DTOs
    public record CreateCajaDto(string Nombre, bool Activo);
    public record UpdateCajaDto(string Nombre, bool Activo);
}