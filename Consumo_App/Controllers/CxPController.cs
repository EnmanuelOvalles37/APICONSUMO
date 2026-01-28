using Dapper;
using Consumo_App.Data.Sql;
using Consumo_App.Models;
using Consumo_App.Models.Pagos;
using Consumo_App.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/cxp")]
    [Authorize]
    public class CuentasPorPagarController : ControllerBase
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly IUserContext _user;

        public CuentasPorPagarController(SqlConnectionFactory connectionFactory, IUserContext user)
        {
            _connectionFactory = connectionFactory;
            _user = user;
        }

        #region Dashboard

        [HttpGet("dashboard")]
        public async Task<IActionResult> Dashboard()
        {
            using var connection = _connectionFactory.Create();
            var hoy = DateTime.UtcNow.Date;

            const string sqlTotales = @"
        SELECT 
            CAST(COUNT(*) AS INT) AS TotalDocumentos,
            ISNULL(SUM(MontoBruto), 0) AS TotalBruto,
            ISNULL(SUM(MontoComision), 0) AS TotalComision,
            ISNULL(SUM(MontoPendiente), 0) AS TotalPorPagar,
            ISNULL(SUM(CASE WHEN FechaVencimiento < @Hoy THEN MontoPendiente ELSE 0 END), 0) AS TotalVencido
        FROM CxpDocumentos
        WHERE Anulado = 0 AND Estado != 2";

            // Usar DTO tipado en lugar de dynamic
            var totales = await connection.QueryFirstAsync<DashboardTotalesDto>(sqlTotales, new { Hoy = hoy });

            var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
            const string sqlComisionesMes = @"
        SELECT ISNULL(SUM(MontoComision), 0) 
        FROM Consumos 
        WHERE Reversado = 0 AND Fecha >= @InicioMes";

            var comisionesMes = await connection.ExecuteScalarAsync<decimal>(sqlComisionesMes, new { InicioMes = inicioMes });

            const string sqlPorProveedor = @"
        SELECT TOP 10
            d.ProveedorId,
            ISNULL(p.Nombre, 'Sin nombre') AS ProveedorNombre,
            ISNULL(p.PorcentajeComision, 0) AS PorcentajeComision,
            CAST(COUNT(*) AS INT) AS Documentos,
            ISNULL(SUM(d.MontoBruto), 0) AS MontoBruto,
            ISNULL(SUM(d.MontoComision), 0) AS MontoComision,
            ISNULL(SUM(d.MontoPendiente), 0) AS MontoPorPagar
        FROM CxpDocumentos d
        INNER JOIN Proveedores p ON d.ProveedorId = p.Id
        WHERE d.Anulado = 0 AND d.Estado != 2
        GROUP BY d.ProveedorId, p.Nombre, p.PorcentajeComision
        ORDER BY SUM(d.MontoPendiente) DESC";

            // Usar DTO tipado
            var porProveedor = await connection.QueryAsync<DashboardProveedorDto>(sqlPorProveedor);

            return Ok(new
            {
                resumen = totales,  // Ya no necesitas cast manual
                comisionesMes,
                porProveedor
            });
        }


        /*
        /// <summary>
        /// GET /api/cxp/dashboard
        /// </summary>
        [HttpGet("dashboard")]
        public async Task<IActionResult> Dashboard()
        {
            using var connection = _connectionFactory.Create();
            var hoy = DateTime.UtcNow.Date;

            // Totales de documentos pendientes
            const string sqlTotales = @"
                SELECT 
                    COUNT(*) AS TotalDocumentos,
                    ISNULL(SUM(MontoBruto), 0) AS TotalBruto,
                    ISNULL(SUM(MontoComision), 0) AS TotalComision,
                    ISNULL(SUM(MontoPendiente), 0) AS TotalPorPagar,
                    ISNULL(SUM(CASE WHEN FechaVencimiento < @Hoy THEN MontoPendiente ELSE 0 END), 0) AS TotalVencido
                FROM CxpDocumentos
                WHERE Anulado = 0 AND Estado != 2"; // 2 = Pagado

            var totales = await connection.QueryFirstAsync<dynamic>(sqlTotales, new { Hoy = hoy });

            // Comisiones del mes
            var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
            const string sqlComisionesMes = @"
                SELECT ISNULL(SUM(MontoComision), 0) 
                FROM Consumos 
                WHERE Reversado = 0 AND Fecha >= @InicioMes";

            var comisionesMes = await connection.ExecuteScalarAsync<decimal>(sqlComisionesMes, new { InicioMes = inicioMes });

            // Por proveedor (top 10)
            const string sqlPorProveedor = @"
                SELECT TOP 10
                    d.ProveedorId,
                    p.Nombre AS ProveedorNombre,
                    p.PorcentajeComision,
                    COUNT(*) AS Documentos,
                    SUM(d.MontoBruto) AS MontoBruto,
                    SUM(d.MontoComision) AS MontoComision,
                    SUM(d.MontoPendiente) AS MontoPorPagar
                FROM CxpDocumentos d
                INNER JOIN Proveedores p ON d.ProveedorId = p.Id
                WHERE d.Anulado = 0 AND d.Estado != 2
                GROUP BY d.ProveedorId, p.Nombre, p.PorcentajeComision
                ORDER BY SUM(d.MontoPendiente) DESC";

            var porProveedor = await connection.QueryAsync<dynamic>(sqlPorProveedor);

            return Ok(new
            {
                resumen = new
                {
                    TotalDocumentos = (int)totales.TotalDocumentos,
                    TotalBruto = (decimal)totales.TotalBruto,
                    TotalComision = (decimal)totales.TotalComision,
                    TotalPorPagar = (decimal)totales.TotalPorPagar,
                    TotalVencido = (decimal)totales.TotalVencido
                },
                comisionesMes,
                porProveedor
            });
        } */

        #endregion

        #region Proveedores con saldo

        /* /// <summary>
         /// GET /api/cxp/proveedores
         /// </summary>
         [HttpGet("proveedores")]
         public async Task<IActionResult> ListarProveedores()
         {
             using var connection = _connectionFactory.Create();
             var hoy = DateTime.UtcNow.Date;

             const string sql = @"
                 SELECT 
                     d.ProveedorId,
                     p.Nombre AS ProveedorNombre,
                     p.DiasCorte AS DiaCorte,
                     p.PorcentajeComision,
                     COUNT(*) AS DocumentosPendientes,
                     SUM(d.MontoBruto) AS TotalBruto,
                     SUM(d.MontoComision) AS TotalComision,
                     SUM(d.MontoPendiente) AS TotalPorPagar,
                     SUM(CASE WHEN d.FechaVencimiento < @Hoy THEN d.MontoPendiente ELSE 0 END) AS Vencido
                 FROM CxpDocumentos d
                 INNER JOIN Proveedores p ON d.ProveedorId = p.Id
                 WHERE d.Anulado = 0 AND d.Estado != 2
                 GROUP BY d.ProveedorId, p.Nombre, p.DiasCorte, p.PorcentajeComision
                 ORDER BY SUM(d.MontoPendiente) DESC";

             var proveedores = await connection.QueryAsync<dynamic>(sql, new { Hoy = hoy });
             return Ok(proveedores);
         }*/

        /// <summary>
        /// GET /api/cxp/proveedores
        /// </summary>
        [HttpGet("proveedores")]
        public async Task<IActionResult> ListarProveedores()
        {
            using var connection = _connectionFactory.Create();
            var hoy = DateTime.UtcNow.Date;

            const string sql = @"
        SELECT 
            d.ProveedorId,
            ISNULL(p.Nombre, 'Sin nombre') AS ProveedorNombre,
            p.DiasCorte AS DiaCorte,
            ISNULL(p.PorcentajeComision, 0) AS PorcentajeComision,
            COUNT(*) AS DocumentosPendientes,
            ISNULL(SUM(d.MontoBruto), 0) AS TotalBruto,
            ISNULL(SUM(d.MontoComision), 0) AS TotalComision,
            ISNULL(SUM(d.MontoPendiente), 0) AS TotalPorPagar,
            ISNULL(SUM(CASE WHEN d.FechaVencimiento < @Hoy THEN d.MontoPendiente ELSE 0 END), 0) AS Vencido
        FROM CxpDocumentos d
        INNER JOIN Proveedores p ON d.ProveedorId = p.Id
        WHERE d.Anulado = 0 
          AND d.Estado != 2
          AND d.MontoPendiente > 0
        GROUP BY d.ProveedorId, p.Nombre, p.DiasCorte, p.PorcentajeComision
        HAVING SUM(d.MontoPendiente) > 0
        ORDER BY SUM(d.MontoPendiente) DESC";

            var proveedores = await connection.QueryAsync<ProveedorCxpDto>(sql, new { Hoy = hoy });

            return Ok(proveedores);
        }

        // DTO tipado
        public class ProveedorCxpDto
        {
            public int ProveedorId { get; set; }
            public string ProveedorNombre { get; set; } = string.Empty;
            public int? DiaCorte { get; set; }           
            public decimal PorcentajeComision { get; set; } 
            public int DocumentosPendientes { get; set; }   
            public decimal TotalBruto { get; set; }
            public decimal TotalComision { get; set; }
            public decimal TotalPorPagar { get; set; }
            public decimal Vencido { get; set; }
        }

        #endregion

        #region Documentos CxP

        /// <summary>
        /// GET /api/cxp/proveedores/{proveedorId}/documentos
        /// </summary>
        [HttpGet("proveedores/{proveedorId:int}/documentos")]
        public async Task<IActionResult> ListarDocumentos(int proveedorId, [FromQuery] bool? soloActivos)
        {
            using var connection = _connectionFactory.Create();

            // Verificar proveedor
            var proveedor = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT Id, Nombre, Rnc, DiasCorte, PorcentajeComision FROM Proveedores WHERE Id = @ProveedorId",
                new { ProveedorId = proveedorId });

            if (proveedor == null)
                return NotFound(new { message = "Proveedor no encontrado." });

            var whereClause = "WHERE d.ProveedorId = @ProveedorId AND d.Anulado = 0";
            if (soloActivos == true)
                whereClause += " AND d.Estado != 2";

            var sql = $@"
                SELECT 
                    d.Id,
                    d.NumeroDocumento,
                    d.NumeroFacturaProveedor,
                    d.PeriodoDesde,
                    d.PeriodoHasta,
                    d.CantidadConsumos,
                    d.MontoBruto,
                    d.MontoComision,
                    d.MontoTotal,
                    d.MontoPagado,
                    d.MontoPendiente,
                    d.Estado,
                    d.FechaEmision,
                    d.FechaVencimiento,
                    CASE WHEN d.FechaVencimiento < GETUTCDATE() 
                         THEN DATEDIFF(DAY, d.FechaVencimiento, GETUTCDATE()) 
                         ELSE 0 END AS DiasVencido
                FROM CxpDocumentos d
                {whereClause}
                ORDER BY d.FechaEmision DESC";

            var rawData = await connection.QueryAsync<dynamic>(sql, new { ProveedorId = proveedorId });

            var documentos = rawData.Select(d => new
            {
                d.Id,
                d.NumeroDocumento,
                d.NumeroFacturaProveedor,
                d.PeriodoDesde,
                d.PeriodoHasta,
                d.CantidadConsumos,
                d.MontoBruto,
                d.MontoComision,
                d.MontoTotal,
                d.MontoPagado,
                d.MontoPendiente,
                Estado = (int)d.Estado,
                EstadoNombre = ((EstadoCxp)(int)d.Estado).ToString(),
                d.FechaEmision,
                d.FechaVencimiento,
                d.DiasVencido
            }).ToList();

            return Ok(new
            {
                proveedor = new
                {
                    Id = (int)proveedor.Id,
                    Nombre = (string)proveedor.Nombre,
                    Rnc = (string?)proveedor.Rnc,
                    DiaCorte = (int?)proveedor.DiasCorte,
                    PorcentajeComision = (decimal)proveedor.PorcentajeComision
                },
                documentos,
                resumen = new
                {
                    TotalBruto = documentos.Sum(d => d.MontoBruto),
                    TotalComision = documentos.Sum(d => d.MontoComision),
                    TotalNeto = documentos.Sum(d => d.MontoTotal),
                    Pagado = documentos.Sum(d => d.MontoPagado),
                    Pendiente = documentos.Sum(d => d.MontoPendiente)
                }
            });
        }

        /// <summary>
        /// GET /api/cxp/documentos/{id}
        /// </summary>
        [HttpGet("documentos/{id:int}")]
        public async Task<IActionResult> ObtenerDocumento(int id)
        {
            using var connection = _connectionFactory.Create();

            // Obtener documento con proveedor
            const string sqlDoc = @"
                SELECT 
                    d.Id, d.NumeroDocumento, d.NumeroFacturaProveedor, d.ProveedorId,
                    d.PeriodoDesde, d.PeriodoHasta, d.CantidadConsumos,
                    d.MontoBruto, d.MontoComision, d.MontoTotal,
                    d.MontoPagado, d.MontoPendiente, d.Estado,
                    d.FechaEmision, d.FechaVencimiento, d.Concepto, d.Notas,
                    p.Id AS ProvId, p.Nombre AS ProvNombre, p.Rnc AS ProvRnc, p.PorcentajeComision AS ProvComision
                FROM CxpDocumentos d
                INNER JOIN Proveedores p ON d.ProveedorId = p.Id
                WHERE d.Id = @Id";

            var doc = await connection.QueryFirstOrDefaultAsync<dynamic>(sqlDoc, new { Id = id });

            if (doc == null)
                return NotFound(new { message = "Documento no encontrado." });

            // Obtener detalles
            const string sqlDetalles = @"
                SELECT 
                    det.Id, det.ConsumoId,
                    c.Fecha,
                    ISNULL(cli.Nombre, 'N/A') AS ClienteNombre,
                    ISNULL(e.Nombre, 'N/A') AS EmpresaNombre,
                    ISNULL(c.Concepto, '') AS Concepto,
                    det.MontoBruto,
                    det.MontoComision,
                    det.MontoNeto
                FROM CxpDocumentoDetalles det
                INNER JOIN Consumos c ON det.ConsumoId = c.Id
                LEFT JOIN Clientes cli ON c.ClienteId = cli.Id
                LEFT JOIN Empresas e ON cli.EmpresaId = e.Id
                WHERE det.CxpDocumentoId = @Id
                ORDER BY c.Fecha DESC";

            var detalles = await connection.QueryAsync<dynamic>(sqlDetalles, new { Id = id });

            // Obtener pagos
            const string sqlPagos = @"
                SELECT 
                    p.Id, p.NumeroComprobante, p.Fecha, p.Monto,
                    p.MetodoPago, p.Referencia
                FROM CxpPagos p
                WHERE p.CxpDocumentoId = @Id AND p.Anulado = 0
                ORDER BY p.Fecha DESC";

            var pagosRaw = await connection.QueryAsync<dynamic>(sqlPagos, new { Id = id });
            var pagos = pagosRaw.Select(p => new
            {
                p.Id,
                p.NumeroComprobante,
                p.Fecha,
                p.Monto,
                MetodoPago = (int)p.MetodoPago,
                MetodoPagoNombre = ((MetodoPago)(int)p.MetodoPago).ToString(),
                p.Referencia
            });

            decimal porcentajePromedio = (decimal)doc.MontoBruto > 0
                ? ((decimal)doc.MontoComision / (decimal)doc.MontoBruto * 100)
                : 0;

            return Ok(new
            {
                doc.Id,
                doc.NumeroDocumento,
                doc.NumeroFacturaProveedor,
                Proveedor = new
                {
                    Id = (int)doc.ProvId,
                    Nombre = (string)doc.ProvNombre,
                    Rnc = (string?)doc.ProvRnc,
                    PorcentajeComision = (decimal)doc.ProvComision
                },
                doc.PeriodoDesde,
                doc.PeriodoHasta,
                doc.CantidadConsumos,
                doc.MontoBruto,
                doc.MontoComision,
                PorcentajeComision = porcentajePromedio,
                doc.MontoTotal,
                doc.MontoPagado,
                doc.MontoPendiente,
                Estado = (int)doc.Estado,
                EstadoNombre = ((EstadoCxp)(int)doc.Estado).ToString(),
                doc.FechaEmision,
                doc.FechaVencimiento,
                doc.Concepto,
                doc.Notas,
                Detalles = detalles,
                Pagos = pagos
            });
        }

        #endregion

        #region Generar Consolidado

        /// <summary>
        /// GET /api/cxp/proveedores/{proveedorId}/preview-consolidado
        /// </summary>
        [HttpGet("proveedores/{proveedorId:int}/preview-consolidado")]
        public async Task<IActionResult> PreviewConsolidado(int proveedorId, [FromQuery] DateTime desde, [FromQuery] DateTime hasta)
        {
            using var connection = _connectionFactory.Create();

            var proveedor = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT Id, Nombre, PorcentajeComision FROM Proveedores WHERE Id = @ProveedorId",
                new { ProveedorId = proveedorId });

            if (proveedor == null)
                return BadRequest(new { message = "Proveedor no encontrado." });

            const string sql = @"
                SELECT 
                    c.Id, c.Fecha,
                    ISNULL(cli.Nombre, 'N/A') AS ClienteNombre,
                    ISNULL(e.Nombre, 'N/A') AS EmpresaNombre,
                    c.Monto,
                    c.MontoComision,
                    c.MontoNetoProveedor
                FROM Consumos c
                LEFT JOIN Clientes cli ON c.ClienteId = cli.Id
                LEFT JOIN Empresas e ON cli.EmpresaId = e.Id
                WHERE c.ProveedorId = @ProveedorId
                  AND c.Reversado = 0
                  AND c.Fecha >= @Desde
                  AND c.Fecha < @Hasta
                  AND NOT EXISTS (SELECT 1 FROM CxpDocumentoDetalles d WHERE d.ConsumoId = c.Id)
                ORDER BY c.Fecha DESC";

            var consumos = (await connection.QueryAsync<dynamic>(sql, new
            {
                ProveedorId = proveedorId,
                Desde = desde,
                Hasta = hasta.AddDays(1)
            })).ToList();

            if (!consumos.Any())
                return BadRequest(new { message = "No hay consumos para consolidar en este período." });

            decimal porcentajeComision = proveedor.PorcentajeComision;
            decimal montoBruto = consumos.Sum(c => (decimal)c.Monto);
            decimal montoComision = 0;
            decimal montoNeto = 0;

            // Verificar si los consumos ya tienen comisión calculada
            if (consumos.Any(c => c.MontoComision != null && (decimal)c.MontoComision > 0))
            {
                montoComision = consumos.Sum(c => (decimal)(c.MontoComision ?? 0m));
                montoNeto = consumos.Sum(c => (decimal)(c.MontoNetoProveedor ?? 0m));
            }
            else
            {
                montoComision = montoBruto * porcentajeComision / 100;
                montoNeto = montoBruto - montoComision;
            }

            var detalles = consumos.Select(c =>
            {
                decimal comision = c.MontoComision != null && (decimal)c.MontoComision > 0
                    ? (decimal)c.MontoComision
                    : ((decimal)c.Monto * porcentajeComision / 100);
                decimal neto = c.MontoNetoProveedor != null && (decimal)c.MontoNetoProveedor > 0
                    ? (decimal)c.MontoNetoProveedor
                    : ((decimal)c.Monto - comision);

                return new
                {
                    c.Id,
                    c.Fecha,
                    c.ClienteNombre,
                    c.EmpresaNombre,
                    MontoBruto = (decimal)c.Monto,
                    MontoComision = comision,
                    MontoNeto = neto
                };
            }).ToList();

            return Ok(new
            {
                Proveedor = new { Id = (int)proveedor.Id, Nombre = (string)proveedor.Nombre, PorcentajeComision = porcentajeComision },
                PeriodoDesde = desde,
                PeriodoHasta = hasta,
                CantidadConsumos = consumos.Count,
                MontoBruto = montoBruto,
                MontoComision = montoComision,
                MontoNeto = montoNeto,
                Consumos = detalles
            });
        }

        /// <summary>
        /// POST /api/cxp/proveedores/{proveedorId}/consolidar
        /// </summary>
        [HttpPost("proveedores/{proveedorId:int}/consolidar")]
        public async Task<IActionResult> GenerarConsolidado(int proveedorId, [FromBody] GenerarConsolidadoCxpDto dto)
        {
            using var connection = _connectionFactory.Create();
            await connection.OpenAsync();

            var proveedor = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT Id, Nombre, PorcentajeComision FROM Proveedores WHERE Id = @ProveedorId",
                new { ProveedorId = proveedorId });

            if (proveedor == null)
                return BadRequest(new { message = "Proveedor no encontrado." });

            // Verificar si ya existe consolidado
            var existente = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM CxpDocumentos 
                WHERE ProveedorId = @ProveedorId 
                  AND PeriodoDesde = @PeriodoDesde 
                  AND PeriodoHasta = @PeriodoHasta 
                  AND Anulado = 0",
                new { ProveedorId = proveedorId, dto.PeriodoDesde, dto.PeriodoHasta }) > 0;

            if (existente)
                return BadRequest(new { message = "Ya existe un consolidado para este período." });

            // Obtener consumos pendientes
            const string sqlConsumos = @"
                SELECT c.Id, c.Monto, c.MontoComision, c.MontoNetoProveedor
                FROM Consumos c
                WHERE c.ProveedorId = @ProveedorId
                  AND c.Reversado = 0
                  AND c.Fecha >= @PeriodoDesde
                  AND c.Fecha < @PeriodoHasta
                  AND NOT EXISTS (SELECT 1 FROM CxpDocumentoDetalles d WHERE d.ConsumoId = c.Id)";

            var consumos = (await connection.QueryAsync<dynamic>(sqlConsumos, new
            {
                ProveedorId = proveedorId,
                dto.PeriodoDesde,
                PeriodoHasta = dto.PeriodoHasta.AddDays(1)
            })).ToList();

            if (!consumos.Any())
                return BadRequest(new { message = "No hay consumos para consolidar." });

            decimal porcentajeComision = proveedor.PorcentajeComision;
            decimal montoBruto = consumos.Sum(c => (decimal)c.Monto);
            decimal montoComision = consumos.Sum(c => (decimal)(c.MontoComision ?? 0m));
            decimal montoNeto = consumos.Sum(c => (decimal)(c.MontoNetoProveedor ?? 0m));

            if (montoComision == 0 && porcentajeComision > 0)
            {
                montoComision = montoBruto * porcentajeComision / 100;
                montoNeto = montoBruto - montoComision;
            }

            using var transaction = connection.BeginTransaction();

            try
            {
                // Generar número de documento
                var año = DateTime.Now.Year;
                var ultimoDoc = await connection.ExecuteScalarAsync<string?>(@"
                    SELECT TOP 1 NumeroDocumento FROM CxpDocumentos 
                    WHERE NumeroDocumento LIKE @Pattern ORDER BY Id DESC",
                    new { Pattern = $"CXP-{año}-%" }, transaction);

                int secuencial = 1;
                if (ultimoDoc != null)
                {
                    var partes = ultimoDoc.Split('-');
                    if (partes.Length == 3 && int.TryParse(partes[2], out int num))
                        secuencial = num + 1;
                }
                var numeroDocumento = $"CXP-{año}-{secuencial:00000}";

                // Crear documento
                const string sqlInsertDoc = @"
                    INSERT INTO CxpDocumentos 
                        (NumeroDocumento, NumeroFacturaProveedor, ProveedorId,
                         PeriodoDesde, PeriodoHasta, MontoBruto, MontoComision, MontoTotal, MontoPendiente,
                         CantidadConsumos, FechaEmision, FechaVencimiento, Estado,
                         Concepto, Notas, CreadoPorUsuarioId, CreadoUtc, Anulado)
                    OUTPUT INSERTED.Id
                    VALUES 
                        (@NumeroDocumento, @NumeroFacturaProveedor, @ProveedorId,
                         @PeriodoDesde, @PeriodoHasta, @MontoBruto, @MontoComision, @MontoTotal, @MontoPendiente,
                         @CantidadConsumos, @FechaEmision, @FechaVencimiento, @Estado,
                         @Concepto, @Notas, @UsuarioId, @CreadoUtc, 0)";

                var documentoId = await connection.ExecuteScalarAsync<int>(sqlInsertDoc, new
                {
                    NumeroDocumento = numeroDocumento,
                    dto.NumeroFacturaProveedor,
                    ProveedorId = proveedorId,
                    dto.PeriodoDesde,
                    dto.PeriodoHasta,
                    MontoBruto = montoBruto,
                    MontoComision = montoComision,
                    MontoTotal = montoNeto,
                    MontoPendiente = montoNeto,
                    CantidadConsumos = consumos.Count,
                    FechaEmision = DateTime.UtcNow,
                    FechaVencimiento = DateTime.UtcNow.AddDays(dto.DiasParaPagar ?? 30),
                    Estado = (int)EstadoCxp.Pendiente,
                    Concepto = dto.Concepto ?? $"Consolidado del {dto.PeriodoDesde:dd/MM/yyyy} al {dto.PeriodoHasta:dd/MM/yyyy}",
                    dto.Notas,
                    UsuarioId = _user.Id,
                    CreadoUtc = DateTime.UtcNow
                }, transaction);

                // Insertar detalles
                const string sqlInsertDetalle = @"
                    INSERT INTO CxpDocumentoDetalles (CxpDocumentoId, ConsumoId, MontoBruto, MontoComision, MontoNeto)
                    VALUES (@DocumentoId, @ConsumoId, @MontoBruto, @MontoComision, @MontoNeto)";

                var detalles = consumos.Select(c =>
                {
                    decimal comisionConsumo = (decimal)(c.MontoComision ?? 0m) > 0
                        ? (decimal)c.MontoComision
                        : ((decimal)c.Monto * porcentajeComision / 100);
                    decimal netoConsumo = (decimal)(c.MontoNetoProveedor ?? 0m) > 0
                        ? (decimal)c.MontoNetoProveedor
                        : ((decimal)c.Monto - comisionConsumo);

                    return new
                    {
                        DocumentoId = documentoId,
                        ConsumoId = (int)c.Id,
                        MontoBruto = (decimal)c.Monto,
                        MontoComision = comisionConsumo,
                        MontoNeto = netoConsumo
                    };
                });

                await connection.ExecuteAsync(sqlInsertDetalle, detalles, transaction);

                transaction.Commit();

                return Ok(new
                {
                    Id = documentoId,
                    NumeroDocumento = numeroDocumento,
                    MontoBruto = montoBruto,
                    MontoComision = montoComision,
                    MontoTotal = montoNeto,
                    CantidadConsumos = consumos.Count,
                    FechaVencimiento = DateTime.UtcNow.AddDays(dto.DiasParaPagar ?? 30),
                    mensaje = $"Consolidado CxP generado. Tu comisión: RD${montoComision:N2}"
                });
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        #endregion

        #region Registrar Pago

        /// <summary>
        /// POST /api/cxp/documentos/{id}/pagos
        /// </summary>
        [HttpPost("documentos/{id:int}/pagos")]
        public async Task<IActionResult> RegistrarPago(int id, [FromBody] RegistrarPagoCxpDto dto)
        {
            using var connection = _connectionFactory.Create();
            await connection.OpenAsync();

            // Obtener documento
            const string sqlDoc = @"
                SELECT Id, ProveedorId, MontoTotal, MontoPagado, MontoPendiente, Estado, Anulado
                FROM CxpDocumentos WHERE Id = @Id";

            var doc = await connection.QueryFirstOrDefaultAsync<dynamic>(sqlDoc, new { Id = id });

            if (doc == null || (bool)doc.Anulado)
                return NotFound(new { message = "Documento no encontrado." });

            if ((int)doc.Estado == (int)EstadoCxp.Pagado)
                return BadRequest(new { message = "Este documento ya está completamente pagado." });

            if (dto.Monto <= 0)
                return BadRequest(new { message = "El monto debe ser mayor a cero." });

            decimal montoPendiente = doc.MontoPendiente;
            if (dto.Monto > montoPendiente)
                return BadRequest(new { message = $"El monto excede el saldo pendiente ({montoPendiente:N2})." });

            using var transaction = connection.BeginTransaction();

            try
            {
                // Generar número de comprobante
                var año = DateTime.Now.Year;
                var ultimoPago = await connection.ExecuteScalarAsync<string?>(@"
                    SELECT TOP 1 NumeroComprobante FROM CxpPagos 
                    WHERE NumeroComprobante LIKE @Pattern ORDER BY Id DESC",
                    new { Pattern = $"PAG-{año}-%" }, transaction);

                int secuencial = 1;
                if (ultimoPago != null)
                {
                    var partes = ultimoPago.Split('-');
                    if (partes.Length == 3 && int.TryParse(partes[2], out int num))
                        secuencial = num + 1;
                }
                var numeroComprobante = $"PAG-{año}-{secuencial:00000}";

                // Insertar pago
                const string sqlInsertPago = @"
                    INSERT INTO CxpPagos 
                        (CxpDocumentoId, NumeroComprobante, Fecha, Monto, MetodoPago, 
                         Referencia, BancoOrigen, CuentaDestino, Notas, Anulado, RegistradoPorUsuarioId, CreadoUtc)
                    OUTPUT INSERTED.Id
                    VALUES 
                        (@DocumentoId, @NumeroComprobante, @Fecha, @Monto, @MetodoPago,
                         @Referencia, @BancoOrigen, @CuentaDestino, @Notas, 0, @UsuarioId, @CreadoUtc)";

                var pagoId = await connection.ExecuteScalarAsync<int>(sqlInsertPago, new
                {
                    DocumentoId = id,
                    NumeroComprobante = numeroComprobante,
                    Fecha = DateTime.UtcNow,
                    dto.Monto,
                    MetodoPago = (int)dto.MetodoPago,
                    dto.Referencia,
                    dto.BancoOrigen,
                    dto.CuentaDestino,
                    dto.Notas,
                    UsuarioId = _user.Id,
                    CreadoUtc = DateTime.UtcNow
                }, transaction);

                // Actualizar documento
                decimal nuevoMontoPagado = (decimal)doc.MontoPagado + dto.Monto;
                decimal nuevoMontoPendiente = montoPendiente - dto.Monto;

                EstadoCxp nuevoEstado = nuevoMontoPendiente <= 0
                    ? EstadoCxp.Pagado
                    : EstadoCxp.ParcialmentePagado;

                if (nuevoMontoPendiente < 0) nuevoMontoPendiente = 0;

                const string sqlUpdateDoc = @"
                    UPDATE CxpDocumentos 
                    SET MontoPagado = @MontoPagado, MontoPendiente = @MontoPendiente, Estado = @Estado
                    WHERE Id = @Id";

                await connection.ExecuteAsync(sqlUpdateDoc, new
                {
                    MontoPagado = nuevoMontoPagado,
                    MontoPendiente = nuevoMontoPendiente,
                    Estado = (int)nuevoEstado,
                    Id = id
                }, transaction);

                transaction.Commit();

                return Ok(new
                {
                    Id = pagoId,
                    NumeroComprobante = numeroComprobante,
                    Monto = dto.Monto,
                    DocumentoNuevoSaldo = nuevoMontoPendiente,
                    DocumentoEstado = nuevoEstado.ToString(),
                    mensaje = "Pago registrado exitosamente."
                });
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        #endregion

        #region Reporte de Comisiones

        /// <summary>
        /// GET /api/cxp/reporte-comisiones
        /// </summary>
        [HttpGet("reporte-comisiones")]
        public async Task<IActionResult> ReporteComisiones(
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta,
            [FromQuery] int? proveedorId)
        {
            using var connection = _connectionFactory.Create();

            var fechaDesde = desde ?? DateTime.UtcNow.AddMonths(-1);
            var fechaHasta = hasta ?? DateTime.UtcNow;

            var whereClause = "WHERE c.Reversado = 0 AND c.Fecha >= @FechaDesde AND c.Fecha < @FechaHasta";
            var parameters = new DynamicParameters();
            parameters.Add("FechaDesde", fechaDesde);
            parameters.Add("FechaHasta", fechaHasta.AddDays(1));

            if (proveedorId.HasValue)
            {
                whereClause += " AND c.ProveedorId = @ProveedorId";
                parameters.Add("ProveedorId", proveedorId.Value);
            }

            // Por proveedor
            var sqlPorProveedor = $@"
                SELECT 
                    c.ProveedorId,
                    p.Nombre AS ProveedorNombre,
                    COUNT(*) AS CantidadConsumos,
                    SUM(c.Monto) AS MontoBruto,
                    SUM(c.MontoComision) AS MontoComision,
                    SUM(c.MontoNetoProveedor) AS MontoNeto,
                    AVG(c.PorcentajeComision) AS PorcentajePromedio
                FROM Consumos c
                INNER JOIN Proveedores p ON c.ProveedorId = p.Id
                {whereClause}
                GROUP BY c.ProveedorId, p.Nombre
                ORDER BY SUM(c.MontoComision) DESC";

            var porProveedor = await connection.QueryAsync<dynamic>(sqlPorProveedor, parameters);

            // Por día
            var sqlPorDia = $@"
                SELECT 
                    CAST(c.Fecha AS DATE) AS Fecha,
                    COUNT(*) AS CantidadConsumos,
                    SUM(c.Monto) AS MontoBruto,
                    SUM(c.MontoComision) AS MontoComision
                FROM Consumos c
                {whereClause}
                GROUP BY CAST(c.Fecha AS DATE)
                ORDER BY CAST(c.Fecha AS DATE)";

            var porDia = await connection.QueryAsync<dynamic>(sqlPorDia, parameters);

            // Totales
            var sqlTotales = $@"
                SELECT 
                    COUNT(*) AS TotalConsumos,
                    ISNULL(SUM(c.Monto), 0) AS TotalBruto,
                    ISNULL(SUM(c.MontoComision), 0) AS TotalComision,
                    ISNULL(SUM(c.MontoNetoProveedor), 0) AS TotalNeto
                FROM Consumos c
                {whereClause}";

            var totales = await connection.QueryFirstAsync<dynamic>(sqlTotales, parameters);

            return Ok(new
            {
                periodo = new { desde = fechaDesde, hasta = fechaHasta },
                totales = new
                {
                    TotalConsumos = (int)totales.TotalConsumos,
                    TotalBruto = (decimal)totales.TotalBruto,
                    TotalComision = (decimal)totales.TotalComision,
                    TotalNeto = (decimal)totales.TotalNeto
                },
                porProveedor,
                porDia
            });
        }

        #endregion

        #region Historial de Pagos

        /// <summary>
        /// GET /api/cxp/pagos
        /// </summary>
        [HttpGet("pagos")]
        public async Task<IActionResult> HistorialPagos(
            [FromQuery] int? proveedorId,
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            using var connection = _connectionFactory.Create();

            var whereClause = "WHERE p.Anulado = 0";
            var parameters = new DynamicParameters();

            if (proveedorId.HasValue)
            {
                whereClause += " AND d.ProveedorId = @ProveedorId";
                parameters.Add("ProveedorId", proveedorId.Value);
            }

            if (desde.HasValue)
            {
                whereClause += " AND p.Fecha >= @Desde";
                parameters.Add("Desde", desde.Value);
            }

            if (hasta.HasValue)
            {
                whereClause += " AND p.Fecha < @Hasta";
                parameters.Add("Hasta", hasta.Value.AddDays(1));
            }

            // Contar y sumar
            var resumenSql = $@"
                SELECT COUNT(*) AS Total, ISNULL(SUM(p.Monto), 0) AS TotalMonto
                FROM CxpPagos p
                INNER JOIN CxpDocumentos d ON p.CxpDocumentoId = d.Id
                {whereClause}";

            var resumen = await connection.QueryFirstAsync<dynamic>(resumenSql, parameters);
            int total = resumen.Total;
            decimal totalMonto = resumen.TotalMonto;

            // Obtener datos paginados
            parameters.Add("Offset", (page - 1) * pageSize);
            parameters.Add("PageSize", pageSize);

            var dataSql = $@"
                SELECT 
                    p.Id, p.NumeroComprobante, p.Fecha, p.Monto,
                    p.MetodoPago, p.Referencia,
                    d.Id AS DocumentoId, d.NumeroDocumento AS DocumentoNumero,
                    prov.Nombre AS ProveedorNombre
                FROM CxpPagos p
                INNER JOIN CxpDocumentos d ON p.CxpDocumentoId = d.Id
                INNER JOIN Proveedores prov ON d.ProveedorId = prov.Id
                {whereClause}
                ORDER BY p.Fecha DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var rawData = await connection.QueryAsync<dynamic>(dataSql, parameters);

            var pagos = rawData.Select(p => new
            {
                p.Id,
                p.NumeroComprobante,
                p.Fecha,
                p.Monto,
                MetodoPago = (int)p.MetodoPago,
                MetodoPagoNombre = ((MetodoPago)(int)p.MetodoPago).ToString(),
                p.Referencia,
                p.DocumentoId,
                p.DocumentoNumero,
                p.ProveedorNombre
            });

            return Ok(new
            {
                data = pagos,
                totalMonto,
                pagination = new { total, page, pageSize, totalPages = (int)Math.Ceiling(total / (double)pageSize) }
            });
        }

        #endregion
    }

    #region DTOs

    public class GenerarConsolidadoCxpDto
    {
        public DateTime PeriodoDesde { get; set; }
        public DateTime PeriodoHasta { get; set; }
        public string? NumeroFacturaProveedor { get; set; }
        public string? Concepto { get; set; }
        public string? Notas { get; set; }
        public int? DiasParaPagar { get; set; }
    }

    public class RegistrarPagoCxpDto
    {
        public decimal Monto { get; set; }
        public MetodoPago MetodoPago { get; set; }
        public string? Referencia { get; set; }
        public string? BancoOrigen { get; set; }
        public string? CuentaDestino { get; set; }
        public string? Notas { get; set; }
    }

    public class DashboardTotalesDto
    {
        public int TotalDocumentos { get; set; }
        public decimal TotalBruto { get; set; }
        public decimal TotalComision { get; set; }
        public decimal TotalPorPagar { get; set; }
        public decimal TotalVencido { get; set; }
    }

    public class DashboardProveedorDto
    {
        public int ProveedorId { get; set; }
        public string ProveedorNombre { get; set; } = string.Empty;
        public decimal PorcentajeComision { get; set; }
        public int Documentos { get; set; }
        public decimal MontoBruto { get; set; }
        public decimal MontoComision { get; set; }
        public decimal MontoPorPagar { get; set; }
    }

    #endregion
}