
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;
using Microsoft.AspNetCore.Authorization;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ReportesController : ControllerBase
    {
        private readonly string _connectionString;

        public ReportesController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")!;
        }

       // GET /api/reportes/consumos?desde=2024-01-01&hasta=2024-12-31&empresaId=1&proveedorId=1
        
        [HttpGet("consumos")]
        public async Task<IActionResult> ReporteConsumos(
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta,
            [FromQuery] int? empresaId,
            [FromQuery] int? proveedorId)
        {
            var fechaDesde = desde ?? DateTime.Today.AddMonths(-1);
            var fechaHasta = (hasta ?? DateTime.Today).AddDays(1);

            var sql = @"
                SELECT 
                    c.Id,
                    c.Fecha,
                    cl.Codigo AS EmpleadoCodigo,
                    cl.Nombre AS EmpleadoNombre,
                    e.Nombre AS EmpresaNombre,
                    p.Nombre AS ProveedorNombre,
                    ISNULL(t.Nombre, '') AS TiendaNombre,
                    c.Monto,
                    c.Concepto,
                    c.Referencia,
                    ISNULL(c.MontoComision, 0) AS MontoComision,
                    ISNULL(c.MontoNetoProveedor, 0) AS MontoNetoProveedor
                FROM Consumos c
                INNER JOIN Clientes cl ON c.ClienteId = cl.Id
                INNER JOIN Empresas e ON c.EmpresaId = e.Id
                INNER JOIN Proveedores p ON c.ProveedorId = p.Id
                LEFT JOIN ProveedorTiendas t ON c.TiendaId = t.Id
                WHERE c.Fecha >= @FechaDesde AND c.Fecha < @FechaHasta
                AND c.Reversado = 0
                AND (@EmpresaId IS NULL OR c.EmpresaId = @EmpresaId)
                AND (@ProveedorId IS NULL OR c.ProveedorId = @ProveedorId)
                ORDER BY c.Fecha DESC";

            using var conn = new SqlConnection(_connectionString);
            var consumos = (await conn.QueryAsync<ConsumoReporteDto>(sql, new
            {
                FechaDesde = fechaDesde,
                FechaHasta = fechaHasta,
                EmpresaId = empresaId,
                ProveedorId = proveedorId
            })).ToList();

            var resumen = new
            {
                totalConsumos = consumos.Count,
                montoTotal = consumos.Sum(c => c.Monto),
                comisionTotal = consumos.Sum(c => c.MontoComision),
                netoProveedores = consumos.Sum(c => c.MontoNetoProveedor)
            };

            return Ok(new { data = consumos, resumen, fechaDesde, fechaHasta = fechaHasta.AddDays(-1) });
        }

       
        // GET /api/reportes/cobros-cxc?desde=2024-01-01&hasta=2024-12-31&empresaId=1
        
        [HttpGet("cobros-cxc")]
        public async Task<IActionResult> ReporteCobrosCxc(
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta,
            [FromQuery] int? empresaId)
        {
            var fechaDesde = desde ?? DateTime.Today.AddMonths(-1);
            var fechaHasta = (hasta ?? DateTime.Today).AddDays(1);

            var sql = @"
                SELECT 
                    p.Id,
                    p.NumeroRecibo,
                    p.Fecha,
                    d.NumeroDocumento,
                    e.Nombre AS EmpresaNombre,
                    p.Monto,
                    p.MetodoPago,
                    p.Referencia,
                    p.Banco
                FROM CxcPagos p
                INNER JOIN CxcDocumentos d ON p.CxcDocumentoId = d.Id
                INNER JOIN Empresas e ON d.EmpresaId = e.Id
                WHERE p.Fecha >= @FechaDesde AND p.Fecha < @FechaHasta
                AND p.Anulado = 0
                AND (@EmpresaId IS NULL OR d.EmpresaId = @EmpresaId)
                ORDER BY p.Fecha DESC";

            using var conn = new SqlConnection(_connectionString);
            var pagos = (await conn.QueryAsync<CxcPagoReporteDto>(sql, new
            {
                FechaDesde = fechaDesde,
                FechaHasta = fechaHasta,
                EmpresaId = empresaId
            })).ToList();

            var porMetodo = pagos.GroupBy(p => p.MetodoPago)
                .Select(g => new {
                    metodo = GetMetodoPagoNombre(g.Key),
                    cantidad = g.Count(),
                    monto = g.Sum(x => x.Monto)
                }).ToList();

            var resumen = new
            {
                totalPagos = pagos.Count,
                montoTotal = pagos.Sum(p => p.Monto),
                porMetodo
            };

            return Ok(new { data = pagos, resumen, fechaDesde, fechaHasta = fechaHasta.AddDays(-1) });
        }

       
        // GET /api/reportes/pagos-cxp?desde=2024-01-01&hasta=2024-12-31&proveedorId=1
        
        [HttpGet("pagos-cxp")]
        public async Task<IActionResult> ReportePagosCxp(
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta,
            [FromQuery] int? proveedorId)
        {
            var fechaDesde = desde ?? DateTime.Today.AddMonths(-1);
            var fechaHasta = (hasta ?? DateTime.Today).AddDays(1);

            var sql = @"
                SELECT 
                    p.Id,
                    p.NumeroComprobante,
                    p.Fecha,
                    d.NumeroDocumento,
                    pr.Nombre AS ProveedorNombre,
                    p.Monto,
                    p.MetodoPago,
                    p.Referencia,
                    p.BancoOrigen
                FROM CxpPagos p
                INNER JOIN CxpDocumentos d ON p.CxpDocumentoId = d.Id
                INNER JOIN Proveedores pr ON d.ProveedorId = pr.Id
                WHERE p.Fecha >= @FechaDesde AND p.Fecha < @FechaHasta
                AND p.Anulado = 0
                AND (@ProveedorId IS NULL OR d.ProveedorId = @ProveedorId)
                ORDER BY p.Fecha DESC";

            using var conn = new SqlConnection(_connectionString);
            var pagos = (await conn.QueryAsync<CxpPagoReporteDto>(sql, new
            {
                FechaDesde = fechaDesde,
                FechaHasta = fechaHasta,
                ProveedorId = proveedorId
            })).ToList();

            var porMetodo = pagos.GroupBy(p => p.MetodoPago)
                .Select(g => new {
                    metodo = GetMetodoPagoNombre(g.Key),
                    cantidad = g.Count(),
                    monto = g.Sum(x => x.Monto)
                }).ToList();

            var resumen = new
            {
                totalPagos = pagos.Count,
                montoTotal = pagos.Sum(p => p.Monto),
                porMetodo
            };

            return Ok(new { data = pagos, resumen, fechaDesde, fechaHasta = fechaHasta.AddDays(-1) });
        }

      
        // GET /api/reportes/antiguedad-cxc?empresaId=1
        
        [HttpGet("antiguedad-cxc")]
        public async Task<IActionResult> ReporteAntiguedadCxc([FromQuery] int? empresaId)
        {
            var sql = @"
                SELECT 
                    d.Id,
                    d.NumeroDocumento,
                    e.Nombre AS EmpresaNombre,
                    d.FechaEmision,
                    d.FechaVencimiento,
                    d.MontoTotal,
                    d.MontoPagado,
                    d.MontoPendiente,
                    d.Estado,
                    d.Refinanciado,
                    DATEDIFF(DAY, d.FechaVencimiento, GETDATE()) AS DiasVencido
                FROM CxcDocumentos d
                INNER JOIN Empresas e ON d.EmpresaId = e.Id
                WHERE d.MontoPendiente > 0 
                AND d.Anulado = 0
                AND (@EmpresaId IS NULL OR d.EmpresaId = @EmpresaId)
                ORDER BY d.FechaVencimiento ASC";

            using var conn = new SqlConnection(_connectionString);
            var documentos = (await conn.QueryAsync<AntiguedadCxcDto>(sql, new { EmpresaId = empresaId })).ToList();

            var porRango = new[]
            {
                new { rango = "Vigente", cantidad = documentos.Count(d => d.DiasVencido <= 0), monto = documentos.Where(d => d.DiasVencido <= 0).Sum(d => d.MontoPendiente) },
                new { rango = "1-30 dias", cantidad = documentos.Count(d => d.DiasVencido >= 1 && d.DiasVencido <= 30), monto = documentos.Where(d => d.DiasVencido >= 1 && d.DiasVencido <= 30).Sum(d => d.MontoPendiente) },
                new { rango = "31-60 dias", cantidad = documentos.Count(d => d.DiasVencido >= 31 && d.DiasVencido <= 60), monto = documentos.Where(d => d.DiasVencido >= 31 && d.DiasVencido <= 60).Sum(d => d.MontoPendiente) },
                new { rango = "61-90 dias", cantidad = documentos.Count(d => d.DiasVencido >= 61 && d.DiasVencido <= 90), monto = documentos.Where(d => d.DiasVencido >= 61 && d.DiasVencido <= 90).Sum(d => d.MontoPendiente) },
                new { rango = "Mas de 90", cantidad = documentos.Count(d => d.DiasVencido > 90), monto = documentos.Where(d => d.DiasVencido > 90).Sum(d => d.MontoPendiente) }
            };

            var resumen = new
            {
                totalDocumentos = documentos.Count,
                totalPendiente = documentos.Sum(d => d.MontoPendiente),
                porRango
            };

            return Ok(new { data = documentos, resumen });
        }

       
        // GET /api/reportes/antiguedad-cxp?proveedorId=1
        
        [HttpGet("antiguedad-cxp")]
        public async Task<IActionResult> ReporteAntiguedadCxp([FromQuery] int? proveedorId)
        {
            var sql = @"
                SELECT 
                    d.Id,
                    d.NumeroDocumento,
                    p.Nombre AS ProveedorNombre,
                    d.FechaEmision,
                    d.FechaVencimiento,
                    d.MontoBruto,
                    d.MontoComision,
                    d.MontoTotal,
                    d.MontoPagado,
                    d.MontoPendiente,
                    d.Estado,
                    DATEDIFF(DAY, d.FechaVencimiento, GETDATE()) AS DiasVencido
                FROM CxpDocumentos d
                INNER JOIN Proveedores p ON d.ProveedorId = p.Id
                WHERE d.MontoPendiente > 0 
                AND d.Anulado = 0
                AND (@ProveedorId IS NULL OR d.ProveedorId = @ProveedorId)
                ORDER BY d.FechaVencimiento ASC";

            using var conn = new SqlConnection(_connectionString);
            var documentos = (await conn.QueryAsync<AntiguedadCxpDto>(sql, new { ProveedorId = proveedorId })).ToList();

            var porRango = new[]
            {
                new { rango = "Vigente", cantidad = documentos.Count(d => d.DiasVencido <= 0), monto = documentos.Where(d => d.DiasVencido <= 0).Sum(d => d.MontoPendiente) },
                new { rango = "1-30 dias", cantidad = documentos.Count(d => d.DiasVencido >= 1 && d.DiasVencido <= 30), monto = documentos.Where(d => d.DiasVencido >= 1 && d.DiasVencido <= 30).Sum(d => d.MontoPendiente) },
                new { rango = "31-60 dias", cantidad = documentos.Count(d => d.DiasVencido >= 31 && d.DiasVencido <= 60), monto = documentos.Where(d => d.DiasVencido >= 31 && d.DiasVencido <= 60).Sum(d => d.MontoPendiente) },
                new { rango = "61-90 dias", cantidad = documentos.Count(d => d.DiasVencido >= 61 && d.DiasVencido <= 90), monto = documentos.Where(d => d.DiasVencido >= 61 && d.DiasVencido <= 90).Sum(d => d.MontoPendiente) },
                new { rango = "Mas de 90", cantidad = documentos.Count(d => d.DiasVencido > 90), monto = documentos.Where(d => d.DiasVencido > 90).Sum(d => d.MontoPendiente) }
            };

            var resumen = new
            {
                totalDocumentos = documentos.Count,
                totalPendiente = documentos.Sum(d => d.MontoPendiente),
                totalComision = documentos.Sum(d => d.MontoComision),
                porRango
            };

            return Ok(new { data = documentos, resumen });
        }

     
        // GET /api/reportes/refinanciamientos?empresaId=1&estado=0
        
        [HttpGet("refinanciamientos")]
        public async Task<IActionResult> ReporteRefinanciamientos(
            [FromQuery] int? empresaId,
            [FromQuery] int? estado)
        {
            var sql = @"
                SELECT 
                    r.Id,
                    r.NumeroRefinanciamiento,
                    e.Nombre AS EmpresaNombre,
                    r.Fecha,
                    r.FechaVencimiento,
                    r.MontoOriginal,
                    r.MontoPagado,
                    r.MontoPendiente,
                    r.Estado,
                    r.Motivo,
                    DATEDIFF(DAY, GETDATE(), r.FechaVencimiento) AS DiasRestantes
                FROM RefinanciamientoDeudas r
                INNER JOIN Empresas e ON r.EmpresaId = e.Id
                WHERE (@EmpresaId IS NULL OR r.EmpresaId = @EmpresaId)
                AND (@Estado IS NULL OR r.Estado = @Estado)
                ORDER BY r.FechaVencimiento ASC";

            using var conn = new SqlConnection(_connectionString);
            var refinanciamientos = (await conn.QueryAsync<RefinanciamientoReporteDto>(sql, new
            {
                EmpresaId = empresaId,
                Estado = estado
            })).ToList();

            var montoOriginalTotal = refinanciamientos.Sum(r => r.MontoOriginal);
            var montoPagadoTotal = refinanciamientos.Sum(r => r.MontoPagado);
            var porcentajeRecuperado = montoOriginalTotal > 0
                ? Math.Round(montoPagadoTotal / montoOriginalTotal * 100, 1)
                : 0;

            var porEstado = refinanciamientos.GroupBy(r => r.Estado)
                .Select(g => new {
                    estado = GetEstadoRefinanciamiento(g.Key),
                    cantidad = g.Count(),
                    monto = g.Sum(x => x.MontoPendiente)
                }).ToList();

            var resumen = new
            {
                total = refinanciamientos.Count,
                montoOriginalTotal,
                montoPagadoTotal,
                montoPendienteTotal = refinanciamientos.Sum(r => r.MontoPendiente),
                porcentajeRecuperado,
                porEstado
            };

            return Ok(new { data = refinanciamientos, resumen });
        }

        
        // LISTAS PARA FILTROS
        
        [HttpGet("listas/empresas")]
        public async Task<IActionResult> ListaEmpresas()
        {
            var sql = "SELECT Id, Nombre FROM Empresas WHERE Activo = 1 ORDER BY Nombre";
            using var conn = new SqlConnection(_connectionString);
            var empresas = await conn.QueryAsync<ListaItemDto>(sql);
            return Ok(empresas);
        }

        [HttpGet("listas/proveedores")]
        public async Task<IActionResult> ListaProveedores()
        {
            var sql = "SELECT Id, Nombre FROM Proveedores WHERE Activo = 1 ORDER BY Nombre";
            using var conn = new SqlConnection(_connectionString);
            var proveedores = await conn.QueryAsync<ListaItemDto>(sql);
            return Ok(proveedores);
        }

        
        private static string GetMetodoPagoNombre(int metodo) => metodo switch
        {
            0 => "Efectivo",
            1 => "Transferencia",
            2 => "Cheque",
            3 => "Tarjeta",
            4 => "Deposito",
            _ => "Otro"
        };

        private static string GetEstadoRefinanciamiento(int estado) => estado switch
        {
            0 => "Pendiente",
            1 => "Pagado",
            2 => "Parcial",
            3 => "Vencido",
            4 => "Castigado",
            _ => "Desconocido"
        };
    }

    public class ConsumoReporteDto
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public string EmpleadoCodigo { get; set; } = "";
        public string EmpleadoNombre { get; set; } = "";
        public string EmpresaNombre { get; set; } = "";
        public string ProveedorNombre { get; set; } = "";
        public string TiendaNombre { get; set; } = "";
        public decimal Monto { get; set; }
        public string? Concepto { get; set; }
        public string? Referencia { get; set; }
        public decimal MontoComision { get; set; }
        public decimal MontoNetoProveedor { get; set; }
    }

    public class CxcPagoReporteDto
    {
        public int Id { get; set; }
        public string NumeroRecibo { get; set; } = "";
        public DateTime Fecha { get; set; }
        public string NumeroDocumento { get; set; } = "";
        public string EmpresaNombre { get; set; } = "";
        public decimal Monto { get; set; }
        public int MetodoPago { get; set; }
        public string? Referencia { get; set; }
        public string? Banco { get; set; }
    }

    public class CxpPagoReporteDto
    {
        public int Id { get; set; }
        public string NumeroComprobante { get; set; } = "";
        public DateTime Fecha { get; set; }
        public string NumeroDocumento { get; set; } = "";
        public string ProveedorNombre { get; set; } = "";
        public decimal Monto { get; set; }
        public int MetodoPago { get; set; }
        public string? Referencia { get; set; }
        public string? BancoOrigen { get; set; }
    }

    public class AntiguedadCxcDto
    {
        public int Id { get; set; }
        public string NumeroDocumento { get; set; } = "";
        public string EmpresaNombre { get; set; } = "";
        public DateTime FechaEmision { get; set; }
        public DateTime FechaVencimiento { get; set; }
        public decimal MontoTotal { get; set; }
        public decimal MontoPagado { get; set; }
        public decimal MontoPendiente { get; set; }
        public int Estado { get; set; }
        public bool Refinanciado { get; set; }
        public int DiasVencido { get; set; }
    }

    public class AntiguedadCxpDto
    {
        public int Id { get; set; }
        public string NumeroDocumento { get; set; } = "";
        public string ProveedorNombre { get; set; } = "";
        public DateTime FechaEmision { get; set; }
        public DateTime FechaVencimiento { get; set; }
        public decimal MontoBruto { get; set; }
        public decimal MontoComision { get; set; }
        public decimal MontoTotal { get; set; }
        public decimal MontoPagado { get; set; }
        public decimal MontoPendiente { get; set; }
        public int Estado { get; set; }
        public int DiasVencido { get; set; }
    }

    public class RefinanciamientoReporteDto
    {
        public int Id { get; set; }
        public string NumeroRefinanciamiento { get; set; } = "";
        public string EmpresaNombre { get; set; } = "";
        public DateTime Fecha { get; set; }
        public DateTime FechaVencimiento { get; set; }
        public decimal MontoOriginal { get; set; }
        public decimal MontoPagado { get; set; }
        public decimal MontoPendiente { get; set; }
        public int Estado { get; set; }
        public string? Motivo { get; set; }
        public int DiasRestantes { get; set; }
    }

    public class ListaItemDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
    }
}