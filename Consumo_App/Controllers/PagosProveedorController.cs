using Dapper;
using Consumo_App.Data.Sql;
using Consumo_App.Models.Pagos;
using Consumo_App.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/pagos-proveedor")]
    [Authorize]
    public class PagosProveedorController : ControllerBase
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly IUserContext _user;

        public PagosProveedorController(SqlConnectionFactory connectionFactory, IUserContext user)
        {
            _connectionFactory = connectionFactory;
            _user = user;
        }

        #region Dashboard CxP

        /// <summary>
        /// GET /api/pagos-proveedor/dashboard
        /// </summary>
        [HttpGet("dashboard")]
        public async Task<IActionResult> Dashboard()
        {
            using var connection = _connectionFactory.Create();

            // Resumen de documentos pendientes
            const string sqlResumen = @"
                SELECT 
                    COUNT(*) AS TotalDocumentos,
                    ISNULL(SUM(MontoBruto), 0) AS MontoBrutoTotal,
                    ISNULL(SUM(MontoComision), 0) AS MontoComisionTotal,
                    ISNULL(SUM(MontoPendiente), 0) AS MontoPendienteTotal,
                    ISNULL(SUM(CASE WHEN FechaVencimiento < GETUTCDATE() THEN MontoPendiente ELSE 0 END), 0) AS MontoVencido
                FROM CxpDocumentos
                WHERE Anulado = 0 AND Estado != 2"; // 2 = Pagado

            var resumenDocumentos = await connection.QueryFirstAsync<dynamic>(sqlResumen);

            // Por proveedor (top 10)
            const string sqlPorProveedor = @"
                SELECT TOP 10
                    d.ProveedorId,
                    p.Nombre AS ProveedorNombre,
                    COUNT(*) AS Documentos,
                    SUM(d.MontoBruto) AS MontoBruto,
                    SUM(d.MontoComision) AS MontoComision,
                    SUM(d.MontoPendiente) AS MontoPendiente
                FROM CxpDocumentos d
                INNER JOIN Proveedores p ON d.ProveedorId = p.Id
                WHERE d.Anulado = 0 AND d.Estado != 2
                GROUP BY d.ProveedorId, p.Nombre
                ORDER BY SUM(d.MontoPendiente) DESC";

            var porProveedor = await connection.QueryAsync<dynamic>(sqlPorProveedor);

            // Comisiones del mes
            var inicioMes = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            var comisionesMes = await connection.ExecuteScalarAsync<decimal>(@"
                SELECT ISNULL(SUM(MontoComision), 0) 
                FROM Consumos 
                WHERE Reversado = 0 AND Fecha >= @InicioMes",
                new { InicioMes = inicioMes });

            return Ok(new
            {
                resumen = new
                {
                    TotalDocumentos = (int)resumenDocumentos.TotalDocumentos,
                    MontoBrutoTotal = (decimal)resumenDocumentos.MontoBrutoTotal,
                    MontoComisionTotal = (decimal)resumenDocumentos.MontoComisionTotal,
                    MontoPendienteTotal = (decimal)resumenDocumentos.MontoPendienteTotal,
                    MontoVencido = (decimal)resumenDocumentos.MontoVencido
                },
                porProveedor,
                comisionesMes,
                mensaje = "Dashboard CxP con comisiones"
            });
        }

        #endregion

        #region Documentos CxP

        /// <summary>
        /// GET /api/pagos-proveedor/documentos
        /// </summary>
        [HttpGet("documentos")]
        public async Task<IActionResult> ListarDocumentos(
            [FromQuery] int? proveedorId,
            [FromQuery] EstadoCxp? estado,
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta,
            [FromQuery] bool? soloVencidos,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            using var connection = _connectionFactory.Create();

            var whereClause = "WHERE d.Anulado = 0";
            var parameters = new DynamicParameters();

            if (proveedorId.HasValue)
            {
                whereClause += " AND d.ProveedorId = @ProveedorId";
                parameters.Add("ProveedorId", proveedorId.Value);
            }

            if (estado.HasValue)
            {
                whereClause += " AND d.Estado = @Estado";
                parameters.Add("Estado", (int)estado.Value);
            }

            if (desde.HasValue)
            {
                whereClause += " AND d.FechaEmision >= @Desde";
                parameters.Add("Desde", desde.Value);
            }

            if (hasta.HasValue)
            {
                whereClause += " AND d.FechaEmision <= @Hasta";
                parameters.Add("Hasta", hasta.Value.AddDays(1));
            }

            if (soloVencidos == true)
            {
                whereClause += " AND d.FechaVencimiento < GETUTCDATE() AND d.Estado != 2";
            }

            // Contar total
            var countSql = $"SELECT COUNT(*) FROM CxpDocumentos d {whereClause}";
            var total = await connection.ExecuteScalarAsync<int>(countSql, parameters);

            // Resumen
            var resumenSql = $@"
                SELECT 
                    COUNT(*) AS TotalDocumentos,
                    ISNULL(SUM(d.MontoBruto), 0) AS MontoBrutoTotal,
                    ISNULL(SUM(d.MontoComision), 0) AS MontoComisionTotal,
                    ISNULL(SUM(d.MontoTotal), 0) AS MontoNetoTotal,
                    ISNULL(SUM(d.MontoPagado), 0) AS MontoPagadoTotal,
                    ISNULL(SUM(d.MontoPendiente), 0) AS MontoPendienteTotal
                FROM CxpDocumentos d
                {whereClause}";
            var resumen = await connection.QueryFirstOrDefaultAsync<dynamic>(resumenSql, parameters);

            // Obtener datos paginados
            parameters.Add("Offset", (page - 1) * pageSize);
            parameters.Add("PageSize", pageSize);

            var dataSql = $@"
                SELECT 
                    d.Id,
                    d.NumeroDocumento,
                    d.NumeroFacturaProveedor,
                    d.ProveedorId,
                    p.Nombre AS ProveedorNombre,
                    p.Rnc AS ProveedorRnc,
                    d.FechaEmision,
                    d.FechaVencimiento,
                    d.PeriodoDesde,
                    d.PeriodoHasta,
                    d.MontoBruto,
                    d.MontoComision,
                    d.MontoTotal,
                    d.MontoPagado,
                    d.MontoPendiente,
                    d.Estado,
                    d.Concepto,
                    CASE WHEN d.FechaVencimiento < GETUTCDATE() 
                         THEN DATEDIFF(DAY, d.FechaVencimiento, GETUTCDATE()) 
                         ELSE 0 END AS DiasVencido
                FROM CxpDocumentos d
                INNER JOIN Proveedores p ON d.ProveedorId = p.Id
                {whereClause}
                ORDER BY d.FechaEmision DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var rawData = await connection.QueryAsync<dynamic>(dataSql, parameters);

            var data = rawData.Select(d => new
            {
                d.Id,
                d.NumeroDocumento,
                d.NumeroFacturaProveedor,
                d.ProveedorId,
                d.ProveedorNombre,
                d.ProveedorRnc,
                d.FechaEmision,
                d.FechaVencimiento,
                d.PeriodoDesde,
                d.PeriodoHasta,
                d.MontoBruto,
                d.MontoComision,
                d.MontoTotal,
                d.MontoPagado,
                d.MontoPendiente,
                Estado = (int)d.Estado,
                EstadoNombre = ((EstadoCxp)(int)d.Estado).ToString(),
                d.Concepto,
                d.DiasVencido
            });

            return Ok(new
            {
                data,
                resumen,
                pagination = new { total, page, pageSize, totalPages = (int)Math.Ceiling(total / (double)pageSize) }
            });
        }

        /// <summary>
        /// GET /api/pagos-proveedor/documentos/{id}
        /// </summary>
        [HttpGet("documentos/{id:int}")]
        public async Task<IActionResult> ObtenerDocumento(int id)
        {
            using var connection = _connectionFactory.Create();

            // Obtener documento con proveedor
            const string sqlDoc = @"
                SELECT 
                    d.Id, d.NumeroDocumento, d.NumeroFacturaProveedor, d.ProveedorId,
                    d.FechaEmision, d.FechaVencimiento, d.PeriodoDesde, d.PeriodoHasta,
                    d.MontoBruto, d.MontoComision, d.MontoTotal, d.MontoPagado, d.MontoPendiente,
                    d.Estado, d.Concepto, d.Notas,
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
                    ISNULL(c.Concepto, '') AS Concepto,
                    det.MontoBruto,
                    det.MontoComision,
                    det.MontoNeto
                FROM CxpDocumentoDetalles det
                INNER JOIN Consumos c ON det.ConsumoId = c.Id
                LEFT JOIN Clientes cli ON c.ClienteId = cli.Id
                WHERE det.CxpDocumentoId = @Id
                ORDER BY c.Fecha DESC";

            var detalles = await connection.QueryAsync<dynamic>(sqlDetalles, new { Id = id });

            // Obtener pagos
            const string sqlPagos = @"
                SELECT 
                    p.Id, p.NumeroComprobante, p.Fecha, p.Monto, p.MetodoPago, p.Referencia
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

            return Ok(new
            {
                doc.Id,
                doc.NumeroDocumento,
                doc.NumeroFacturaProveedor,
                doc.ProveedorId,
                Proveedor = new
                {
                    Id = (int)doc.ProvId,
                    Nombre = (string)doc.ProvNombre,
                    Rnc = (string?)doc.ProvRnc,
                    PorcentajeComision = (decimal)doc.ProvComision
                },
                doc.FechaEmision,
                doc.FechaVencimiento,
                doc.PeriodoDesde,
                doc.PeriodoHasta,
                doc.MontoBruto,
                doc.MontoComision,
                doc.MontoTotal,
                doc.MontoPagado,
                doc.MontoPendiente,
                Estado = (int)doc.Estado,
                EstadoNombre = ((EstadoCxp)(int)doc.Estado).ToString(),
                doc.Concepto,
                doc.Notas,
                Detalles = detalles,
                Pagos = pagos
            });
        }

        /// <summary>
        /// POST /api/pagos-proveedor/consolidar
        /// </summary>
        [HttpPost("consolidar")]
        public async Task<IActionResult> GenerarConsolidado([FromBody] GenerarConsolidadoDto dto)
        {
            using var connection = _connectionFactory.Create();
            await connection.OpenAsync();

            // Verificar proveedor
            var proveedor = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT Id, Nombre, PorcentajeComision FROM Proveedores WHERE Id = @Id",
                new { Id = dto.ProveedorId });

            if (proveedor == null)
                return BadRequest(new { message = "Proveedor no encontrado." });

            // Verificar documento existente
            var existente = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM CxpDocumentos 
                WHERE ProveedorId = @ProveedorId 
                  AND PeriodoDesde = @PeriodoDesde 
                  AND PeriodoHasta = @PeriodoHasta 
                  AND Anulado = 0",
                new { dto.ProveedorId, dto.PeriodoDesde, dto.PeriodoHasta }) > 0;

            if (existente)
                return BadRequest(new { message = "Ya existe un documento para este período." });

            // Obtener consumos no facturados
            const string sqlConsumos = @"
                SELECT c.Id, c.Monto, c.MontoComision, c.MontoNetoProveedor
                FROM Consumos c
                WHERE c.ProveedorId = @ProveedorId
                  AND c.Reversado = 0
                  AND c.Fecha >= @FechaDesde
                  AND c.Fecha < @FechaHasta
                  AND NOT EXISTS (SELECT 1 FROM CxpDocumentoDetalles d WHERE d.ConsumoId = c.Id)";

            var consumos = (await connection.QueryAsync<dynamic>(sqlConsumos, new
            {
                dto.ProveedorId,
                FechaDesde = dto.PeriodoDesde.AddHours(4),
                FechaHasta = dto.PeriodoHasta.AddDays(1).AddHours(4)
            })).ToList();

            if (!consumos.Any())
                return BadRequest(new { message = "No hay consumos para consolidar en este período." });

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

                // Insertar documento
                const string sqlInsertDoc = @"
                    INSERT INTO CxpDocumentos 
                        (ProveedorId, NumeroDocumento, NumeroFacturaProveedor, FechaEmision, FechaVencimiento,
                         PeriodoDesde, PeriodoHasta, MontoBruto, MontoComision, MontoTotal, MontoPendiente,
                         CantidadConsumos, Estado, Concepto, Notas, CreadoPorUsuarioId, CreadoUtc, Anulado)
                    OUTPUT INSERTED.Id
                    VALUES 
                        (@ProveedorId, @NumeroDocumento, @NumeroFacturaProveedor, @FechaEmision, @FechaVencimiento,
                         @PeriodoDesde, @PeriodoHasta, @MontoBruto, @MontoComision, @MontoTotal, @MontoPendiente,
                         @CantidadConsumos, @Estado, @Concepto, @Notas, @UsuarioId, @CreadoUtc, 0)";

                var documentoId = await connection.ExecuteScalarAsync<int>(sqlInsertDoc, new
                {
                    dto.ProveedorId,
                    NumeroDocumento = numeroDocumento,
                    dto.NumeroFacturaProveedor,
                    FechaEmision = DateTime.UtcNow,
                    FechaVencimiento = DateTime.UtcNow.AddDays(dto.DiasParaPagar ?? 30),
                    dto.PeriodoDesde,
                    dto.PeriodoHasta,
                    MontoBruto = montoBruto,
                    MontoComision = montoComision,
                    MontoTotal = montoNeto,
                    MontoPendiente = montoNeto,
                    CantidadConsumos = consumos.Count,
                    Estado = (int)EstadoCxp.Pendiente,
                    Concepto = dto.Concepto ?? $"Consolidado de consumos del {dto.PeriodoDesde:dd/MM/yyyy} al {dto.PeriodoHasta:dd/MM/yyyy}",
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
                    decimal comisionConsumo = (decimal)(c.MontoComision ?? 0m);
                    decimal netoConsumo = (decimal)(c.MontoNetoProveedor ?? 0m);

                    if (comisionConsumo == 0 && porcentajeComision > 0)
                    {
                        comisionConsumo = (decimal)c.Monto * porcentajeComision / 100;
                        netoConsumo = (decimal)c.Monto - comisionConsumo;
                    }

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
                    MontoNeto = montoNeto,
                    PorcentajeComisionPromedio = montoBruto > 0 ? (montoComision / montoBruto * 100) : 0,
                    FechaVencimiento = DateTime.UtcNow.AddDays(dto.DiasParaPagar ?? 30),
                    CantidadConsumos = consumos.Count,
                    mensaje = $"Documento CxP generado. Comisión ganada: RD${montoComision:N2}"
                });
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        #endregion

        #region Pagos

        /// <summary>
        /// POST /api/pagos-proveedor/documentos/{id}/pagos
        /// </summary>
        [HttpPost("documentos/{id:int}/pagos")]
        public async Task<IActionResult> RegistrarPago(int id, [FromBody] RegistrarPagoCxpDto dto)
        {
            using var connection = _connectionFactory.Create();
            await connection.OpenAsync();

            // Obtener documento
            var doc = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT Id, MontoPagado, MontoPendiente, Estado, Anulado
                FROM CxpDocumentos WHERE Id = @Id",
                new { Id = id });

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

                await connection.ExecuteAsync(@"
                    UPDATE CxpDocumentos 
                    SET MontoPagado = @MontoPagado, MontoPendiente = @MontoPendiente, Estado = @Estado
                    WHERE Id = @Id",
                    new
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

        #region Reportes

        [HttpGet("reporte-comisiones")]
        public async Task<IActionResult> ReporteComisiones(
    [FromQuery] DateTime desde,
    [FromQuery] DateTime hasta,
    [FromQuery] int? proveedorId)
        {
            using var connection = _connectionFactory.Create();
            var whereClause = "WHERE c.Reversado = 0 AND c.Fecha >= @Desde AND c.Fecha < @Hasta";
            var parameters = new DynamicParameters();
            parameters.Add("Desde", desde);
            parameters.Add("Hasta", hasta.AddDays(1));

            if (proveedorId.HasValue)
            {
                whereClause += " AND c.ProveedorId = @ProveedorId";
                parameters.Add("ProveedorId", proveedorId.Value);
            }

            // Totales
            var sqlTotales = $@"
        SELECT 
            COUNT(*) AS TotalConsumos,
            ISNULL(SUM(c.Monto), 0) AS MontoBruto,
            ISNULL(SUM(c.MontoComision), 0) AS MontoComision,
            ISNULL(SUM(c.MontoNetoProveedor), 0) AS MontoNeto
        FROM Consumos c
        {whereClause}";

            var totales = await connection.QueryFirstAsync<dynamic>(sqlTotales, parameters);

            // Por proveedor
            var sqlPorProveedor = $@"
        SELECT 
            c.ProveedorId,
            p.Nombre AS ProveedorNombre,
            COUNT(*) AS Consumos,
            ISNULL(SUM(c.Monto), 0) AS MontoBruto,
            ISNULL(SUM(c.MontoComision), 0) AS MontoComision,
            ISNULL(SUM(c.MontoNetoProveedor), 0) AS MontoNeto,
            ISNULL(AVG(c.PorcentajeComision), 0) AS PorcentajePromedio
        FROM Consumos c
        INNER JOIN Proveedores p ON c.ProveedorId = p.Id
        {whereClause}
        GROUP BY c.ProveedorId, p.Nombre
        ORDER BY SUM(c.MontoComision) DESC";

            var porProveedorRaw = await connection.QueryAsync<dynamic>(sqlPorProveedor, parameters);

            // Por día
            var sqlPorDia = $@"
        SELECT 
            CAST(c.Fecha AS DATE) AS Fecha,
            COUNT(*) AS Consumos,
            ISNULL(SUM(c.Monto), 0) AS MontoBruto,
            ISNULL(SUM(c.MontoComision), 0) AS MontoComision
        FROM Consumos c
        {whereClause}
        GROUP BY CAST(c.Fecha AS DATE)
        ORDER BY CAST(c.Fecha AS DATE)";

            var porDiaRaw = await connection.QueryAsync<dynamic>(sqlPorDia, parameters);

            // MAPEO EXPLÍCITO PARA GARANTIZAR QUE NO HAYA NULLS
            var porProveedor = porProveedorRaw.Select(p => new
            {
                proveedorId = (int)p.ProveedorId,
                proveedorNombre = (string)p.ProveedorNombre ?? "",
                consumos = (int)p.Consumos,
                montoBruto = (decimal)(p.MontoBruto ?? 0),
                montoComision = (decimal)(p.MontoComision ?? 0),
                montoNeto = (decimal)(p.MontoNeto ?? 0),
                porcentajePromedio = (decimal)(p.PorcentajePromedio ?? 0)
            }).ToList();

            var porDia = porDiaRaw.Select(d => new
            {
                fecha = ((DateTime)d.Fecha).ToString("yyyy-MM-dd"),
                consumos = (int)d.Consumos,
                montoBruto = (decimal)(d.MontoBruto ?? 0),
                montoComision = (decimal)(d.MontoComision ?? 0)
            }).ToList();

            return Ok(new
            {
                periodo = new
                {
                    desde = desde.ToString("yyyy-MM-dd"),
                    hasta = hasta.ToString("yyyy-MM-dd")
                },
                totales = new
                {
                    totalConsumos = (int)totales.TotalConsumos,
                    montoBruto = (decimal)(totales.MontoBruto ?? 0),
                    montoComision = (decimal)(totales.MontoComision ?? 0),
                    montoNeto = (decimal)(totales.MontoNeto ?? 0)
                },
                porProveedor,
                porDia
            });
        }

        /// <summary>
        /// GET /api/pagos-proveedor/antiguedad
        /// </summary>
        [HttpGet("antiguedad")]
        public async Task<IActionResult> ReporteAntiguedad()
        {
            using var connection = _connectionFactory.Create();
            var hoy = DateTime.UtcNow.Date;

            const string sql = @"
                SELECT 
                    d.Id,
                    d.NumeroDocumento,
                    p.Nombre AS ProveedorNombre,
                    d.FechaEmision,
                    d.FechaVencimiento,
                    d.MontoBruto,
                    d.MontoComision,
                    d.MontoPendiente,
                    CASE WHEN d.FechaVencimiento < @Hoy 
                         THEN DATEDIFF(DAY, d.FechaVencimiento, @Hoy) 
                         ELSE 0 END AS DiasVencido,
                    CASE 
                        WHEN d.FechaVencimiento >= @Hoy THEN 'Vigente'
                        WHEN DATEDIFF(DAY, d.FechaVencimiento, @Hoy) <= 30 THEN '1-30 días'
                        WHEN DATEDIFF(DAY, d.FechaVencimiento, @Hoy) <= 60 THEN '31-60 días'
                        WHEN DATEDIFF(DAY, d.FechaVencimiento, @Hoy) <= 90 THEN '61-90 días'
                        ELSE 'Más de 90 días'
                    END AS Rango
                FROM CxpDocumentos d
                INNER JOIN Proveedores p ON d.ProveedorId = p.Id
                WHERE d.Anulado = 0 AND d.Estado != 2
                ORDER BY DiasVencido DESC";

            var documentos = (await connection.QueryAsync<dynamic>(sql, new { Hoy = hoy })).ToList();

            var resumenPorRango = documentos
                .GroupBy(d => (string)d.Rango)
                .Select(g => new
                {
                    Rango = g.Key,
                    Documentos = g.Count(),
                    MontoPendiente = g.Sum(d => (decimal)d.MontoPendiente),
                    MontoComision = g.Sum(d => (decimal)d.MontoComision)
                })
                .ToList();

            return Ok(new
            {
                documentos,
                resumenPorRango,
                totalPendiente = documentos.Sum(d => (decimal)d.MontoPendiente),
                totalComision = documentos.Sum(d => (decimal)d.MontoComision)
            });
        }

        /// <summary>
        /// GET /api/pagos-proveedor/preview-consolidado
        /// </summary>
        [HttpGet("preview-consolidado")]
        public async Task<IActionResult> PreviewConsolidado(
            [FromQuery] int proveedorId,
            [FromQuery] DateTime periodoDesde,
            [FromQuery] DateTime periodoHasta)
        {
            using var connection = _connectionFactory.Create();

            var proveedor = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT Id, Nombre, PorcentajeComision FROM Proveedores WHERE Id = @Id",
                new { Id = proveedorId });

            if (proveedor == null)
                return BadRequest(new { message = "Proveedor no encontrado." });

            const string sql = @"
                SELECT 
                    c.Id, c.Fecha,
                    ISNULL(cli.Nombre, 'N/A') AS ClienteNombre,
                    c.Monto,
                    c.MontoComision,
                    c.MontoNetoProveedor
                FROM Consumos c
                LEFT JOIN Clientes cli ON c.ClienteId = cli.Id
                WHERE c.ProveedorId = @ProveedorId
                  AND c.Reversado = 0
                  AND c.Fecha >= @FechaDesde
                  AND c.Fecha < @FechaHasta
                  AND NOT EXISTS (SELECT 1 FROM CxpDocumentoDetalles d WHERE d.ConsumoId = c.Id)
                ORDER BY c.Fecha DESC";

            var consumos = (await connection.QueryAsync<dynamic>(sql, new
            {
                ProveedorId = proveedorId,
                FechaDesde = periodoDesde.AddHours(4),
                FechaHasta = periodoHasta.AddDays(1).AddHours(4)
            })).ToList();

            if (!consumos.Any())
                return BadRequest(new { message = "No hay consumos para consolidar en este período." });

            decimal porcentajeComision = proveedor.PorcentajeComision;
            decimal montoBruto = consumos.Sum(c => (decimal)c.Monto);
            decimal montoComision = consumos.Sum(c => (decimal)(c.MontoComision ?? 0m));
            decimal montoNeto = consumos.Sum(c => (decimal)(c.MontoNetoProveedor ?? 0m));

            if (montoComision == 0 && porcentajeComision > 0)
            {
                montoComision = montoBruto * porcentajeComision / 100;
                montoNeto = montoBruto - montoComision;
            }

            var detalleConsumos = consumos.Select(c =>
            {
                decimal comision = (decimal)(c.MontoComision ?? 0m);
                decimal neto = (decimal)(c.MontoNetoProveedor ?? 0m);

                if (comision == 0 && porcentajeComision > 0)
                {
                    comision = (decimal)c.Monto * porcentajeComision / 100;
                    neto = (decimal)c.Monto - comision;
                }

                return new
                {
                    c.Id,
                    c.Fecha,
                    c.ClienteNombre,
                    Monto = (decimal)c.Monto,
                    MontoComision = comision,
                    MontoNeto = neto
                };
            }).ToList();

            return Ok(new
            {
                CantidadConsumos = consumos.Count,
                MontoBruto = montoBruto,
                MontoComision = montoComision,
                MontoNeto = montoNeto,
                PorcentajeComision = porcentajeComision,
                Consumos = detalleConsumos
            });
        }

        #endregion
    }

    #region DTOs

    public class GenerarConsolidadoDto
    {
        public int ProveedorId { get; set; }
        public DateTime PeriodoDesde { get; set; }
        public DateTime PeriodoHasta { get; set; }
        public string? NumeroFacturaProveedor { get; set; }
        public string? Concepto { get; set; }
        public string? Notas { get; set; }
        public int? DiasParaPagar { get; set; }
    }

    #endregion
}