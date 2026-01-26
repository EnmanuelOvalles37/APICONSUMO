using Dapper;
using Consumo_App.Data.Sql;
using Consumo_App.Models.Pagos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/reportes/pagos")]
    [Authorize]
    public class ReportesPagosController : ControllerBase
    {
        private readonly SqlConnectionFactory _connectionFactory;

        public ReportesPagosController(SqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        #region Dashboard Resumen

        /// <summary>
        /// GET /api/reportes/pagos/dashboard
        /// </summary>
        [HttpGet("dashboard")]
        public async Task<IActionResult> Dashboard()
        {
            using var connection = _connectionFactory.Create();

            var hoy = DateTime.UtcNow;
            var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
            var proximos7Dias = hoy.AddDays(7);

            // Todo en una sola query con subqueries
            const string sql = @"
                SELECT
                    -- CxC
                    (SELECT ISNULL(SUM(MontoPendiente), 0) FROM CxcDocumentos 
                     WHERE Estado NOT IN (3, 4) AND Refinanciado = 0) AS CxcPendiente,
                    
                    (SELECT ISNULL(SUM(MontoPendiente), 0) FROM CxcDocumentos 
                     WHERE FechaVencimiento < @Hoy AND Estado NOT IN (3, 4) AND Refinanciado = 0) AS CxcVencido,
                    
                    (SELECT ISNULL(SUM(MontoPendiente), 0) FROM RefinanciamientoDeudas 
                     WHERE Estado NOT IN (4, 5)) AS CxcRefinanciado,
                    
                    (SELECT ISNULL(SUM(Monto), 0) FROM CxcPagos 
                     WHERE Anulado = 0 AND Fecha >= @InicioMes) AS CxcCobradoMes,
                    
                    (SELECT COUNT(*) FROM CxcDocumentos 
                     WHERE FechaVencimiento >= @Hoy AND FechaVencimiento <= @Proximos7 
                     AND Estado NOT IN (3, 4) AND Refinanciado = 0) AS CxcProximosVencer,
                    
                    -- CxP
                    (SELECT ISNULL(SUM(MontoPendiente), 0) FROM CxpDocumentos 
                     WHERE Anulado = 0 AND Estado != 2) AS CxpPendiente,
                    
                    (SELECT ISNULL(SUM(MontoPendiente), 0) FROM CxpDocumentos 
                     WHERE FechaVencimiento < @Hoy AND Anulado = 0 AND Estado != 2) AS CxpVencido,
                    
                    (SELECT ISNULL(SUM(Monto), 0) FROM CxpPagos 
                     WHERE Anulado = 0 AND Fecha >= @InicioMes) AS CxpPagadoMes,
                    
                    (SELECT COUNT(*) FROM CxpDocumentos 
                     WHERE FechaVencimiento >= @Hoy AND FechaVencimiento <= @Proximos7 
                     AND Anulado = 0 AND Estado != 2) AS CxpProximosVencer";

            var data = await connection.QueryFirstAsync<dynamic>(sql, new
            {
                Hoy = hoy,
                InicioMes = inicioMes,
                Proximos7 = proximos7Dias
            });

            return Ok(new
            {
                CuentasPorCobrar = new
                {
                    TotalPendiente = (decimal)data.CxcPendiente,
                    TotalVencido = (decimal)data.CxcVencido,
                    TotalRefinanciado = (decimal)data.CxcRefinanciado,
                    CobradoEsteMes = (decimal)data.CxcCobradoMes,
                    ProximosAVencer = (int)data.CxcProximosVencer
                },
                CuentasPorPagar = new
                {
                    TotalPendiente = (decimal)data.CxpPendiente,
                    TotalVencido = (decimal)data.CxpVencido,
                    PagadoEsteMes = (decimal)data.CxpPagadoMes,
                    ProximosAVencer = (int)data.CxpProximosVencer
                },
                FechaConsulta = hoy.AddHours(-4).ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        #endregion

        #region Antigüedad CxC

        /// <summary>
        /// GET /api/reportes/pagos/antiguedad-cxc
        /// </summary>
        [HttpGet("antiguedad-cxc")]
        public async Task<IActionResult> AntiguedadCxc([FromQuery] int? empresaId)
        {
            using var connection = _connectionFactory.Create();
            var hoy = DateTime.UtcNow;

            var whereEmpresa = empresaId.HasValue ? "AND d.EmpresaId = @EmpresaId" : "";

            var sql = $@"
                SELECT 
                    d.Id, d.NumeroDocumento, d.EmpresaId,
                    e.Nombre AS EmpresaNombre,
                    d.FechaEmision, d.FechaVencimiento, d.MontoPendiente,
                    CASE WHEN d.FechaVencimiento < @Hoy 
                         THEN DATEDIFF(DAY, d.FechaVencimiento, @Hoy) ELSE 0 END AS DiasVencido
                FROM CxcDocumentos d
                INNER JOIN Empresas e ON d.EmpresaId = e.Id
                WHERE d.Estado NOT IN (3, 4) AND d.Refinanciado = 0 {whereEmpresa}
                ORDER BY d.FechaVencimiento";

            var documentos = (await connection.QueryAsync<DocumentoAntiguedad>(sql, new { Hoy = hoy, EmpresaId = empresaId })).ToList();

            // Clasificar por antigüedad
            var corriente = documentos.Where(d => d.DiasVencido <= 0).ToList();
            var de1a30 = documentos.Where(d => d.DiasVencido > 0 && d.DiasVencido <= 30).ToList();
            var de31a60 = documentos.Where(d => d.DiasVencido > 30 && d.DiasVencido <= 60).ToList();
            var de61a90 = documentos.Where(d => d.DiasVencido > 60 && d.DiasVencido <= 90).ToList();
            var mas90 = documentos.Where(d => d.DiasVencido > 90).ToList();

            return Ok(new
            {
                Resumen = new
                {
                    Corriente = corriente.Sum(d => d.MontoPendiente),
                    De1a30Dias = de1a30.Sum(d => d.MontoPendiente),
                    De31a60Dias = de31a60.Sum(d => d.MontoPendiente),
                    De61a90Dias = de61a90.Sum(d => d.MontoPendiente),
                    Mas90Dias = mas90.Sum(d => d.MontoPendiente),
                    Total = documentos.Sum(d => d.MontoPendiente)
                },
                Detalle = new { Corriente = corriente, De1a30Dias = de1a30, De31a60Dias = de31a60, De61a90Dias = de61a90, Mas90Dias = mas90 }
            });
        }

        /// <summary>
        /// GET /api/reportes/pagos/antiguedad-cxc-por-empresa
        /// </summary>
        [HttpGet("antiguedad-cxc-por-empresa")]
        public async Task<IActionResult> AntiguedadCxcPorEmpresa()
        {
            using var connection = _connectionFactory.Create();
            var hoy = DateTime.UtcNow;

            const string sql = @"
                SELECT 
                    e.Id AS EmpresaId, e.Nombre AS EmpresaNombre, e.Rnc AS EmpresaRnc,
                    SUM(CASE WHEN d.FechaVencimiento >= @Hoy THEN d.MontoPendiente ELSE 0 END) AS Corriente,
                    SUM(CASE WHEN d.FechaVencimiento < @Hoy AND DATEDIFF(DAY, d.FechaVencimiento, @Hoy) <= 30 THEN d.MontoPendiente ELSE 0 END) AS De1a30Dias,
                    SUM(CASE WHEN DATEDIFF(DAY, d.FechaVencimiento, @Hoy) > 30 AND DATEDIFF(DAY, d.FechaVencimiento, @Hoy) <= 60 THEN d.MontoPendiente ELSE 0 END) AS De31a60Dias,
                    SUM(CASE WHEN DATEDIFF(DAY, d.FechaVencimiento, @Hoy) > 60 AND DATEDIFF(DAY, d.FechaVencimiento, @Hoy) <= 90 THEN d.MontoPendiente ELSE 0 END) AS De61a90Dias,
                    SUM(CASE WHEN DATEDIFF(DAY, d.FechaVencimiento, @Hoy) > 90 THEN d.MontoPendiente ELSE 0 END) AS Mas90Dias,
                    SUM(d.MontoPendiente) AS Total
                FROM CxcDocumentos d
                INNER JOIN Empresas e ON d.EmpresaId = e.Id
                WHERE d.Estado NOT IN (3, 4) AND d.Refinanciado = 0
                GROUP BY e.Id, e.Nombre, e.Rnc
                HAVING SUM(d.MontoPendiente) > 0
                ORDER BY SUM(d.MontoPendiente) DESC";

            var data = (await connection.QueryAsync<dynamic>(sql, new { Hoy = hoy })).ToList();

            return Ok(new
            {
                Resumen = new
                {
                    Corriente = data.Sum(d => (decimal)d.Corriente),
                    De1a30Dias = data.Sum(d => (decimal)d.De1a30Dias),
                    De31a60Dias = data.Sum(d => (decimal)d.De31a60Dias),
                    De61a90Dias = data.Sum(d => (decimal)d.De61a90Dias),
                    Mas90Dias = data.Sum(d => (decimal)d.Mas90Dias),
                    Total = data.Sum(d => (decimal)d.Total)
                },
                PorEmpresa = data
            });
        }

        #endregion

        #region Antigüedad CxP

        /// <summary>
        /// GET /api/reportes/pagos/antiguedad-cxp
        /// </summary>
        [HttpGet("antiguedad-cxp")]
        public async Task<IActionResult> AntiguedadCxp([FromQuery] int? proveedorId)
        {
            using var connection = _connectionFactory.Create();
            var hoy = DateTime.UtcNow;

            var whereProveedor = proveedorId.HasValue ? "AND d.ProveedorId = @ProveedorId" : "";

            var sql = $@"
                SELECT 
                    d.Id, d.NumeroDocumento, d.ProveedorId,
                    p.Nombre AS ProveedorNombre,
                    d.FechaEmision, d.FechaVencimiento, d.MontoPendiente,
                    CASE WHEN d.FechaVencimiento < @Hoy 
                         THEN DATEDIFF(DAY, d.FechaVencimiento, @Hoy) ELSE 0 END AS DiasVencido
                FROM CxpDocumentos d
                INNER JOIN Proveedores p ON d.ProveedorId = p.Id
                WHERE d.Anulado = 0 AND d.Estado != 2 {whereProveedor}
                ORDER BY d.FechaVencimiento";

            var documentos = (await connection.QueryAsync<DocumentoAntiguedadCxp>(sql, new { Hoy = hoy, ProveedorId = proveedorId })).ToList();

            var corriente = documentos.Where(d => d.DiasVencido <= 0).ToList();
            var de1a30 = documentos.Where(d => d.DiasVencido > 0 && d.DiasVencido <= 30).ToList();
            var de31a60 = documentos.Where(d => d.DiasVencido > 30 && d.DiasVencido <= 60).ToList();
            var de61a90 = documentos.Where(d => d.DiasVencido > 60 && d.DiasVencido <= 90).ToList();
            var mas90 = documentos.Where(d => d.DiasVencido > 90).ToList();

            return Ok(new
            {
                Resumen = new
                {
                    Corriente = corriente.Sum(d => d.MontoPendiente),
                    De1a30Dias = de1a30.Sum(d => d.MontoPendiente),
                    De31a60Dias = de31a60.Sum(d => d.MontoPendiente),
                    De61a90Dias = de61a90.Sum(d => d.MontoPendiente),
                    Mas90Dias = mas90.Sum(d => d.MontoPendiente),
                    Total = documentos.Sum(d => d.MontoPendiente)
                },
                Detalle = new { Corriente = corriente, De1a30Dias = de1a30, De31a60Dias = de31a60, De61a90Dias = de61a90, Mas90Dias = mas90 }
            });
        }

        /// <summary>
        /// GET /api/reportes/pagos/antiguedad-cxp-por-proveedor
        /// </summary>
        [HttpGet("antiguedad-cxp-por-proveedor")]
        public async Task<IActionResult> AntiguedadCxpPorProveedor()
        {
            using var connection = _connectionFactory.Create();
            var hoy = DateTime.UtcNow;

            const string sql = @"
                SELECT 
                    p.Id AS ProveedorId, p.Nombre AS ProveedorNombre, p.Rnc AS ProveedorRnc,
                    SUM(CASE WHEN d.FechaVencimiento >= @Hoy THEN d.MontoPendiente ELSE 0 END) AS Corriente,
                    SUM(CASE WHEN d.FechaVencimiento < @Hoy AND DATEDIFF(DAY, d.FechaVencimiento, @Hoy) <= 30 THEN d.MontoPendiente ELSE 0 END) AS De1a30Dias,
                    SUM(CASE WHEN DATEDIFF(DAY, d.FechaVencimiento, @Hoy) > 30 AND DATEDIFF(DAY, d.FechaVencimiento, @Hoy) <= 60 THEN d.MontoPendiente ELSE 0 END) AS De31a60Dias,
                    SUM(CASE WHEN DATEDIFF(DAY, d.FechaVencimiento, @Hoy) > 60 AND DATEDIFF(DAY, d.FechaVencimiento, @Hoy) <= 90 THEN d.MontoPendiente ELSE 0 END) AS De61a90Dias,
                    SUM(CASE WHEN DATEDIFF(DAY, d.FechaVencimiento, @Hoy) > 90 THEN d.MontoPendiente ELSE 0 END) AS Mas90Dias,
                    SUM(d.MontoPendiente) AS Total
                FROM CxpDocumentos d
                INNER JOIN Proveedores p ON d.ProveedorId = p.Id
                WHERE d.Anulado = 0 AND d.Estado != 2
                GROUP BY p.Id, p.Nombre, p.Rnc
                HAVING SUM(d.MontoPendiente) > 0
                ORDER BY SUM(d.MontoPendiente) DESC";

            var data = (await connection.QueryAsync<dynamic>(sql, new { Hoy = hoy })).ToList();

            return Ok(new
            {
                Resumen = new
                {
                    Corriente = data.Sum(d => (decimal)d.Corriente),
                    De1a30Dias = data.Sum(d => (decimal)d.De1a30Dias),
                    De31a60Dias = data.Sum(d => (decimal)d.De31a60Dias),
                    De61a90Dias = data.Sum(d => (decimal)d.De61a90Dias),
                    Mas90Dias = data.Sum(d => (decimal)d.Mas90Dias),
                    Total = data.Sum(d => (decimal)d.Total)
                },
                PorProveedor = data
            });
        }

        #endregion

        #region Histórico de Cobros y Pagos

        /// <summary>
        /// GET /api/reportes/pagos/historico-cobros?desde=&amp;hasta=
        /// </summary>
        [HttpGet("historico-cobros")]
        public async Task<IActionResult> HistoricoCobros(
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta,
            [FromQuery] int? empresaId)
        {
            using var connection = _connectionFactory.Create();

            var fechaHasta = hasta ?? DateTime.UtcNow;
            var fechaDesde = desde ?? fechaHasta.AddMonths(-1);

            var whereEmpresa = empresaId.HasValue ? "AND d.EmpresaId = @EmpresaId" : "";

            var sql = $@"
                SELECT 
                    p.Id, p.NumeroRecibo, p.Fecha, p.Monto, p.MetodoPago, p.Referencia,
                    d.NumeroDocumento, e.Nombre AS EmpresaNombre
                FROM CxcPagos p
                INNER JOIN CxcDocumentos d ON p.CxcDocumentoId = d.Id
                INNER JOIN Empresas e ON d.EmpresaId = e.Id
                WHERE p.Anulado = 0 AND p.Fecha >= @Desde AND p.Fecha < @Hasta {whereEmpresa}
                ORDER BY p.Fecha DESC";

            var pagosRaw = (await connection.QueryAsync<dynamic>(sql, new
            {
                Desde = fechaDesde,
                Hasta = fechaHasta.AddDays(1),
                EmpresaId = empresaId
            })).ToList();

            var pagos = pagosRaw.Select(p => new
            {
                p.Id,
                p.NumeroRecibo,
                Fecha = (DateTime)p.Fecha,
                Monto = (decimal)p.Monto,
                MetodoPago = (int)p.MetodoPago,
                MetodoPagoNombre = ((MetodoPago)(int)p.MetodoPago).ToString(),
                p.Referencia,
                p.NumeroDocumento,
                p.EmpresaNombre
            }).ToList();

            var porDia = pagos.GroupBy(p => p.Fecha.Date)
                .Select(g => new { Fecha = g.Key, Cantidad = g.Count(), Monto = g.Sum(p => p.Monto) })
                .OrderByDescending(x => x.Fecha).ToList();

            var porMetodo = pagos.GroupBy(p => p.MetodoPagoNombre)
                .Select(g => new { MetodoPago = g.Key, Cantidad = g.Count(), Monto = g.Sum(p => p.Monto) }).ToList();

            return Ok(new
            {
                Resumen = new
                {
                    TotalCobros = pagos.Count,
                    MontoTotal = pagos.Sum(p => p.Monto),
                    Desde = fechaDesde.ToString("yyyy-MM-dd"),
                    Hasta = fechaHasta.ToString("yyyy-MM-dd")
                },
                Pagos = pagos,
                PorDia = porDia,
                PorMetodoPago = porMetodo
            });
        }

        /// <summary>
        /// GET /api/reportes/pagos/historico-pagos?desde=&amp;hasta=
        /// </summary>
        [HttpGet("historico-pagos")]
        public async Task<IActionResult> HistoricoPagos(
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta,
            [FromQuery] int? proveedorId)
        {
            using var connection = _connectionFactory.Create();

            var fechaHasta = hasta ?? DateTime.UtcNow;
            var fechaDesde = desde ?? fechaHasta.AddMonths(-1);

            var whereProveedor = proveedorId.HasValue ? "AND d.ProveedorId = @ProveedorId" : "";

            var sql = $@"
                SELECT 
                    p.Id, p.NumeroComprobante AS NumeroRecibo, p.Fecha, p.Monto, p.MetodoPago, p.Referencia,
                    d.NumeroDocumento, pr.Nombre AS ProveedorNombre
                FROM CxpPagos p
                INNER JOIN CxpDocumentos d ON p.CxpDocumentoId = d.Id
                INNER JOIN Proveedores pr ON d.ProveedorId = pr.Id
                WHERE p.Anulado = 0 AND p.Fecha >= @Desde AND p.Fecha < @Hasta {whereProveedor}
                ORDER BY p.Fecha DESC";

            var pagosRaw = (await connection.QueryAsync<dynamic>(sql, new
            {
                Desde = fechaDesde,
                Hasta = fechaHasta.AddDays(1),
                ProveedorId = proveedorId
            })).ToList();

            var pagos = pagosRaw.Select(p => new
            {
                p.Id,
                p.NumeroRecibo,
                Fecha = (DateTime)p.Fecha,
                Monto = (decimal)p.Monto,
                MetodoPago = (int)p.MetodoPago,
                MetodoPagoNombre = ((MetodoPago)(int)p.MetodoPago).ToString(),
                p.Referencia,
                p.NumeroDocumento,
                p.ProveedorNombre
            }).ToList();

            var porDia = pagos.GroupBy(p => p.Fecha.Date)
                .Select(g => new { Fecha = g.Key, Cantidad = g.Count(), Monto = g.Sum(p => p.Monto) })
                .OrderByDescending(x => x.Fecha).ToList();

            var porMetodo = pagos.GroupBy(p => p.MetodoPagoNombre)
                .Select(g => new { MetodoPago = g.Key, Cantidad = g.Count(), Monto = g.Sum(p => p.Monto) }).ToList();

            var porProveedor = pagos.GroupBy(p => p.ProveedorNombre)
                .Select(g => new { ProveedorNombre = g.Key, Cantidad = g.Count(), Monto = g.Sum(p => p.Monto) })
                .OrderByDescending(x => x.Monto).ToList();

            return Ok(new
            {
                Resumen = new
                {
                    TotalPagos = pagos.Count,
                    MontoTotal = pagos.Sum(p => p.Monto),
                    Desde = fechaDesde.ToString("yyyy-MM-dd"),
                    Hasta = fechaHasta.ToString("yyyy-MM-dd")
                },
                Pagos = pagos,
                PorDia = porDia,
                PorMetodoPago = porMetodo,
                PorProveedor = porProveedor
            });
        }

        #endregion

        #region Flujo de Caja Proyectado

        /// <summary>
        /// GET /api/reportes/pagos/flujo-caja?dias=30
        /// </summary>
        [HttpGet("flujo-caja")]
        public async Task<IActionResult> FlujoCajaProyectado([FromQuery] int dias = 30)
        {
            using var connection = _connectionFactory.Create();

            var hoy = DateTime.UtcNow.Date;
            var hasta = hoy.AddDays(dias);

            // CxC por vencer (ingresos esperados)
            const string sqlCxc = @"
                SELECT CAST(FechaVencimiento AS DATE) AS Fecha, SUM(MontoPendiente) AS Monto
                FROM CxcDocumentos
                WHERE Estado NOT IN (3, 4) AND Refinanciado = 0
                  AND FechaVencimiento >= @Hoy AND FechaVencimiento <= @Hasta
                GROUP BY CAST(FechaVencimiento AS DATE)
                ORDER BY Fecha";

            var cxcPorVencer = (await connection.QueryAsync<(DateTime Fecha, decimal Monto)>(sqlCxc, new { Hoy = hoy, Hasta = hasta })).ToList();

            // CxP por vencer (egresos esperados)
            const string sqlCxp = @"
                SELECT CAST(FechaVencimiento AS DATE) AS Fecha, SUM(MontoPendiente) AS Monto
                FROM CxpDocumentos
                WHERE Anulado = 0 AND Estado != 2
                  AND FechaVencimiento >= @Hoy AND FechaVencimiento <= @Hasta
                GROUP BY CAST(FechaVencimiento AS DATE)
                ORDER BY Fecha";

            var cxpPorVencer = (await connection.QueryAsync<(DateTime Fecha, decimal Monto)>(sqlCxp, new { Hoy = hoy, Hasta = hasta })).ToList();

            // Combinar y calcular flujo diario
            var todasLasFechas = cxcPorVencer.Select(x => x.Fecha)
                .Union(cxpPorVencer.Select(x => x.Fecha))
                .OrderBy(f => f)
                .ToList();

            decimal saldoAcumulado = 0;
            var flujoDiario = todasLasFechas.Select(fecha =>
            {
                var ingresos = cxcPorVencer.Where(x => x.Fecha == fecha).Sum(x => x.Monto);
                var egresos = cxpPorVencer.Where(x => x.Fecha == fecha).Sum(x => x.Monto);
                saldoAcumulado += ingresos - egresos;

                return new
                {
                    Fecha = fecha,
                    Ingresos = ingresos,
                    Egresos = egresos,
                    Neto = ingresos - egresos,
                    SaldoAcumulado = saldoAcumulado
                };
            }).ToList();

            var totalIngresos = cxcPorVencer.Sum(x => x.Monto);
            var totalEgresos = cxpPorVencer.Sum(x => x.Monto);

            return Ok(new
            {
                Periodo = new { Desde = hoy, Hasta = hasta, Dias = dias },
                Resumen = new
                {
                    TotalIngresosEsperados = totalIngresos,
                    TotalEgresosEsperados = totalEgresos,
                    FlujoNetoEsperado = totalIngresos - totalEgresos
                },
                FlujoDiario = flujoDiario
            });
        }

        #endregion
    }

    #region Helper Classes

    public class DocumentoAntiguedad
    {
        public int Id { get; set; }
        public string NumeroDocumento { get; set; } = "";
        public int EmpresaId { get; set; }
        public string EmpresaNombre { get; set; } = "";
        public DateTime FechaEmision { get; set; }
        public DateTime FechaVencimiento { get; set; }
        public decimal MontoPendiente { get; set; }
        public int DiasVencido { get; set; }
    }

    public class DocumentoAntiguedadCxp
    {
        public int Id { get; set; }
        public string NumeroDocumento { get; set; } = "";
        public int ProveedorId { get; set; }
        public string ProveedorNombre { get; set; } = "";
        public DateTime FechaEmision { get; set; }
        public DateTime FechaVencimiento { get; set; }
        public decimal MontoPendiente { get; set; }
        public int DiasVencido { get; set; }
    }

    #endregion
}