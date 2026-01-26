using Dapper;
using Consumo_App.Data.Sql;
using Consumo_App.Models.Pagos;
using Consumo_App.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/refinanciamiento")]
    [Authorize]
    public class RefinanciamientoController : ControllerBase
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly IUserContext _user;

        public RefinanciamientoController(SqlConnectionFactory connectionFactory, IUserContext user)
        {
            _connectionFactory = connectionFactory;
            _user = user;
        }

        #region Dashboard

        /// <summary>
        /// GET /api/refinanciamiento/dashboard
        /// </summary>
        [HttpGet("dashboard")]
        public async Task<IActionResult> Dashboard()
        {
            using var connection = _connectionFactory.Create();
            var hoy = DateTime.UtcNow.Date;
            var en30Dias = hoy.AddDays(30);
            var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);

            // Resumen de refinanciamientos activos
            const string sqlResumen = @"
                SELECT 
                    COUNT(*) AS TotalActivos,
                    ISNULL(SUM(MontoPendiente), 0) AS MontoPendiente,
                    SUM(CASE WHEN FechaVencimiento < @Hoy THEN 1 ELSE 0 END) AS TotalVencidos,
                    ISNULL(SUM(CASE WHEN FechaVencimiento < @Hoy THEN MontoPendiente ELSE 0 END), 0) AS MontoVencido,
                    SUM(CASE WHEN FechaVencimiento >= @Hoy AND FechaVencimiento <= @En30Dias THEN 1 ELSE 0 END) AS ProximosVencer
                FROM RefinanciamientoDeudas
                WHERE Estado NOT IN (3, 4, 5)"; // Anulado, Pagado, Castigado

            var resumen = await connection.QueryFirstAsync<dynamic>(sqlResumen, new { Hoy = hoy, En30Dias = en30Dias });

            // Cobrado este mes
            var cobradoMes = await connection.ExecuteScalarAsync<decimal>(@"
                SELECT ISNULL(SUM(Monto), 0) FROM RefinanciamientoPagos 
                WHERE Anulado = 0 AND Fecha >= @InicioMes",
                new { InicioMes = inicioMes });

            // Por empresa (top 10)
            const string sqlPorEmpresa = @"
                SELECT TOP 10
                    r.EmpresaId,
                    e.Nombre AS EmpresaNombre,
                    COUNT(*) AS Cantidad,
                    SUM(r.MontoPendiente) AS MontoPendiente,
                    SUM(CASE WHEN r.FechaVencimiento < @Hoy THEN r.MontoPendiente ELSE 0 END) AS MontoVencido
                FROM RefinanciamientoDeudas r
                INNER JOIN Empresas e ON r.EmpresaId = e.Id
                WHERE r.Estado NOT IN (3, 4, 5)
                GROUP BY r.EmpresaId, e.Nombre
                ORDER BY SUM(r.MontoPendiente) DESC";

            var porEmpresa = await connection.QueryAsync<dynamic>(sqlPorEmpresa, new { Hoy = hoy });

            return Ok(new
            {
                resumen = new
                {
                    totalActivos = (int)resumen.TotalActivos,
                    totalVencidos = (int)resumen.TotalVencidos,
                    montoPendiente = (decimal)resumen.MontoPendiente,
                    montoVencido = (decimal)resumen.MontoVencido,
                    proximosVencer = (int)resumen.ProximosVencer,
                    cobradoMes
                },
                porEmpresa
            });
        }

        #endregion

        #region Listar Refinanciamientos

        /// <summary>
        /// GET /api/refinanciamiento
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Listar(
            [FromQuery] int? empresaId,
            [FromQuery] EstadoRefinanciamiento? estado,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            using var connection = _connectionFactory.Create();

            var whereClause = "WHERE 1=1";
            var parameters = new DynamicParameters();

            if (empresaId.HasValue)
            {
                whereClause += " AND r.EmpresaId = @EmpresaId";
                parameters.Add("EmpresaId", empresaId.Value);
            }

            if (estado.HasValue)
            {
                whereClause += " AND r.Estado = @Estado";
                parameters.Add("Estado", (int)estado.Value);
            }

            // Contar total
            var countSql = $"SELECT COUNT(*) FROM RefinanciamientoDeudas r {whereClause}";
            var totalCount = await connection.ExecuteScalarAsync<int>(countSql, parameters);

            // Resumen
            var resumenSql = $@"
                SELECT 
                    ISNULL(SUM(r.MontoOriginal), 0) AS MontoTotalOriginal,
                    ISNULL(SUM(r.MontoPagado), 0) AS MontoTotalPagado,
                    ISNULL(SUM(r.MontoPendiente), 0) AS MontoTotalPendiente
                FROM RefinanciamientoDeudas r
                {whereClause}";
            var resumenData = await connection.QueryFirstAsync<dynamic>(resumenSql, parameters);

            // Obtener datos paginados
            parameters.Add("Offset", (page - 1) * pageSize);
            parameters.Add("PageSize", pageSize);

            var dataSql = $@"
                SELECT 
                    r.Id,
                    r.NumeroRefinanciamiento,
                    r.EmpresaId,
                    e.Nombre AS EmpresaNombre,
                    r.CxcDocumentoId,
                    d.NumeroDocumento AS DocumentoNumero,
                    d.MontoTotal AS DocumentoMonto,
                    r.Fecha,
                    r.MontoOriginal,
                    r.MontoPagado,
                    r.MontoPendiente,
                    r.FechaVencimiento,
                    r.Estado,
                    CASE 
                        WHEN r.FechaVencimiento < GETUTCDATE() 
                             AND r.Estado NOT IN (4, 5)
                        THEN DATEDIFF(DAY, r.FechaVencimiento, GETUTCDATE())
                        ELSE 0
                    END AS DiasVencido
                FROM RefinanciamientoDeudas r
                INNER JOIN Empresas e ON r.EmpresaId = e.Id
                INNER JOIN CxcDocumentos d ON r.CxcDocumentoId = d.Id
                {whereClause}
                ORDER BY r.Fecha DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var rawData = await connection.QueryAsync<dynamic>(dataSql, parameters);

            var data = rawData.Select(r => new
            {
                r.Id,
                r.NumeroRefinanciamiento,
                r.EmpresaId,
                r.EmpresaNombre,
                r.CxcDocumentoId,
                r.DocumentoNumero,
                r.DocumentoMonto,
                r.Fecha,
                r.MontoOriginal,
                r.MontoPagado,
                r.MontoPendiente,
                r.FechaVencimiento,
                Estado = (int)r.Estado,
                EstadoNombre = ((EstadoRefinanciamiento)(int)r.Estado).ToString(),
                r.DiasVencido
            });

            return Ok(new
            {
                data,
                resumen = new
                {
                    TotalRefinanciamientos = totalCount,
                    MontoTotalOriginal = (decimal)resumenData.MontoTotalOriginal,
                    MontoTotalPagado = (decimal)resumenData.MontoTotalPagado,
                    MontoTotalPendiente = (decimal)resumenData.MontoTotalPendiente
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

        #endregion

        #region Obtener Detalle

        /// <summary>
        /// GET /api/refinanciamiento/{id}
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> Obtener(int id)
        {
            using var connection = _connectionFactory.Create();

            // Obtener refinanciamiento
            const string sqlRef = @"
                SELECT 
                    r.Id, r.NumeroRefinanciamiento, r.EmpresaId, r.CxcDocumentoId,
                    r.Fecha, r.MontoOriginal, r.MontoPagado, r.MontoPendiente,
                    r.FechaVencimiento, r.Estado, r.Motivo, r.Notas, r.CreadoUtc,
                    e.Nombre AS EmpresaNombre, e.Rnc AS EmpresaRnc,
                    d.NumeroDocumento, d.FechaEmision AS DocumentoFechaEmision, d.MontoTotal AS DocumentoMontoTotal,
                    u.Nombre AS CreadoPor
                FROM RefinanciamientoDeudas r
                INNER JOIN Empresas e ON r.EmpresaId = e.Id
                INNER JOIN CxcDocumentos d ON r.CxcDocumentoId = d.Id
                LEFT JOIN Usuarios u ON r.CreadoPorUsuarioId = u.Id
                WHERE r.Id = @Id";

            var ref_ = await connection.QueryFirstOrDefaultAsync<dynamic>(sqlRef, new { Id = id });

            if (ref_ == null)
                return NotFound(new { message = "Refinanciamiento no encontrado." });

            // Obtener pagos
            const string sqlPagos = @"
                SELECT 
                    p.Id, p.Fecha, p.Monto, p.MetodoPago, p.Referencia, p.Notas,
                    u.Nombre AS RegistradoPor
                FROM RefinanciamientoPagos p
                LEFT JOIN Usuarios u ON p.RegistradoPorUsuarioId = u.Id
                WHERE p.RefinanciamientoId = @Id AND p.Anulado = 0
                ORDER BY p.Fecha DESC";

            var pagosRaw = await connection.QueryAsync<dynamic>(sqlPagos, new { Id = id });
            var pagos = pagosRaw.Select(p => new
            {
                p.Id,
                p.Fecha,
                p.Monto,
                MetodoPago = (int)p.MetodoPago,
                MetodoPagoNombre = ((MetodoPago)(int)p.MetodoPago).ToString(),
                p.Referencia,
                p.Notas,
                p.RegistradoPor
            });

            // Obtener consumos originales del documento
            const string sqlConsumos = @"
                SELECT 
                    det.ConsumoId, det.Monto, c.Fecha,
                    cli.Nombre AS ClienteNombre, cli.Cedula AS ClienteCedula
                FROM CxcDocumentoDetalles det
                INNER JOIN Consumos c ON det.ConsumoId = c.Id
                INNER JOIN Clientes cli ON c.ClienteId = cli.Id
                WHERE det.CxcDocumentoId = @DocId
                ORDER BY c.Fecha";

            var consumos = await connection.QueryAsync<dynamic>(sqlConsumos, new { DocId = (int)ref_.CxcDocumentoId });

            return Ok(new
            {
                ref_.Id,
                ref_.NumeroRefinanciamiento,
                ref_.EmpresaId,
                Empresa = new
                {
                    Id = (int)ref_.EmpresaId,
                    Nombre = (string)ref_.EmpresaNombre,
                    Rnc = (string)ref_.EmpresaRnc
                },
                ref_.CxcDocumentoId,
                DocumentoOriginal = new
                {
                    Id = (int)ref_.CxcDocumentoId,
                    NumeroDocumento = (string)ref_.NumeroDocumento,
                    FechaEmision = (DateTime)ref_.DocumentoFechaEmision,
                    MontoTotal = (decimal)ref_.DocumentoMontoTotal
                },
                ref_.Fecha,
                ref_.MontoOriginal,
                ref_.MontoPagado,
                ref_.MontoPendiente,
                ref_.FechaVencimiento,
                Estado = (int)ref_.Estado,
                EstadoNombre = ((EstadoRefinanciamiento)(int)ref_.Estado).ToString(),
                ref_.Motivo,
                ref_.Notas,
                ref_.CreadoUtc,
                ref_.CreadoPor,
                Pagos = pagos,
                ConsumosOriginales = consumos
            });
        }

        #endregion

        #region Crear Refinanciamiento

        /// <summary>
        /// POST /api/refinanciamiento
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Crear([FromBody] CrearRefinanciamientoDto dto)
        {
            using var connection = _connectionFactory.Create();
            await connection.OpenAsync();

            // Obtener documento
            var doc = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT Id, EmpresaId, MontoPendiente, Estado, Refinanciado
                FROM CxcDocumentos WHERE Id = @Id",
                new { Id = dto.CxcDocumentoId });

            if (doc == null)
                return NotFound(new { message = "Documento no encontrado." });

            // Validaciones
            if ((int)doc.Estado == (int)EstadoCxc.Pagado)
                return BadRequest(new { message = "No se puede refinanciar un documento ya pagado." });

            if ((int)doc.Estado == (int)EstadoCxc.Anulado)
                return BadRequest(new { message = "No se puede refinanciar un documento anulado." });

            if ((bool)doc.Refinanciado)
                return BadRequest(new { message = "Este documento ya fue refinanciado." });

            decimal montoPendiente = doc.MontoPendiente;
            if (montoPendiente <= 0)
                return BadRequest(new { message = "El documento no tiene saldo pendiente." });

            using var transaction = connection.BeginTransaction();

            try
            {
                // Generar número de refinanciamiento
                var año = DateTime.Now.Year;
                var ultimoRef = await connection.ExecuteScalarAsync<string?>(@"
                    SELECT TOP 1 NumeroRefinanciamiento FROM RefinanciamientoDeudas 
                    WHERE NumeroRefinanciamiento LIKE @Pattern ORDER BY Id DESC",
                    new { Pattern = $"REF-{año}-%" }, transaction);

                int secuencial = 1;
                if (ultimoRef != null)
                {
                    var partes = ultimoRef.Split('-');
                    if (partes.Length == 3 && int.TryParse(partes[2], out int num))
                        secuencial = num + 1;
                }
                var numeroRefinanciamiento = $"REF-{año}-{secuencial:00000}";

                // Insertar refinanciamiento
                const string sqlInsert = @"
                    INSERT INTO RefinanciamientoDeudas 
                        (CxcDocumentoId, EmpresaId, NumeroRefinanciamiento, Fecha, 
                         MontoOriginal, MontoPagado, MontoPendiente, FechaVencimiento, 
                         Estado, Motivo, Notas, CreadoUtc, CreadoPorUsuarioId)
                    OUTPUT INSERTED.Id
                    VALUES 
                        (@DocId, @EmpresaId, @NumRef, @Fecha,
                         @MontoOriginal, 0, @MontoPendiente, @FechaVenc,
                         @Estado, @Motivo, @Notas, @CreadoUtc, @UsuarioId)";

                var refinanciamientoId = await connection.ExecuteScalarAsync<int>(sqlInsert, new
                {
                    DocId = dto.CxcDocumentoId,
                    EmpresaId = (int)doc.EmpresaId,
                    NumRef = numeroRefinanciamiento,
                    Fecha = DateTime.UtcNow,
                    MontoOriginal = montoPendiente,
                    MontoPendiente = montoPendiente,
                    FechaVenc = dto.NuevaFechaVencimiento,
                    Estado = (int)EstadoRefinanciamiento.Pendiente,
                    dto.Motivo,
                    dto.Notas,
                    CreadoUtc = DateTime.UtcNow,
                    UsuarioId = _user.Id
                }, transaction);

                // Marcar documento como refinanciado
                await connection.ExecuteAsync(@"
                    UPDATE CxcDocumentos 
                    SET Refinanciado = 1, FechaRefinanciamiento = @Fecha, Estado = @Estado
                    WHERE Id = @Id",
                    new
                    {
                        Fecha = DateTime.UtcNow,
                        Estado = (int)EstadoCxc.Refinanciado,
                        Id = dto.CxcDocumentoId
                    }, transaction);

                // Restaurar saldo a los clientes/empleados
                await connection.ExecuteAsync(@"
                    UPDATE cli
                    SET cli.Saldo = cli.Saldo + det.Monto
                    FROM Clientes cli
                    INNER JOIN Consumos c ON c.ClienteId = cli.Id
                    INNER JOIN CxcDocumentoDetalles det ON det.ConsumoId = c.Id
                    WHERE det.CxcDocumentoId = @DocId",
                    new { DocId = dto.CxcDocumentoId }, transaction);

                transaction.Commit();

                return Ok(new
                {
                    Id = refinanciamientoId,
                    NumeroRefinanciamiento = numeroRefinanciamiento,
                    MontoOriginal = montoPendiente,
                    FechaVencimiento = dto.NuevaFechaVencimiento,
                    mensaje = "Refinanciamiento creado exitosamente. El saldo ha sido restaurado a los empleados."
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
        /// POST /api/refinanciamiento/{id}/pagos
        /// </summary>
        [HttpPost("{id:int}/pagos")]
        public async Task<IActionResult> RegistrarPago(int id, [FromBody] PagoRefinanciamientoDto dto)
        {
            using var connection = _connectionFactory.Create();
            await connection.OpenAsync();

            // Obtener refinanciamiento
            var ref_ = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT MontoPagado, MontoPendiente, MontoOriginal, Estado, CxcDocumentoId
                FROM RefinanciamientoDeudas WHERE Id = @Id",
                new { Id = id });

            if (ref_ == null)
                return NotFound(new { message = "Refinanciamiento no encontrado." });

            // Validaciones
            if ((int)ref_.Estado == (int)EstadoRefinanciamiento.Pagado)
                return BadRequest(new { message = "Este refinanciamiento ya está pagado." });

            if ((int)ref_.Estado == (int)EstadoRefinanciamiento.Castigado)
                return BadRequest(new { message = "No se pueden registrar pagos en un refinanciamiento castigado." });

            if (dto.Monto <= 0)
                return BadRequest(new { message = "El monto debe ser mayor a cero." });

            decimal montoPendienteRef = ref_.MontoPendiente;
            if (dto.Monto > montoPendienteRef)
                return BadRequest(new { message = $"El monto excede el saldo pendiente ({montoPendienteRef:N2})." });

            int cxcDocumentoId = ref_.CxcDocumentoId;

            // Calcular nuevos valores
            decimal nuevoMontoPagadoRef = (decimal)ref_.MontoPagado + dto.Monto;
            decimal nuevoMontoPendienteRef = montoPendienteRef - dto.Monto;

            EstadoRefinanciamiento nuevoEstadoRef = nuevoMontoPendienteRef <= 0
                ? EstadoRefinanciamiento.Pagado
                : EstadoRefinanciamiento.ParcialmentePagado;

            if (nuevoMontoPendienteRef < 0) nuevoMontoPendienteRef = 0;

            using var transaction = connection.BeginTransaction();

            try
            {
                // Insertar pago
                const string sqlPago = @"
                    INSERT INTO RefinanciamientoPagos 
                        (RefinanciamientoId, Fecha, Monto, MetodoPago, Referencia, Notas, CreadoUtc, RegistradoPorUsuarioId, Anulado)
                    OUTPUT INSERTED.Id
                    VALUES 
                        (@RefId, @Fecha, @Monto, @MetodoPago, @Referencia, @Notas, @CreadoUtc, @UsuarioId, 0)";

                var pagoId = await connection.ExecuteScalarAsync<int>(sqlPago, new
                {
                    RefId = id,
                    Fecha = DateTime.UtcNow,
                    dto.Monto,
                    MetodoPago = (int)dto.MetodoPago,
                    dto.Referencia,
                    dto.Notas,
                    CreadoUtc = DateTime.UtcNow,
                    UsuarioId = _user.Id
                }, transaction);

                // Actualizar refinanciamiento
                await connection.ExecuteAsync(@"
                    UPDATE RefinanciamientoDeudas 
                    SET MontoPagado = @MontoPagado, MontoPendiente = @MontoPendiente, Estado = @Estado
                    WHERE Id = @Id",
                    new
                    {
                        MontoPagado = nuevoMontoPagadoRef,
                        MontoPendiente = nuevoMontoPendienteRef,
                        Estado = (int)nuevoEstadoRef,
                        Id = id
                    }, transaction);

                // Obtener documento CxC
                var doc = await connection.QueryFirstAsync<dynamic>(@"
                    SELECT MontoTotal, MontoPagado, MontoPendiente FROM CxcDocumentos WHERE Id = @Id",
                    new { Id = cxcDocumentoId }, transaction);

                decimal nuevoMontoPagadoDoc = (decimal)doc.MontoPagado + dto.Monto;
                decimal nuevoMontoPendienteDoc = (decimal)doc.MontoPendiente - dto.Monto;
                if (nuevoMontoPendienteDoc < 0) nuevoMontoPendienteDoc = 0;

                // Si refinanciamiento pagado, marcar documento como pagado
                EstadoCxc nuevoEstadoDoc = nuevoEstadoRef == EstadoRefinanciamiento.Pagado
                    ? EstadoCxc.Pagado
                    : EstadoCxc.Refinanciado;

                // Actualizar documento CxC
                await connection.ExecuteAsync(@"
                    UPDATE CxcDocumentos 
                    SET MontoPagado = @MontoPagado, MontoPendiente = @MontoPendiente, Estado = @Estado
                    WHERE Id = @Id",
                    new
                    {
                        MontoPagado = nuevoMontoPagadoDoc,
                        MontoPendiente = nuevoMontoPendienteDoc,
                        Estado = (int)nuevoEstadoDoc,
                        Id = cxcDocumentoId
                    }, transaction);

                transaction.Commit();

                return Ok(new
                {
                    Id = pagoId,
                    Monto = dto.Monto,
                    NuevoSaldoRefinanciamiento = nuevoMontoPendienteRef,
                    NuevoSaldoDocumento = nuevoMontoPendienteDoc,
                    EstadoRefinanciamiento = nuevoEstadoRef.ToString(),
                    EstadoDocumento = nuevoEstadoDoc.ToString(),
                    mensaje = nuevoEstadoRef == EstadoRefinanciamiento.Pagado
                        ? "¡Refinanciamiento pagado completamente!"
                        : $"Pago registrado. Saldo pendiente: RD${nuevoMontoPendienteRef:N2}"
                });
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        /// <summary>
        /// POST /api/refinanciamiento/{id}/castigar
        /// </summary>
        [HttpPost("{id:int}/castigar")]
        public async Task<IActionResult> Castigar(int id, [FromBody] CastigarRefinanciamientoDto dto)
        {
            using var connection = _connectionFactory.Create();

            // Obtener refinanciamiento
            var ref_ = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT MontoPendiente, Estado, Notas FROM RefinanciamientoDeudas WHERE Id = @Id",
                new { Id = id });

            if (ref_ == null)
                return NotFound(new { message = "Refinanciamiento no encontrado." });

            // Validaciones
            if ((int)ref_.Estado == (int)EstadoRefinanciamiento.Pagado)
                return BadRequest(new { message = "No se puede castigar un refinanciamiento ya pagado." });

            if ((int)ref_.Estado == (int)EstadoRefinanciamiento.Castigado)
                return BadRequest(new { message = "Este refinanciamiento ya está castigado." });

            decimal montoPendiente = ref_.MontoPendiente;
            string? notas = ref_.Notas;
            var nuevasNotas = $"{notas}\n[CASTIGADO] {DateTime.Now:dd/MM/yyyy}: {dto.Motivo}";

            await connection.ExecuteAsync(@"
                UPDATE RefinanciamientoDeudas SET Estado = @Estado, Notas = @Notas WHERE Id = @Id",
                new
                {
                    Estado = (int)EstadoRefinanciamiento.Castigado,
                    Notas = nuevasNotas,
                    Id = id
                });

            return Ok(new
            {
                mensaje = "Refinanciamiento castigado exitosamente.",
                montoPerdido = montoPendiente
            });
        }

        #endregion
    }

    #region DTOs

    public class CrearRefinanciamientoDto
    {
        public int CxcDocumentoId { get; set; }
        public DateTime NuevaFechaVencimiento { get; set; }
        public string? Motivo { get; set; }
        public string? Notas { get; set; }
    }

    public class PagoRefinanciamientoDto
    {
        public decimal Monto { get; set; }
        public MetodoPago MetodoPago { get; set; } = MetodoPago.Transferencia;
        public string? Referencia { get; set; }
        public string? Notas { get; set; }
    }

    public class CastigarRefinanciamientoDto
    {
        public string? Motivo { get; set; }
    }

    #endregion
}