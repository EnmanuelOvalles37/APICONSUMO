using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Security.Claims;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]  
    public class EmpleadorController : ControllerBase
    {
        private readonly string _connectionString;

        public EmpleadorController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")!;
        }

       
        private int GetUsuarioIdFromToken()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out var userId))
                return userId;
            return 0;
        }

       
        [HttpGet("mi-empresa")]
        public async Task<IActionResult> ObtenerMiEmpresa()
        {
            var usuarioId = GetUsuarioIdFromToken();
            if (usuarioId == 0)
                return Unauthorized(new { message = "Token invalido" });

            var sql = @"
                SELECT 
                    e.Id,
                    e.Nombre,
                    e.Rnc,
                    e.Telefono,
                    e.Email,
                    e.Direccion,
                    e.Limite_Credito AS LimiteCredito,
                    e.DiaCorte,
                    e.Activo
                FROM Empresas e
                INNER JOIN UsuarioEmpresa ue ON e.Id = ue.EmpresaId
                WHERE ue.UsuarioId = @UsuarioId AND e.Activo = 1";

            using var conn = new SqlConnection(_connectionString);
            var empresa = await conn.QueryFirstOrDefaultAsync<EmpresaEmpleadorDto>(sql, new { UsuarioId = usuarioId });

            if (empresa == null)
                return NotFound(new { message = "No se encontro empresa asociada a este usuario" });

            return Ok(empresa);
        }

        
        [HttpGet("dashboard")]
        public async Task<IActionResult> ObtenerDashboard()
        {
            var usuarioId = GetUsuarioIdFromToken();
            if (usuarioId == 0)
                return Unauthorized(new { message = "Token invalido" });

            using var conn = new SqlConnection(_connectionString);

            // Obtener EmpresaId del usuario
            var empresaSql = @"
                SELECT e.Id, e.Nombre, e.Limite_Credito AS LimiteCredito, e.DiaCorte
                FROM Empresas e
                INNER JOIN UsuarioEmpresa ue ON e.Id = ue.EmpresaId
                WHERE ue.UsuarioId = @UsuarioId AND e.Activo = 1";

            var empresa = await conn.QueryFirstOrDefaultAsync<dynamic>(empresaSql, new { UsuarioId = usuarioId });

            if (empresa == null)
                return NotFound(new { message = "No se encontro empresa asociada" });

            int empresaId = empresa.Id;

            // Estadisticas
            var statsSql = @"
                -- Total empleados
                SELECT COUNT(*) FROM Clientes WHERE EmpresaId = @EmpresaId;
                
                -- Empleados activos
                SELECT COUNT(*) FROM Clientes WHERE EmpresaId = @EmpresaId AND Activo = 1;
                
                -- Credito total asignado (suma de SaldoOriginal)
                SELECT ISNULL(SUM(SaldoOriginal), 0) FROM Clientes WHERE EmpresaId = @EmpresaId AND Activo = 1;
                
                -- Credito disponible (suma de Saldo actual)
                SELECT ISNULL(SUM(Saldo), 0) FROM Clientes WHERE EmpresaId = @EmpresaId AND Activo = 1;
                
                -- Consumos del mes actual
                SELECT ISNULL(SUM(Monto), 0) FROM Consumos 
                WHERE EmpresaId = @EmpresaId AND Reversado = 0
                AND MONTH(Fecha) = MONTH(GETDATE()) AND YEAR(Fecha) = YEAR(GETDATE());
                
                -- Consumos de hoy
                SELECT ISNULL(SUM(Monto), 0) FROM Consumos 
                WHERE EmpresaId = @EmpresaId AND Reversado = 0
                AND CAST(Fecha AS DATE) = CAST(GETDATE() AS DATE);
                
                -- Total pendiente por pagar (CxC)
                SELECT ISNULL(SUM(MontoPendiente), 0) FROM CxcDocumentos 
                WHERE EmpresaId = @EmpresaId AND Anulado = 0 AND MontoPendiente > 0;
                
                -- Documentos pendientes
                SELECT COUNT(*) FROM CxcDocumentos 
                WHERE EmpresaId = @EmpresaId AND Anulado = 0 AND MontoPendiente > 0;";

            using var multi = await conn.QueryMultipleAsync(statsSql, new { EmpresaId = empresaId });

            var totalEmpleados = await multi.ReadFirstAsync<int>();
            var empleadosActivos = await multi.ReadFirstAsync<int>();
            var creditoAsignado = await multi.ReadFirstAsync<decimal>();
            var creditoDisponible = await multi.ReadFirstAsync<decimal>();
            var consumosMes = await multi.ReadFirstAsync<decimal>();
            var consumosHoy = await multi.ReadFirstAsync<decimal>();
            var montoPendiente = await multi.ReadFirstAsync<decimal>();
            var documentosPendientes = await multi.ReadFirstAsync<int>();

            var creditoUtilizado = creditoAsignado - creditoDisponible;
            var porcentajeUtilizado = creditoAsignado > 0
                ? Math.Round((creditoUtilizado / creditoAsignado) * 100, 1)
                : 0;

            var dashboard = new
            {
                empresaId,
                empresaNombre = empresa.Nombre,
                limiteCredito = empresa.LimiteCredito,
                diaCorte = empresa.DiaCorte,
                totalEmpleados,
                empleadosActivos,
                creditoAsignado,
                creditoDisponible,
                creditoUtilizado,
                porcentajeUtilizado,
                consumosMes,
                consumosHoy,
                montoPendiente,
                documentosPendientes
            };

            return Ok(dashboard);
        }

        
        [HttpGet("empleados")]
        public async Task<IActionResult> ObtenerEmpleados()
        {
            var usuarioId = GetUsuarioIdFromToken();
            if (usuarioId == 0)
                return Unauthorized(new { message = "Token invalido" });

            var sql = @"
                SELECT 
                    c.Id,
                    c.Codigo,
                    c.Nombre,
                    c.Cedula,
                    c.Grupo,
                    c.SaldoOriginal AS LimiteCredito,
                    c.Saldo AS SaldoDisponible,
                    (c.SaldoOriginal - c.Saldo) AS Consumido,
                    CASE WHEN c.SaldoOriginal > 0 
                        THEN ROUND(((c.SaldoOriginal - c.Saldo) / c.SaldoOriginal) * 100, 1) 
                        ELSE 0 END AS PorcentajeUtilizado,
                    c.Activo,
                    (SELECT COUNT(*) FROM Consumos WHERE ClienteId = c.Id AND Reversado = 0
                     AND MONTH(Fecha) = MONTH(GETDATE()) AND YEAR(Fecha) = YEAR(GETDATE())) AS ConsumosMes,
                    (SELECT ISNULL(SUM(Monto), 0) FROM Consumos WHERE ClienteId = c.Id AND Reversado = 0
                     AND MONTH(Fecha) = MONTH(GETDATE()) AND YEAR(Fecha) = YEAR(GETDATE())) AS MontoMes
                FROM Clientes c
                INNER JOIN Empresas e ON c.EmpresaId = e.Id
                INNER JOIN UsuarioEmpresa ue ON e.Id = ue.EmpresaId
                WHERE ue.UsuarioId = @UsuarioId
                ORDER BY c.Nombre";

            using var conn = new SqlConnection(_connectionString);
            var empleados = await conn.QueryAsync<EmpleadoDto>(sql, new { UsuarioId = usuarioId });

            return Ok(empleados);
        }

        
        [HttpGet("empleados-saldo-bajo")]
        public async Task<IActionResult> EmpleadosSaldoBajo()
        {
            var usuarioId = GetUsuarioIdFromToken();
            if (usuarioId == 0)
                return Unauthorized(new { message = "Token invalido" });

            var sql = @"
                SELECT 
                    c.Id,
                    c.Codigo,
                    c.Nombre,
                    c.Grupo,
                    c.SaldoOriginal AS LimiteCredito,
                    c.Saldo AS SaldoDisponible,
                    CASE WHEN c.SaldoOriginal > 0 
                        THEN ROUND((c.Saldo / c.SaldoOriginal) * 100, 1) 
                        ELSE 0 END AS PorcentajeDisponible
                FROM Clientes c
                INNER JOIN Empresas e ON c.EmpresaId = e.Id
                INNER JOIN UsuarioEmpresa ue ON e.Id = ue.EmpresaId
                WHERE ue.UsuarioId = @UsuarioId 
                AND c.Activo = 1
                AND c.SaldoOriginal > 0
                AND (c.Saldo / c.SaldoOriginal) < 0.20
                ORDER BY (c.Saldo / c.SaldoOriginal) ASC";

            using var conn = new SqlConnection(_connectionString);
            var empleados = await conn.QueryAsync<EmpleadoSaldoBajoDto>(sql, new { UsuarioId = usuarioId });

            return Ok(empleados);
        }

        
        [HttpGet("documentos-cxc")]
        public async Task<IActionResult> ObtenerDocumentosCxc()
        {
            var usuarioId = GetUsuarioIdFromToken();
            if (usuarioId == 0)
                return Unauthorized(new { message = "Token invalido" });

            var sql = @"
                SELECT 
                    d.Id,
                    d.NumeroDocumento,
                    d.FechaEmision,
                    d.FechaVencimiento,
                    d.PeriodoDesde,
                    d.PeriodoHasta,
                    d.MontoTotal,
                    d.MontoPagado,
                    d.MontoPendiente,
                    d.Estado,
                    d.CantidadConsumos,
                    d.CantidadEmpleados,
                    DATEDIFF(DAY, d.FechaVencimiento, GETDATE()) AS DiasVencido
                FROM CxcDocumentos d
                INNER JOIN Empresas e ON d.EmpresaId = e.Id
                INNER JOIN UsuarioEmpresa ue ON e.Id = ue.EmpresaId
                WHERE ue.UsuarioId = @UsuarioId AND d.Anulado = 0
                ORDER BY d.FechaVencimiento DESC";

            using var conn = new SqlConnection(_connectionString);
            var documentos = await conn.QueryAsync<DocumentoCxcEmpleadorDto>(sql, new { UsuarioId = usuarioId });

            return Ok(documentos);
        }

        
        [HttpGet("historial-pagos")]
        public async Task<IActionResult> HistorialPagos()
        {
            var usuarioId = GetUsuarioIdFromToken();
            if (usuarioId == 0)
                return Unauthorized(new { message = "Token invalido" });

            var sql = @"
                SELECT 
                    p.Id,
                    p.NumeroRecibo,
                    p.Fecha,
                    d.NumeroDocumento,
                    p.Monto,
                    p.MetodoPago,
                    p.Referencia,
                    p.Banco
                FROM CxcPagos p
                INNER JOIN CxcDocumentos d ON p.CxcDocumentoId = d.Id
                INNER JOIN Empresas e ON d.EmpresaId = e.Id
                INNER JOIN UsuarioEmpresa ue ON e.Id = ue.EmpresaId
                WHERE ue.UsuarioId = @UsuarioId AND p.Anulado = 0
                ORDER BY p.Fecha DESC";

            using var conn = new SqlConnection(_connectionString);
            var pagos = await conn.QueryAsync<PagoEmpleadorDto>(sql, new { UsuarioId = usuarioId });

            return Ok(pagos);
        }

      
        [HttpGet("reporte/por-empleado")]
        public async Task<IActionResult> ReportePorEmpleado(
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta)
        {
            var usuarioId = GetUsuarioIdFromToken();
            if (usuarioId == 0)
                return Unauthorized(new { message = "Token invalido" });

            var fechaDesde = desde ?? DateTime.Today.AddMonths(-1);
            var fechaHasta = (hasta ?? DateTime.Today).AddDays(1);

            var sql = @"
                SELECT 
                    cl.Id AS EmpleadoId,
                    cl.Codigo,
                    cl.Nombre AS EmpleadoNombre,
                    cl.Grupo,
                    COUNT(*) AS TotalConsumos,
                    SUM(c.Monto) AS MontoTotal,
                    AVG(c.Monto) AS PromedioConsumo,
                    MIN(c.Fecha) AS PrimerConsumo,
                    MAX(c.Fecha) AS UltimoConsumo,
                    cl.SaldoOriginal AS LimiteCredito,
                    cl.Saldo AS SaldoDisponible
                FROM Consumos c
                INNER JOIN Clientes cl ON c.ClienteId = cl.Id
                INNER JOIN Empresas e ON c.EmpresaId = e.Id
                INNER JOIN UsuarioEmpresa ue ON e.Id = ue.EmpresaId
                WHERE ue.UsuarioId = @UsuarioId 
                AND c.Reversado = 0
                AND c.Fecha >= @FechaDesde AND c.Fecha < @FechaHasta
                GROUP BY cl.Id, cl.Codigo, cl.Nombre, cl.Grupo, cl.SaldoOriginal, cl.Saldo
                ORDER BY MontoTotal DESC";

            using var conn = new SqlConnection(_connectionString);
            var reporte = await conn.QueryAsync<ReporteEmpleadoDto>(sql, new
            {
                UsuarioId = usuarioId,
                FechaDesde = fechaDesde,
                FechaHasta = fechaHasta
            });

            var resumen = new
            {
                totalEmpleados = reporte.Count(),
                totalConsumos = reporte.Sum(r => r.TotalConsumos),
                montoTotal = reporte.Sum(r => r.MontoTotal)
            };

            return Ok(new { data = reporte, resumen, fechaDesde, fechaHasta = fechaHasta.AddDays(-1) });
        }

        
        [HttpGet("reporte/por-periodo")]
        public async Task<IActionResult> ReportePorPeriodo(
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta)
        {
            var usuarioId = GetUsuarioIdFromToken();
            if (usuarioId == 0)
                return Unauthorized(new { message = "Token invalido" });

            var fechaDesde = desde ?? DateTime.Today.AddMonths(-1);
            var fechaHasta = (hasta ?? DateTime.Today).AddDays(1);

            var sql = @"
                SELECT 
                    CAST(c.Fecha AS DATE) AS Fecha,
                    COUNT(*) AS TotalConsumos,
                    SUM(c.Monto) AS MontoTotal,
                    COUNT(DISTINCT c.ClienteId) AS EmpleadosActivos
                FROM Consumos c
                INNER JOIN Empresas e ON c.EmpresaId = e.Id
                INNER JOIN UsuarioEmpresa ue ON e.Id = ue.EmpresaId
                WHERE ue.UsuarioId = @UsuarioId 
                AND c.Reversado = 0
                AND c.Fecha >= @FechaDesde AND c.Fecha < @FechaHasta
                GROUP BY CAST(c.Fecha AS DATE)
                ORDER BY Fecha DESC";

            using var conn = new SqlConnection(_connectionString);
            var reporte = await conn.QueryAsync<ReportePeriodoDto>(sql, new
            {
                UsuarioId = usuarioId,
                FechaDesde = fechaDesde,
                FechaHasta = fechaHasta
            });

            var resumen = new
            {
                diasConActividad = reporte.Count(),
                totalConsumos = reporte.Sum(r => r.TotalConsumos),
                montoTotal = reporte.Sum(r => r.MontoTotal),
                promedioDiario = reporte.Any() ? reporte.Average(r => r.MontoTotal) : 0
            };

            return Ok(new { data = reporte, resumen, fechaDesde, fechaHasta = fechaHasta.AddDays(-1) });
        }

        
        [HttpGet("ultimos-consumos")]
        public async Task<IActionResult> UltimosConsumos([FromQuery] int limit = 20)
        {
            var usuarioId = GetUsuarioIdFromToken();
            if (usuarioId == 0)
                return Unauthorized(new { message = "Token invalido" });

            var sql = @"
                SELECT TOP (@Limit)
                    c.Id,
                    c.Fecha,
                    cl.Nombre AS EmpleadoNombre,
                    cl.Codigo AS EmpleadoCodigo,
                    p.Nombre AS ProveedorNombre,
                    t.Nombre AS TiendaNombre,
                    c.Monto,
                    c.Reversado
                FROM Consumos c
                INNER JOIN Clientes cl ON c.ClienteId = cl.Id
                INNER JOIN Proveedores p ON c.ProveedorId = p.Id
                LEFT JOIN ProveedorTiendas t ON c.TiendaId = t.Id
                INNER JOIN Empresas e ON c.EmpresaId = e.Id
                INNER JOIN UsuarioEmpresa ue ON e.Id = ue.EmpresaId
                WHERE ue.UsuarioId = @UsuarioId
                ORDER BY c.Fecha DESC";

            using var conn = new SqlConnection(_connectionString);
            var consumos = await conn.QueryAsync<ConsumoEmpleadorDto>(sql, new { UsuarioId = usuarioId, Limit = limit });

            return Ok(consumos);
        }

        // Agregar estos 3 endpoints a tu EmpleadorController.cs

        /// <summary>
        /// GET /api/empleador/documentos-cxc/{id}
        /// Detalle de un documento CxC específico
        /// </summary>
        [HttpGet("documentos-cxc/{id:int}")]
        public async Task<IActionResult> ObtenerDocumentoCxcDetalle(int id)
        {
            var usuarioId = GetUsuarioIdFromToken();
            if (usuarioId == 0)
                return Unauthorized(new { message = "Token inválido" });

            using var conn = new SqlConnection(_connectionString);

            // Verificar que el documento pertenece a la empresa del usuario
            var sqlDoc = @"
        SELECT 
            d.Id,
            d.NumeroDocumento,
            d.FechaEmision,
            d.FechaVencimiento,
            d.PeriodoDesde,
            d.PeriodoHasta,
            d.MontoTotal,
            d.MontoPagado,
            d.MontoPendiente,
            d.Estado,
            d.CantidadConsumos,
            d.CantidadEmpleados,
            CASE WHEN d.FechaVencimiento < GETDATE() 
                 THEN DATEDIFF(DAY, d.FechaVencimiento, GETDATE()) 
                 ELSE 0 END AS DiasVencido
        FROM CxcDocumentos d
        INNER JOIN Empresas e ON d.EmpresaId = e.Id
        INNER JOIN UsuarioEmpresa ue ON e.Id = ue.EmpresaId
        WHERE d.Id = @Id AND ue.UsuarioId = @UsuarioId AND d.Anulado = 0";

            var documento = await conn.QueryFirstOrDefaultAsync<DocumentoCxcEmpleadorDto>(sqlDoc,
                new { Id = id, UsuarioId = usuarioId });

            if (documento == null)
                return NotFound(new { message = "Documento no encontrado" });

            // Obtener consumos del documento
            var sqlConsumos = @"
        SELECT 
            c.Id,
            c.Fecha,
            cl.Nombre AS EmpleadoNombre,
            cl.Codigo AS EmpleadoCodigo,
            p.Nombre AS ProveedorNombre,
            ISNULL(t.Nombre, '') AS TiendaNombre,
            ISNULL(c.Concepto, '') AS Concepto,
            det.Monto
        FROM CxcDocumentoDetalles det
        INNER JOIN Consumos c ON det.ConsumoId = c.Id
        INNER JOIN Clientes cl ON c.ClienteId = cl.Id
        INNER JOIN Proveedores p ON c.ProveedorId = p.Id
        LEFT JOIN ProveedorTiendas t ON c.TiendaId = t.Id
        WHERE det.CxcDocumentoId = @Id
        ORDER BY c.Fecha DESC";

            var consumos = await conn.QueryAsync<ConsumoDocumentoDetalleDto>(sqlConsumos, new { Id = id });

            // Obtener pagos del documento
            var sqlPagos = @"
        SELECT 
            p.Id,
            p.NumeroRecibo AS NumeroComprobante,
            p.Fecha,
            p.Monto,
            CASE p.MetodoPago 
                WHEN 0 THEN 'Efectivo'
                WHEN 1 THEN 'Transferencia'
                WHEN 2 THEN 'Tarjeta'
                WHEN 3 THEN 'Cheque'
                ELSE 'Otro'
            END AS MetodoPago,
            ISNULL(p.Referencia, '') AS Referencia
        FROM CxcPagos p
        WHERE p.CxcDocumentoId = @Id AND p.Anulado = 0
        ORDER BY p.Fecha DESC";

            var pagos = await conn.QueryAsync<PagoDocumentoDetalleDto>(sqlPagos, new { Id = id });

            return Ok(new
            {
                documento,
                consumos,
                pagos
            });
        }

        /// <summary>
        /// GET /api/empleador/empleados/{empleadoId}/consumos
        /// Consumos de un empleado específico en un rango de fechas
        /// </summary>
        [HttpGet("empleados/{empleadoId:int}/consumos")]
        public async Task<IActionResult> ConsumosEmpleado(int empleadoId, [FromQuery] DateTime? desde, [FromQuery] DateTime? hasta)
        {
            var usuarioId = GetUsuarioIdFromToken();
            if (usuarioId == 0)
                return Unauthorized(new { message = "Token inválido" });

            using var conn = new SqlConnection(_connectionString);

            // Verificar que el empleado pertenece a la empresa del usuario
            var verificar = await conn.QueryFirstOrDefaultAsync<int?>(@"
        SELECT c.Id FROM Clientes c
        INNER JOIN Empresas e ON c.EmpresaId = e.Id
        INNER JOIN UsuarioEmpresa ue ON e.Id = ue.EmpresaId
        WHERE c.Id = @EmpleadoId AND ue.UsuarioId = @UsuarioId",
                new { EmpleadoId = empleadoId, UsuarioId = usuarioId });

            if (verificar == null)
                return NotFound(new { message = "Empleado no encontrado" });

            var fechaDesde = desde ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var fechaHasta = (hasta ?? DateTime.Now).AddDays(1);

            var sql = @"
        SELECT 
            c.Id,
            c.Fecha,
            p.Nombre AS ProveedorNombre,
            ISNULL(t.Nombre, '') AS TiendaNombre,
            ISNULL(c.Concepto, '') AS Concepto,
            c.Monto,
            c.Reversado
        FROM Consumos c
        INNER JOIN Proveedores p ON c.ProveedorId = p.Id
        LEFT JOIN ProveedorTiendas t ON c.TiendaId = t.Id
        WHERE c.ClienteId = @EmpleadoId
          AND c.Fecha >= @Desde
          AND c.Fecha < @Hasta
        ORDER BY c.Fecha DESC";

            var consumos = await conn.QueryAsync<ConsumoEmpleadoDetalleDto>(sql, new
            {
                EmpleadoId = empleadoId,
                Desde = fechaDesde,
                Hasta = fechaHasta
            });

            return Ok(consumos);
        }

        /// <summary>
        /// GET /api/empleador/consumos-dia
        /// Consumos de un día específico
        /// </summary>
        [HttpGet("consumos-dia")]
        public async Task<IActionResult> ConsumosDia([FromQuery] DateTime fecha)
        {
            var usuarioId = GetUsuarioIdFromToken();
            if (usuarioId == 0)
                return Unauthorized(new { message = "Token inválido" });

            using var conn = new SqlConnection(_connectionString);

            var sql = @"
        SELECT 
            c.Id,
            c.Fecha,
            cl.Nombre AS EmpleadoNombre,
            cl.Codigo AS EmpleadoCodigo,
            p.Nombre AS ProveedorNombre,
            ISNULL(t.Nombre, '') AS TiendaNombre,
            ISNULL(c.Concepto, '') AS Concepto,
            c.Monto,
            c.Reversado
        FROM Consumos c
        INNER JOIN Clientes cl ON c.ClienteId = cl.Id
        INNER JOIN Empresas e ON c.EmpresaId = e.Id
        INNER JOIN UsuarioEmpresa ue ON e.Id = ue.EmpresaId
        INNER JOIN Proveedores p ON c.ProveedorId = p.Id
        LEFT JOIN ProveedorTiendas t ON c.TiendaId = t.Id
        WHERE ue.UsuarioId = @UsuarioId
          AND CAST(c.Fecha AS DATE) = @Fecha
        ORDER BY c.Fecha DESC";

            var consumos = await conn.QueryAsync<ConsumoEmpleadorDto>(sql, new
            {
                UsuarioId = usuarioId,
                Fecha = fecha.Date
            });

            return Ok(consumos);
        }

    }

    
    public class EmpresaEmpleadorDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
        public string Rnc { get; set; } = "";
        public string? Telefono { get; set; }
        public string? Email { get; set; }
        public string? Direccion { get; set; }
        public decimal LimiteCredito { get; set; }
        public int? DiaCorte { get; set; }
        public bool Activo { get; set; }
    }

    public class EmpleadoDto
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string? Cedula { get; set; }
        public string? Grupo { get; set; }
        public decimal LimiteCredito { get; set; }
        public decimal SaldoDisponible { get; set; }
        public decimal Consumido { get; set; }
        public decimal PorcentajeUtilizado { get; set; }
        public bool Activo { get; set; }
        public int ConsumosMes { get; set; }
        public decimal MontoMes { get; set; }
    }

    public class EmpleadoSaldoBajoDto
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string? Grupo { get; set; }
        public decimal LimiteCredito { get; set; }
        public decimal SaldoDisponible { get; set; }
        public decimal PorcentajeDisponible { get; set; }
    }

    public class DocumentoCxcEmpleadorDto
    {
        public int Id { get; set; }
        public string NumeroDocumento { get; set; } = "";
        public DateTime FechaEmision { get; set; }
        public DateTime FechaVencimiento { get; set; }
        public DateTime PeriodoDesde { get; set; }
        public DateTime PeriodoHasta { get; set; }
        public decimal MontoTotal { get; set; }
        public decimal MontoPagado { get; set; }
        public decimal MontoPendiente { get; set; }
        public int Estado { get; set; }
        public int CantidadConsumos { get; set; }
        public int CantidadEmpleados { get; set; }
        public int DiasVencido { get; set; }
    }

    public class PagoEmpleadorDto
    {
        public int Id { get; set; }
        public string NumeroRecibo { get; set; } = "";
        public DateTime Fecha { get; set; }
        public string NumeroDocumento { get; set; } = "";
        public decimal Monto { get; set; }
        public int MetodoPago { get; set; }
        public string? Referencia { get; set; }
        public string? Banco { get; set; }
    }

    public class ReporteEmpleadoDto
    {
        public int EmpleadoId { get; set; }
        public string Codigo { get; set; } = "";
        public string EmpleadoNombre { get; set; } = "";
        public string? Grupo { get; set; }
        public int TotalConsumos { get; set; }
        public decimal MontoTotal { get; set; }
        public decimal PromedioConsumo { get; set; }
        public DateTime PrimerConsumo { get; set; }
        public DateTime UltimoConsumo { get; set; }
        public decimal LimiteCredito { get; set; }
        public decimal SaldoDisponible { get; set; }
    }

    public class ReportePeriodoDto
    {
        public DateTime Fecha { get; set; }
        public int TotalConsumos { get; set; }
        public decimal MontoTotal { get; set; }
        public int EmpleadosActivos { get; set; }
    }

    public class ConsumoEmpleadorDto
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public string EmpleadoNombre { get; set; } = "";
        public string EmpleadoCodigo { get; set; } = "";
        public string ProveedorNombre { get; set; } = "";
        public string? TiendaNombre { get; set; }
        public decimal Monto { get; set; }
        public bool Reversado { get; set; }
    }

    public class ConsumoDocumentoDetalleDto
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public string EmpleadoNombre { get; set; } = "";
        public string EmpleadoCodigo { get; set; } = "";
        public string ProveedorNombre { get; set; } = "";
        public string TiendaNombre { get; set; } = "";
        public string Concepto { get; set; } = "";
        public decimal Monto { get; set; }
    }

    public class PagoDocumentoDetalleDto
    {
        public int Id { get; set; }
        public string NumeroComprobante { get; set; } = "";
        public DateTime Fecha { get; set; }
        public decimal Monto { get; set; }
        public string MetodoPago { get; set; } = "";
        public string Referencia { get; set; } = "";
    }

    public class ConsumoEmpleadoDetalleDto
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public string ProveedorNombre { get; set; } = "";
        public string TiendaNombre { get; set; } = "";
        public string Concepto { get; set; } = "";
        public decimal Monto { get; set; }
        public bool Reversado { get; set; }
    }
}