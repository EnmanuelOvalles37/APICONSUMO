using Dapper;
using Consumo_App.Data.Sql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/cajas/{cajaId:int}/consumos")]
    [Authorize]
    public class CajaConsumosController : ControllerBase
    {
        private readonly SqlConnectionFactory _connectionFactory;

        public CajaConsumosController(SqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        /// <summary>
        /// GET /api/cajas/{cajaId}/consumos?desde=2025-11-30&hasta=2025-11-30
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetConsumos(
            int cajaId,
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            using var connection = _connectionFactory.Create();

            // 1) Verificar que la caja existe
            var cajaExiste = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM ProveedorCajas WHERE Id = @CajaId",
                new { CajaId = cajaId }) > 0;

            if (!cajaExiste)
                return NotFound(new { message = "Caja no encontrada." });

            // 2) Construir filtros
            var whereClause = "WHERE c.CajaId = @CajaId";
            var parameters = new DynamicParameters();
            parameters.Add("CajaId", cajaId);

            // RD está en UTC-4, agregamos 4 horas para convertir fecha local a UTC
            if (desde.HasValue)
            {
                var desdeUtc = desde.Value.Date.AddHours(4);
                whereClause += " AND c.Fecha >= @DesdeUtc";
                parameters.Add("DesdeUtc", desdeUtc);
            }

            if (hasta.HasValue)
            {
                var hastaUtc = hasta.Value.Date.AddDays(1).AddHours(4);
                whereClause += " AND c.Fecha < @HastaUtc";
                parameters.Add("HastaUtc", hastaUtc);
            }

            // 3) Contar total
            var countSql = $@"
                SELECT COUNT(*) 
                FROM Consumos c
                {whereClause}";

            var total = await connection.ExecuteScalarAsync<int>(countSql, parameters);

            // 4) Obtener datos paginados
            parameters.Add("Offset", (page - 1) * pageSize);
            parameters.Add("PageSize", pageSize);

            var dataSql = $@"
                SELECT 
                    c.Id,
                    c.Fecha,
                    c.ClienteId,
                    ISNULL(cli.Nombre, '') AS ClienteNombre,
                    ISNULL(cli.Cedula, '') AS ClienteCedula,
                    c.ProveedorId,
                    ISNULL(p.Nombre, '') AS ProveedorNombre,
                    c.TiendaId,
                    ISNULL(t.Nombre, '') AS TiendaNombre,
                    c.CajaId,
                    ISNULL(caja.Nombre, '') AS CajaNombre,
                    c.Monto,
                    c.Concepto,
                    c.Referencia,
                    c.Reversado,
                    c.ReversadoUtc,
                    c.UsuarioRegistradorId,
                    ISNULL(u.Nombre, 'N/A') AS UsuarioRegistrador
                FROM Consumos c
                LEFT JOIN Clientes cli ON c.ClienteId = cli.Id
                LEFT JOIN Proveedores p ON c.ProveedorId = p.Id
                LEFT JOIN ProveedorTiendas t ON c.TiendaId = t.Id
                LEFT JOIN ProveedorCajas caja ON c.CajaId = caja.Id
                LEFT JOIN Usuarios u ON c.UsuarioRegistradorId = u.Id
                {whereClause}
                ORDER BY c.Fecha DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var consumosRaw = await connection.QueryAsync<dynamic>(dataSql, parameters);

            // 5) Proyectar con conversión de zona horaria
            var data = consumosRaw.Select(c => new
            {
                c.Id,
                Fecha = ((DateTime)c.Fecha).AddHours(-4),
                FechaUtc = (DateTime)c.Fecha,
                c.ClienteId,
                c.ClienteNombre,
                c.ClienteCedula,
                c.ProveedorId,
                c.ProveedorNombre,
                c.TiendaId,
                c.TiendaNombre,
                c.CajaId,
                c.CajaNombre,
                c.Monto,
                c.Concepto,
                c.Referencia,
                c.Reversado,
                c.ReversadoUtc,
                c.UsuarioRegistradorId,
                c.UsuarioRegistrador
            }).ToList();

            // 6) Calcular totales (solo no reversados)
            var resumenSql = $@"
                SELECT 
                    COUNT(*) AS Cantidad,
                    ISNULL(SUM(c.Monto), 0) AS MontoTotal
                FROM Consumos c
                {whereClause} AND c.Reversado = 0";

            // Recrear parámetros sin paginación
            var resumenParams = new DynamicParameters();
            resumenParams.Add("CajaId", cajaId);
            if (desde.HasValue) resumenParams.Add("DesdeUtc", desde.Value.Date.AddHours(4));
            if (hasta.HasValue) resumenParams.Add("HastaUtc", hasta.Value.Date.AddDays(1).AddHours(4));

            var resumen = await connection.QueryFirstAsync<dynamic>(resumenSql, resumenParams);

            return Ok(new
            {
                data,
                resumen = new
                {
                    totalConsumos = (int)resumen.Cantidad,
                    totalMonto = (decimal)resumen.MontoTotal,
                    desde = desde?.ToString("yyyy-MM-dd"),
                    hasta = hasta?.ToString("yyyy-MM-dd")
                },
                pagination = new
                {
                    total,
                    page,
                    pageSize,
                    totalPages = (int)Math.Ceiling(total / (double)pageSize)
                },
                montoTotal = (decimal)resumen.MontoTotal
            });
        }

        [HttpGet("por-dia")]
        public async Task<IActionResult> GetConsumosPorDia(
            int cajaId,
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta)
        {
            using var connection = _connectionFactory.Create();

            var whereClause = "WHERE c.CajaId = @CajaId AND c.Reversado = 0";
            var parameters = new DynamicParameters();
            parameters.Add("CajaId", cajaId);

            if (desde.HasValue)
            {
                var desdeUtc = desde.Value.Date.AddHours(4);
                whereClause += " AND c.Fecha >= @DesdeUtc";
                parameters.Add("DesdeUtc", desdeUtc);
            }

            if (hasta.HasValue)
            {
                var hastaUtc = hasta.Value.Date.AddDays(1).AddHours(4);
                whereClause += " AND c.Fecha < @HastaUtc";
                parameters.Add("HastaUtc", hastaUtc);
            }

            // Agrupar por día (convertir a hora local RD antes de agrupar)
            var sql = $@"
                SELECT 
                    CAST(DATEADD(HOUR, -4, c.Fecha) AS DATE) AS Fecha,
                    COUNT(*) AS Cantidad,
                    SUM(c.Monto) AS Total
                FROM Consumos c
                {whereClause}
                GROUP BY CAST(DATEADD(HOUR, -4, c.Fecha) AS DATE)
                ORDER BY CAST(DATEADD(HOUR, -4, c.Fecha) AS DATE) DESC";

            var porDia = await connection.QueryAsync<dynamic>(sql, parameters);

            return Ok(porDia);
        }

        [HttpGet("estadisticas")]
        public async Task<IActionResult> GetEstadisticas(int cajaId)
        {
            using var connection = _connectionFactory.Create();

            // Verificar que la caja existe
            var cajaExiste = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM ProveedorCajas WHERE Id = @CajaId",
                new { CajaId = cajaId }) > 0;

            if (!cajaExiste)
                return NotFound(new { message = "Caja no encontrada." });

            var ahoraUtc = DateTime.UtcNow;
            var ahoraLocal = ahoraUtc.AddHours(-4);
            var hoyInicioLocal = ahoraLocal.Date;

            var hoyInicioUtc = hoyInicioLocal.AddHours(4);
            var hoyFinUtc = hoyInicioUtc.AddDays(1);
            var hace7DiasUtc = hoyInicioUtc.AddDays(-6);
            var hace30DiasUtc = hoyInicioUtc.AddDays(-29);

            const string sql = @"
                SELECT 
                    -- Hoy
                    SUM(CASE WHEN Fecha >= @HoyInicioUtc AND Fecha < @HoyFinUtc THEN 1 ELSE 0 END) AS HoyCantidad,
                    ISNULL(SUM(CASE WHEN Fecha >= @HoyInicioUtc AND Fecha < @HoyFinUtc THEN Monto ELSE 0 END), 0) AS HoyMonto,
                    -- Semana (últimos 7 días)
                    SUM(CASE WHEN Fecha >= @Hace7DiasUtc AND Fecha < @HoyFinUtc THEN 1 ELSE 0 END) AS SemanaCantidad,
                    ISNULL(SUM(CASE WHEN Fecha >= @Hace7DiasUtc AND Fecha < @HoyFinUtc THEN Monto ELSE 0 END), 0) AS SemanaMonto,
                    -- Mes (últimos 30 días)
                    SUM(CASE WHEN Fecha >= @Hace30DiasUtc AND Fecha < @HoyFinUtc THEN 1 ELSE 0 END) AS MesCantidad,
                    ISNULL(SUM(CASE WHEN Fecha >= @Hace30DiasUtc AND Fecha < @HoyFinUtc THEN Monto ELSE 0 END), 0) AS MesMonto
                FROM Consumos
                WHERE CajaId = @CajaId AND Reversado = 0";

            var stats = await connection.QueryFirstAsync<dynamic>(sql, new
            {
                CajaId = cajaId,
                HoyInicioUtc = hoyInicioUtc,
                HoyFinUtc = hoyFinUtc,
                Hace7DiasUtc = hace7DiasUtc,
                Hace30DiasUtc = hace30DiasUtc
            });

            return Ok(new
            {
                hoy = new
                {
                    cantidad = (int)stats.HoyCantidad,
                    monto = (decimal)stats.HoyMonto
                },
                semana = new
                {
                    cantidad = (int)stats.SemanaCantidad,
                    monto = (decimal)stats.SemanaMonto
                },
                mes = new
                {
                    cantidad = (int)stats.MesCantidad,
                    monto = (decimal)stats.MesMonto
                },
                fechaConsulta = ahoraLocal.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }
    }
}