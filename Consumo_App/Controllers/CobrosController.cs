using Dapper;
using Consumo_App.Data.Sql;
using Consumo_App.Models;
using Consumo_App.Models.Pagos;
using Consumo_App.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class CobrosController : ControllerBase
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly IUserContext _user;

        public CobrosController(SqlConnectionFactory connectionFactory, IUserContext user)
        {
            _connectionFactory = connectionFactory;
            _user = user;
        }

        #region Documentos CxC

        /// <summary>
        /// Listar documentos CxC con filtros
        /// </summary>
        [HttpGet("documentos")]
        public async Task<IActionResult> ListarDocumentos(
            [FromQuery] int? empresaId,
            [FromQuery] EstadoCxc? estado,
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta,
            [FromQuery] bool? soloVencidos,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            using var connection = _connectionFactory.Create();

            var whereClause = "WHERE 1=1";
            var parameters = new DynamicParameters();

            if (empresaId.HasValue)
            {
                whereClause += " AND d.EmpresaId = @EmpresaId";
                parameters.Add("EmpresaId", empresaId.Value);
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
                whereClause += " AND (d.Estado = 3 OR (d.FechaVencimiento < GETUTCDATE() AND d.Estado NOT IN (2, 5)))";
            }

            // Contar total
            var countSql = $@"
                SELECT COUNT(*) 
                FROM CxcDocumentos d 
                INNER JOIN Empresas e ON d.EmpresaId = e.Id 
                {whereClause}";

            var totalCount = await connection.ExecuteScalarAsync<int>(countSql, parameters);

            // Obtener datos paginados
            parameters.Add("Offset", (page - 1) * pageSize);
            parameters.Add("PageSize", pageSize);

            var dataSql = $@"
                SELECT 
                    d.Id,
                    d.NumeroDocumento,
                    d.EmpresaId,
                    e.Nombre AS EmpresaNombre,
                    e.Rnc AS EmpresaRnc,
                    d.FechaEmision,
                    d.FechaVencimiento,
                    d.PeriodoDesde,
                    d.PeriodoHasta,
                    d.MontoTotal,
                    d.MontoPagado,
                    d.MontoPendiente,
                    d.Estado,
                    d.Refinanciado,
                    CASE 
                        WHEN d.FechaVencimiento < GETUTCDATE() THEN DATEDIFF(DAY, d.FechaVencimiento, GETUTCDATE())
                        ELSE 0
                    END AS DiasVencido
                FROM CxcDocumentos d
                INNER JOIN Empresas e ON d.EmpresaId = e.Id
                {whereClause}
                ORDER BY d.FechaEmision DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var rawData = await connection.QueryAsync<dynamic>(dataSql, parameters);

            var data = rawData.Select(r => new
            {
                r.Id,
                r.NumeroDocumento,
                r.EmpresaId,
                r.EmpresaNombre,
                r.EmpresaRnc,
                r.FechaEmision,
                r.FechaVencimiento,
                r.PeriodoDesde,
                r.PeriodoHasta,
                r.MontoTotal,
                r.MontoPagado,
                r.MontoPendiente,
                Estado = (int)r.Estado,
                EstadoNombre = ((EstadoCxc)(int)r.Estado).ToString(),
                r.Refinanciado,
                r.DiasVencido
            }).ToList();

            // Calcular resumen
            var resumenSql = $@"
                SELECT 
                    ISNULL(SUM(d.MontoTotal), 0) AS MontoTotalFacturado,
                    ISNULL(SUM(d.MontoPagado), 0) AS MontoTotalCobrado,
                    ISNULL(SUM(d.MontoPendiente), 0) AS MontoTotalPendiente
                FROM CxcDocumentos d
                INNER JOIN Empresas e ON d.EmpresaId = e.Id
                {whereClause}";

            // Recrear parámetros sin offset/pagesize para resumen
            var resumenParams = new DynamicParameters();
            if (empresaId.HasValue) resumenParams.Add("EmpresaId", empresaId.Value);
            if (estado.HasValue) resumenParams.Add("Estado", (int)estado.Value);
            if (desde.HasValue) resumenParams.Add("Desde", desde.Value);
            if (hasta.HasValue) resumenParams.Add("Hasta", hasta.Value.AddDays(1));

            var resumen = await connection.QueryFirstAsync<dynamic>(resumenSql, resumenParams);

            return Ok(new
            {
                data,
                resumen = new
                {
                    TotalDocumentos = totalCount,
                    MontoTotalFacturado = (decimal)resumen.MontoTotalFacturado,
                    MontoTotalCobrado = (decimal)resumen.MontoTotalCobrado,
                    MontoTotalPendiente = (decimal)resumen.MontoTotalPendiente
                },
                pagination = new
                {
                    total = totalCount,
                    page,
                    pageSize,
                    totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
                }
            });
        }

        [HttpGet("documentos/{id:int}")]
        public async Task<IActionResult> ObtenerDocumento(int id)
        {
            using var connection = _connectionFactory.Create();

            // Obtener documento con empresa
            const string sqlDoc = @"
                SELECT 
                    d.Id, d.NumeroDocumento, d.EmpresaId,
                    d.PeriodoDesde, d.PeriodoHasta,
                    d.CantidadConsumos, d.CantidadEmpleados,
                    d.MontoTotal, d.MontoPagado, d.MontoPendiente,
                    d.Estado, d.FechaEmision, d.FechaVencimiento,
                    e.Id AS EmpresaId, e.Nombre AS EmpresaNombre, e.Rnc AS EmpresaRnc
                FROM CxcDocumentos d
                INNER JOIN Empresas e ON d.EmpresaId = e.Id
                WHERE d.Id = @Id";

            var doc = await connection.QueryFirstOrDefaultAsync<dynamic>(sqlDoc, new { Id = id });

            if (doc == null)
                return NotFound(new { message = "Documento no encontrado." });

            // Obtener detalles
            const string sqlDetalles = @"
                SELECT 
                    det.Id,
                    det.ConsumoId,
                    c.Fecha,
                    ISNULL(cli.Nombre, 'N/A') AS EmpleadoNombre,
                    ISNULL(cli.Codigo, '') AS EmpleadoCodigo,
                    ISNULL(p.Nombre, 'N/A') AS ProveedorNombre,
                    '' AS TiendaNombre,
                    ISNULL(c.Concepto, '') AS Concepto,
                    det.Monto
                FROM CxcDocumentoDetalles det
                INNER JOIN Consumos c ON det.ConsumoId = c.Id
                LEFT JOIN Clientes cli ON c.ClienteId = cli.Id
                LEFT JOIN Proveedores p ON c.ProveedorId = p.Id
                WHERE det.CxcDocumentoId = @Id
                ORDER BY c.Fecha DESC";

            var detalles = await connection.QueryAsync<dynamic>(sqlDetalles, new { Id = id });

            // Obtener pagos
            const string sqlPagos = @"
                SELECT 
                    p.Id, p.NumeroRecibo AS NumeroComprobante, p.Fecha,
                    p.Monto, p.MetodoPago, p.Referencia
                FROM CxcPagos p
                WHERE p.CxcDocumentoId = @Id AND p.Anulado = 0
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

            // Obtener refinanciamiento activo si existe
            const string sqlRefinanciamiento = @"
                SELECT 
                    r.Id, r.MontoOriginal, r.MontoPagado, r.MontoPendiente,
                    r.Fecha, r.FechaVencimiento, r.Estado
                FROM RefinanciamientoDeudas r
                WHERE r.CxcDocumentoId = @Id 
                  AND r.Estado NOT IN (3, 4)
                ORDER BY r.Id DESC";

            var refinanciamientoRaw = await connection.QueryFirstOrDefaultAsync<dynamic>(sqlRefinanciamiento, new { Id = id });
            object? refinanciamiento = refinanciamientoRaw != null ? new
            {
                refinanciamientoRaw.Id,
                refinanciamientoRaw.MontoOriginal,
                refinanciamientoRaw.MontoPagado,
                refinanciamientoRaw.MontoPendiente,
                refinanciamientoRaw.Fecha,
                refinanciamientoRaw.FechaVencimiento,
                Estado = (int)refinanciamientoRaw.Estado,
                EstadoNombre = ((EstadoRefinanciamiento)(int)refinanciamientoRaw.Estado).ToString()
            } : null;

            return Ok(new
            {
                doc.Id,
                doc.NumeroDocumento,
                Empresa = new
                {
                    Id = (int)doc.EmpresaId,
                    Nombre = (string)doc.EmpresaNombre,
                    Rnc = (string)doc.EmpresaRnc
                },
                doc.PeriodoDesde,
                doc.PeriodoHasta,
                doc.CantidadConsumos,
                doc.CantidadEmpleados,
                doc.MontoTotal,
                doc.MontoPagado,
                doc.MontoPendiente,
                Estado = (int)doc.Estado,
                EstadoNombre = ((EstadoCxc)(int)doc.Estado).ToString(),
                doc.FechaEmision,
                doc.FechaVencimiento,
                Detalles = detalles,
                Pagos = pagos,
                Refinanciamiento = refinanciamiento
            });
        }

        /// <summary>
        /// Generar documento CxC (corte) para una empresa
        /// </summary>
        [HttpPost("documentos/generar")]
        public async Task<IActionResult> GenerarDocumento([FromBody] GenerarCxcDto dto)
        {
            if (dto == null) return BadRequest(new { message = "DTO inválido." });

            using var connection = _connectionFactory.Create();
            await connection.OpenAsync();

            // Verificar empresa
            var empresa = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT Id, Nombre FROM Empresas WHERE Id = @EmpresaId",
                new { dto.EmpresaId });

            if (empresa == null)
                return BadRequest(new { message = "Empresa no encontrada." });

            if (dto.PeriodoHasta <= dto.PeriodoDesde)
                return BadRequest(new { message = "El período es inválido." });

            // Obtener días de gracia
            var config = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT DiasGracia FROM ConfiguracionCortes WHERE EmpresaId = @EmpresaId",
                new { dto.EmpresaId });
            var diasGracia = config?.DiasGracia ?? 5;

            // Zona horaria
            TimeZoneInfo tz;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo"); }
            catch { try { tz = TimeZoneInfo.FindSystemTimeZoneById("SA Western Standard Time"); } catch { tz = TimeZoneInfo.Utc; } }

            var fechaDesdeUtc = TimeZoneInfo.ConvertTimeToUtc(dto.PeriodoDesde, tz);
            var fechaHastaUtc = TimeZoneInfo.ConvertTimeToUtc(dto.PeriodoHasta.AddDays(1), tz);

            using var transaction = connection.BeginTransaction();

            try
            {
                // 1) Verificar documento existente
                var existeDoc = await connection.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(*) FROM CxcDocumentos 
                    WHERE EmpresaId = @EmpresaId 
                      AND PeriodoDesde = @PeriodoDesde 
                      AND PeriodoHasta = @PeriodoHasta 
                      AND Estado != 5",
                    new { dto.EmpresaId, dto.PeriodoDesde, dto.PeriodoHasta }, transaction);

                if (existeDoc > 0)
                    return BadRequest(new { message = "Ya existe un documento para este período." });

                // 2) Obtener consumos
                const string sqlConsumos = @"
                    SELECT c.Id, c.Monto
                    FROM Consumos c
                    INNER JOIN Clientes cli ON c.ClienteId = cli.Id
                    WHERE cli.EmpresaId = @EmpresaId
                      AND c.Reversado = 0
                      AND c.Fecha >= @FechaDesde
                      AND c.Fecha < @FechaHasta
                      AND NOT EXISTS (SELECT 1 FROM CxcDocumentoDetalles d WHERE d.ConsumoId = c.Id)";

                var consumos = (await connection.QueryAsync<(int Id, decimal Monto)>(sqlConsumos, new
                {
                    dto.EmpresaId,
                    FechaDesde = fechaDesdeUtc,
                    FechaHasta = fechaHastaUtc
                }, transaction)).ToList();

                if (!consumos.Any())
                    return BadRequest(new { message = "No hay consumos para facturar en este período." });

                var montoTotal = consumos.Sum(c => c.Monto);

                // 3) Generar número de documento con lock
                var anio = DateTime.UtcNow.Year;
                await connection.ExecuteAsync(
                    "EXEC sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockTimeout = 10000",
                    new { Resource = $"CXC-Num-{anio}" }, transaction);

                var ultimoNumero = await connection.ExecuteScalarAsync<string?>(@"
                    SELECT TOP 1 NumeroDocumento 
                    FROM CxcDocumentos 
                    WHERE NumeroDocumento LIKE @Pattern
                    ORDER BY Id DESC",
                    new { Pattern = $"CXC-{anio}-%" }, transaction);

                int secuencial = 1;
                if (ultimoNumero != null)
                {
                    var partes = ultimoNumero.Split('-');
                    if (partes.Length == 3 && int.TryParse(partes[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int num))
                        secuencial = num + 1;
                }

                var numeroDocumento = $"CXC-{anio}-{secuencial:00000}";
                var fechaVencimiento = DateTime.UtcNow.AddDays(diasGracia);

                // 4) Crear documento
                const string sqlInsertDoc = @"
                    INSERT INTO CxcDocumentos 
                        (EmpresaId, NumeroDocumento, FechaEmision, FechaVencimiento, 
                         PeriodoDesde, PeriodoHasta, MontoTotal, MontoPagado, MontoPendiente, 
                         Estado, Refinanciado, Notas, CreadoUtc, CreadoPorUsuarioId)
                    OUTPUT INSERTED.Id
                    VALUES 
                        (@EmpresaId, @NumeroDocumento, @FechaEmision, @FechaVencimiento,
                         @PeriodoDesde, @PeriodoHasta, @MontoTotal, 0, @MontoPendiente,
                         @Estado, 0, @Notas, @CreadoUtc, @UsuarioId)";

                var documentoId = await connection.ExecuteScalarAsync<int>(sqlInsertDoc, new
                {
                    dto.EmpresaId,
                    NumeroDocumento = numeroDocumento,
                    FechaEmision = DateTime.UtcNow,
                    FechaVencimiento = fechaVencimiento,
                    dto.PeriodoDesde,
                    dto.PeriodoHasta,
                    MontoTotal = montoTotal,
                    MontoPendiente = montoTotal,
                    Estado = (int)EstadoCxc.Pendiente,
                    dto.Notas,
                    CreadoUtc = DateTime.UtcNow,
                    UsuarioId = _user.Id
                }, transaction);

                // 5) Agregar detalles
                const string sqlInsertDetalle = @"
                    INSERT INTO CxcDocumentoDetalles (CxcDocumentoId, ConsumoId, Monto)
                    VALUES (@DocumentoId, @ConsumoId, @Monto)";

                var detalles = consumos.Select(c => new
                {
                    DocumentoId = documentoId,
                    ConsumoId = c.Id,
                    c.Monto
                });

                await connection.ExecuteAsync(sqlInsertDetalle, detalles, transaction);

                transaction.Commit();

                return Ok(new
                {
                    Id = documentoId,
                    NumeroDocumento = numeroDocumento,
                    MontoTotal = montoTotal,
                    FechaVencimiento = fechaVencimiento,
                    mensaje = "Documento CxC generado exitosamente."
                });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                Console.WriteLine($"ERROR GenerarDocumento: {ex.Message}");
                return StatusCode(500, new { message = "Error interno al generar el documento CxC." });
            }
        }

        #endregion

        #region Pagos CxC

        /// <summary>
        /// Registrar pago contra documento CxC
        /// </summary>
        [HttpPost("pagos")]
        public async Task<IActionResult> RegistrarPago([FromBody] RegistrarPagoCxcDto dto)
        {
            using var connection = _connectionFactory.Create();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                // 1) Obtener documento
                const string sqlDoc = @"
                    SELECT Id, EmpresaId, MontoTotal, MontoPagado, MontoPendiente, Estado, Refinanciado
                    FROM CxcDocumentos
                    WHERE Id = @Id";

                var doc = await connection.QueryFirstOrDefaultAsync<dynamic>(sqlDoc, new { Id = dto.CxcDocumentoId }, transaction);

                if (doc == null)
                    return NotFound(new { message = "Documento no encontrado." });

                if ((int)doc.Estado == (int)EstadoCxc.Pagado)
                    return BadRequest(new { message = "Este documento ya está completamente pagado." });

                if ((int)doc.Estado == (int)EstadoCxc.Anulado)
                    return BadRequest(new { message = "No se puede pagar un documento anulado." });

                if ((bool)doc.Refinanciado)
                    return BadRequest(new { message = "Este documento fue refinanciado. El pago debe aplicarse al refinanciamiento." });

                if (dto.Monto <= 0)
                    return BadRequest(new { message = "El monto debe ser mayor a cero." });

                decimal montoPendiente = doc.MontoPendiente;
                if (dto.Monto > montoPendiente)
                    return BadRequest(new { message = $"El monto excede el saldo pendiente ({montoPendiente:N2})." });

                // 2) Generar número de recibo
                var anio = DateTime.Now.Year;
                var ultimoRecibo = await connection.ExecuteScalarAsync<string?>(@"
                    SELECT TOP 1 NumeroRecibo FROM CxcPagos 
                    WHERE NumeroRecibo LIKE @Pattern ORDER BY Id DESC",
                    new { Pattern = $"REC-{anio}-%" }, transaction);

                int secuencial = 1;
                if (ultimoRecibo != null)
                {
                    var partes = ultimoRecibo.Split('-');
                    if (partes.Length == 3 && int.TryParse(partes[2], out int num))
                        secuencial = num + 1;
                }
                var numeroRecibo = $"REC-{anio}-{secuencial:00000}";

                // 3) Calcular nuevos valores
                decimal montoTotal = doc.MontoTotal;
                decimal montoPagado = doc.MontoPagado;
                var nuevoMontoPagado = montoPagado + dto.Monto;
                var nuevoMontoPendiente = montoPendiente - dto.Monto;

                EstadoCxc nuevoEstado = nuevoMontoPendiente <= 0
                    ? EstadoCxc.Pagado
                    : EstadoCxc.ParcialmentePagado;

                if (nuevoMontoPendiente < 0) nuevoMontoPendiente = 0;

                // 4) Insertar pago
                const string sqlInsertPago = @"
                    INSERT INTO CxcPagos 
                        (CxcDocumentoId, NumeroRecibo, Fecha, Monto, MetodoPago, 
                         Referencia, Banco, Notas, Anulado, CreadoUtc, RegistradoPorUsuarioId)
                    OUTPUT INSERTED.Id
                    VALUES 
                        (@CxcDocumentoId, @NumeroRecibo, @Fecha, @Monto, @MetodoPago,
                         @Referencia, @Banco, @Notas, 0, @CreadoUtc, @UsuarioId)";

                var pagoId = await connection.ExecuteScalarAsync<int>(sqlInsertPago, new
                {
                    dto.CxcDocumentoId,
                    NumeroRecibo = numeroRecibo,
                    Fecha = DateTime.UtcNow,
                    dto.Monto,
                    MetodoPago = (int)dto.MetodoPago,
                    dto.Referencia,
                    dto.Banco,
                    dto.Notas,
                    CreadoUtc = DateTime.UtcNow,
                    UsuarioId = _user.Id
                }, transaction);

                // 5) Actualizar documento
                const string sqlUpdateDoc = @"
                    UPDATE CxcDocumentos 
                    SET MontoPagado = @MontoPagado,
                        MontoPendiente = @MontoPendiente,
                        Estado = @Estado
                    WHERE Id = @Id";

                await connection.ExecuteAsync(sqlUpdateDoc, new
                {
                    MontoPagado = nuevoMontoPagado,
                    MontoPendiente = nuevoMontoPendiente,
                    Estado = (int)nuevoEstado,
                    Id = dto.CxcDocumentoId
                }, transaction);

                // 6) Restaurar saldo de los clientes proporcionalmente
                decimal porcentajePago = montoTotal > 0 ? dto.Monto / montoTotal : 0;

                const string sqlRestaurarSaldos = @"
                    WITH ConsumosCliente AS (
                        SELECT 
                            c.ClienteId,
                            SUM(det.Monto) AS MontoConsumido
                        FROM CxcDocumentoDetalles det
                        INNER JOIN Consumos c ON det.ConsumoId = c.Id
                        WHERE det.CxcDocumentoId = @DocumentoId
                          AND c.Reversado = 0
                        GROUP BY c.ClienteId
                    )
                    UPDATE cli
                    SET cli.Saldo = CASE 
                        WHEN cli.Saldo + (cc.MontoConsumido * @Porcentaje) > cli.SaldoOriginal 
                        THEN cli.SaldoOriginal 
                        ELSE cli.Saldo + (cc.MontoConsumido * @Porcentaje) 
                    END
                    FROM Clientes cli
                    INNER JOIN ConsumosCliente cc ON cli.Id = cc.ClienteId";

                var clientesRestaurados = await connection.ExecuteAsync(sqlRestaurarSaldos, new
                {
                    DocumentoId = dto.CxcDocumentoId,
                    Porcentaje = porcentajePago
                }, transaction);

                transaction.Commit();

                return Ok(new
                {
                    Id = pagoId,
                    NumeroRecibo = numeroRecibo,
                    Monto = dto.Monto,
                    DocumentoNuevoSaldo = nuevoMontoPendiente,
                    DocumentoEstado = nuevoEstado.ToString(),
                    ClientesRestaurados = clientesRestaurados,
                    PorcentajeRestaurado = porcentajePago * 100,
                    mensaje = $"Pago registrado exitosamente. Se restauró crédito a {clientesRestaurados} empleados."
                });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                Console.WriteLine($"ERROR RegistrarPago: {ex.Message}");
                return StatusCode(500, new { message = "Error interno al registrar el pago." });
            }
        }

        /// <summary>
        /// Listar pagos recibidos
        /// </summary>
        [HttpGet("pagos")]
        public async Task<IActionResult> ListarPagos(
            [FromQuery] int? empresaId,
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            using var connection = _connectionFactory.Create();

            var whereClause = "WHERE p.Anulado = 0";
            var parameters = new DynamicParameters();

            if (empresaId.HasValue)
            {
                whereClause += " AND d.EmpresaId = @EmpresaId";
                parameters.Add("EmpresaId", empresaId.Value);
            }

            if (desde.HasValue)
            {
                whereClause += " AND p.Fecha >= @Desde";
                parameters.Add("Desde", desde.Value);
            }

            if (hasta.HasValue)
            {
                whereClause += " AND p.Fecha <= @Hasta";
                parameters.Add("Hasta", hasta.Value.AddDays(1));
            }

            // Contar y sumar
            var resumenSql = $@"
                SELECT COUNT(*) AS Total, ISNULL(SUM(p.Monto), 0) AS MontoTotal
                FROM CxcPagos p
                INNER JOIN CxcDocumentos d ON p.CxcDocumentoId = d.Id
                {whereClause}";

            var resumen = await connection.QueryFirstAsync<dynamic>(resumenSql, parameters);
            int totalCount = resumen.Total;
            decimal montoTotal = resumen.MontoTotal;

            // Obtener datos paginados
            parameters.Add("Offset", (page - 1) * pageSize);
            parameters.Add("PageSize", pageSize);

            var dataSql = $@"
                SELECT 
                    p.Id, p.NumeroRecibo, p.Fecha, p.Monto, p.MetodoPago, 
                    p.Referencia, p.Banco,
                    d.Id AS DocumentoId, d.NumeroDocumento, d.EmpresaId, 
                    e.Nombre AS EmpresaNombre,
                    u.Nombre AS RegistradoPor
                FROM CxcPagos p
                INNER JOIN CxcDocumentos d ON p.CxcDocumentoId = d.Id
                INNER JOIN Empresas e ON d.EmpresaId = e.Id
                LEFT JOIN Usuarios u ON p.RegistradoPorUsuarioId = u.Id
                {whereClause}
                ORDER BY p.Fecha DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var rawData = await connection.QueryAsync<dynamic>(dataSql, parameters);

            var data = rawData.Select(r => new
            {
                r.Id,
                r.NumeroRecibo,
                r.Fecha,
                r.Monto,
                MetodoPago = (int)r.MetodoPago,
                MetodoPagoNombre = ((MetodoPago)(int)r.MetodoPago).ToString(),
                r.Referencia,
                r.Banco,
                r.DocumentoId,
                DocumentoNumero = (string)r.NumeroDocumento,
                r.EmpresaId,
                r.EmpresaNombre,
                r.RegistradoPor
            });

            return Ok(new
            {
                data,
                resumen = new { totalPagos = totalCount, montoTotal },
                pagination = new { total = totalCount, page, pageSize, totalPages = (int)Math.Ceiling(totalCount / (double)pageSize) }
            });
        }

        /// <summary>
        /// Anular un pago
        /// </summary>
        [HttpPost("pagos/{id:int}/anular")]
        public async Task<IActionResult> AnularPago(int id, [FromBody] AnularPagoDto dto)
        {
            using var connection = _connectionFactory.Create();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                // Obtener pago
                const string sqlPago = @"
                    SELECT Id, CxcDocumentoId, Monto, Anulado 
                    FROM CxcPagos WHERE Id = @Id";

                var pago = await connection.QueryFirstOrDefaultAsync<dynamic>(sqlPago, new { Id = id }, transaction);

                if (pago == null)
                    return NotFound(new { message = "Pago no encontrado." });

                if ((bool)pago.Anulado)
                    return BadRequest(new { message = "Este pago ya está anulado." });

                // Obtener documento
                const string sqlDoc = @"
                    SELECT Id, MontoPagado, MontoPendiente, MontoTotal, FechaVencimiento
                    FROM CxcDocumentos WHERE Id = @Id";

                var doc = await connection.QueryFirstOrDefaultAsync<dynamic>(
                    sqlDoc, new { Id = (int)pago.CxcDocumentoId }, transaction);

                if (doc == null)
                    return NotFound(new { message = "Documento no encontrado." });

                // Anular pago
                const string sqlAnular = @"
                    UPDATE CxcPagos 
                    SET Anulado = 1, 
                        AnuladoUtc = @AnuladoUtc, 
                        AnuladoPorUsuarioId = @UsuarioId,
                        MotivoAnulacion = @Motivo
                    WHERE Id = @Id";

                await connection.ExecuteAsync(sqlAnular, new
                {
                    Id = id,
                    AnuladoUtc = DateTime.UtcNow,
                    UsuarioId = _user.Id,
                    dto.Motivo
                }, transaction);

                // Revertir en documento
                decimal nuevoMontoPagado = (decimal)doc.MontoPagado - (decimal)pago.Monto;
                decimal nuevoMontoPendiente = (decimal)doc.MontoPendiente + (decimal)pago.Monto;

                EstadoCxc nuevoEstado;
                if (nuevoMontoPendiente >= (decimal)doc.MontoTotal)
                    nuevoEstado = ((DateTime)doc.FechaVencimiento < DateTime.UtcNow)
                        ? EstadoCxc.Vencido
                        : EstadoCxc.Pendiente;
                else
                    nuevoEstado = EstadoCxc.ParcialmentePagado;

                const string sqlUpdateDoc = @"
                    UPDATE CxcDocumentos 
                    SET MontoPagado = @MontoPagado, 
                        MontoPendiente = @MontoPendiente, 
                        Estado = @Estado
                    WHERE Id = @Id";

                await connection.ExecuteAsync(sqlUpdateDoc, new
                {
                    MontoPagado = nuevoMontoPagado,
                    MontoPendiente = nuevoMontoPendiente,
                    Estado = (int)nuevoEstado,
                    Id = (int)pago.CxcDocumentoId
                }, transaction);

                transaction.Commit();

                return Ok(new { mensaje = "Pago anulado exitosamente." });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                Console.WriteLine($"ERROR AnularPago: {ex.Message}");
                return StatusCode(500, new { message = "Error interno al anular el pago." });
            }
        }

        #endregion

        #region Estado de Cuenta Empresa

        /// <summary>
        /// Obtener estado de cuenta de una empresa
        /// </summary>
        [HttpGet("empresas/{empresaId:int}/estado-cuenta")]
        public async Task<IActionResult> EstadoCuentaEmpresa(int empresaId)
        {
            using var connection = _connectionFactory.Create();

            // Verificar empresa
            var empresa = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT Id, Nombre, Rnc FROM Empresas WHERE Id = @EmpresaId",
                new { EmpresaId = empresaId });

            if (empresa == null)
                return NotFound(new { message = "Empresa no encontrada." });

            // Documentos pendientes
            const string sqlDocumentos = @"
                SELECT 
                    Id, NumeroDocumento, FechaEmision, FechaVencimiento,
                    MontoTotal, MontoPagado, MontoPendiente, Estado,
                    CASE WHEN FechaVencimiento < GETUTCDATE() 
                         THEN DATEDIFF(DAY, FechaVencimiento, GETUTCDATE()) 
                         ELSE 0 END AS DiasVencido
                FROM CxcDocumentos
                WHERE EmpresaId = @EmpresaId AND Estado IN (0, 1, 3)
                ORDER BY FechaVencimiento";

            var documentosPendientes = (await connection.QueryAsync<dynamic>(sqlDocumentos, new { EmpresaId = empresaId })).ToList();

            // Refinanciamientos pendientes
            const string sqlRefinanciamientos = @"
                SELECT 
                    Id, NumeroRefinanciamiento, Fecha, MontoOriginal, 
                    MontoPendiente, FechaVencimiento, Estado
                FROM RefinanciamientoDeudas
                WHERE EmpresaId = @EmpresaId AND Estado IN (0, 1)";

            var refinanciamientos = (await connection.QueryAsync<dynamic>(sqlRefinanciamientos, new { EmpresaId = empresaId })).ToList();

            // Calcular totales
            decimal totalPendiente = documentosPendientes.Sum(d => (decimal)d.MontoPendiente);
            decimal totalRefinanciado = refinanciamientos.Sum(r => (decimal)r.MontoPendiente);
            decimal totalVencido = documentosPendientes
                .Where(d => (int)d.DiasVencido > 0)
                .Sum(d => (decimal)d.MontoPendiente);

            return Ok(new
            {
                Empresa = new
                {
                    Id = (int)empresa.Id,
                    Nombre = (string)empresa.Nombre,
                    Rnc = (string)empresa.Rnc
                },
                Resumen = new
                {
                    TotalPendiente = totalPendiente,
                    TotalRefinanciado = totalRefinanciado,
                    TotalVencido = totalVencido,
                    TotalDeuda = totalPendiente + totalRefinanciado
                },
                DocumentosPendientes = documentosPendientes,
                Refinanciamientos = refinanciamientos
            });
        }

        #endregion
    }

    #region DTOs

    public class GenerarCxcDto
    {
        public int EmpresaId { get; set; }
        public DateTime PeriodoDesde { get; set; }
        public DateTime PeriodoHasta { get; set; }
        public string? Notas { get; set; }
    }

    public class RegistrarPagoCxcDto
    {
        public int CxcDocumentoId { get; set; }
        public decimal Monto { get; set; }
        public MetodoPago MetodoPago { get; set; } = MetodoPago.Transferencia;
        public string? Referencia { get; set; }
        public string? Banco { get; set; }
        public string? Notas { get; set; }
    }

    public class AnularPagoDto
    {
        public string? Motivo { get; set; }
    }

    #endregion
}