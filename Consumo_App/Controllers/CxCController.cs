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
    [Route("api/cxc")]
    [Authorize]
    public class CuentasPorCobrarController : ControllerBase
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly IUserContext _user;

        public CuentasPorCobrarController(SqlConnectionFactory connectionFactory, IUserContext user)
        {
            _connectionFactory = connectionFactory;
            _user = user;
        }

        #region Dashboard

        /*/// <summary>
        /// GET /api/cxc/dashboard
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
                    ISNULL(SUM(MontoPendiente), 0) AS TotalPorCobrar,
                    ISNULL(SUM(CASE WHEN FechaVencimiento < @Hoy THEN MontoPendiente ELSE 0 END), 0) AS TotalVencido,
                    ISNULL(SUM(CASE WHEN Estado = 4 THEN MontoPendiente ELSE 0 END), 0) AS TotalRefinanciado
                FROM CxcDocumentos
                WHERE Anulado = 0 AND Estado != 2"; // 2 = Pagado

            var totales = await connection.QueryFirstAsync<dynamic>(sqlTotales, new { Hoy = hoy }); */

        public class DashboardCxcTotalesDto
        {
            public int TotalDocumentos { get; set; }
            public decimal TotalPorCobrar { get; set; }
            public decimal TotalVencido { get; set; }
            public decimal TotalRefinanciado { get; set; }
        }

        [HttpGet("dashboard")]
        public async Task<IActionResult> Dashboard()
        {
            using var connection = _connectionFactory.Create();
            var hoy = DateTime.UtcNow.Date;

            const string sqlTotales = @"
        SELECT 
            CAST(COUNT(*) AS INT) AS TotalDocumentos,
            ISNULL(SUM(MontoPendiente), 0) AS TotalPorCobrar,
            ISNULL(SUM(CASE WHEN FechaVencimiento < @Hoy THEN MontoPendiente ELSE 0 END), 0) AS TotalVencido,
            ISNULL(SUM(CASE WHEN Estado = 4 THEN MontoPendiente ELSE 0 END), 0) AS TotalRefinanciado
        FROM CxcDocumentos
        WHERE Anulado = 0 AND Estado != 2";

            var totales = await connection.QueryFirstAsync<DashboardCxcTotalesDto>(sqlTotales, new { Hoy = hoy });


            // Cobrado este mes
            var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
            const string sqlCobradoMes = @"
                SELECT ISNULL(SUM(Monto), 0) 
                FROM CxcPagos 
                WHERE Anulado = 0 AND Fecha >= @InicioMes";

            var cobradoMes = await connection.ExecuteScalarAsync<decimal>(sqlCobradoMes, new { InicioMes = inicioMes });

            // Por empresa (top 10)
            const string sqlPorEmpresa = @"
                SELECT TOP 10
                    d.EmpresaId,
                    e.Nombre AS EmpresaNombre,
                    e.DiaCorte,
                    COUNT(*) AS Documentos,
                    SUM(d.MontoPendiente) AS PorCobrar,
                    SUM(CASE WHEN d.FechaVencimiento < @Hoy THEN d.MontoPendiente ELSE 0 END) AS Vencido
                FROM CxcDocumentos d
                INNER JOIN Empresas e ON d.EmpresaId = e.Id
                WHERE d.Anulado = 0 AND d.Estado != 2
                GROUP BY d.EmpresaId, e.Nombre, e.DiaCorte
                ORDER BY SUM(d.MontoPendiente) DESC";

            var porEmpresa = await connection.QueryAsync<dynamic>(sqlPorEmpresa, new { Hoy = hoy });

            return Ok(new
            {
                resumen = new
                {
                    TotalDocumentos = (int)totales.TotalDocumentos,
                    TotalPorCobrar = (decimal)totales.TotalPorCobrar,
                    TotalVencido = (decimal)totales.TotalVencido,
                    TotalRefinanciado = (decimal)totales.TotalRefinanciado
                },
                cobradoMes,
                porEmpresa
            });
        }

        #endregion

        #region Empresas con saldo

        [HttpGet("empresas")]
        public async Task<IActionResult> ListarEmpresas()
        {
            using var connection = _connectionFactory.Create();
            var hoy = DateTime.UtcNow.Date;

            const string sql = @"
        SELECT 
            d.EmpresaId,
            ISNULL(e.Nombre, 'Sin nombre') AS EmpresaNombre,
            e.DiaCorte,
            CAST(COUNT(*) AS INT) AS DocumentosPendientes,
            ISNULL(SUM(d.MontoPendiente), 0) AS TotalPorCobrar,
            ISNULL(SUM(CASE WHEN d.Estado = 4 THEN d.MontoPendiente ELSE 0 END), 0) AS TotalRefinanciado,
            ISNULL(SUM(CASE WHEN d.FechaVencimiento < @Hoy THEN d.MontoPendiente ELSE 0 END), 0) AS Vencido
        FROM CxcDocumentos d
        INNER JOIN Empresas e ON d.EmpresaId = e.Id
        WHERE d.Anulado = 0 AND d.Estado != 2
        GROUP BY d.EmpresaId, e.Nombre, e.DiaCorte
        ORDER BY SUM(d.MontoPendiente) DESC";

            var empresas = await connection.QueryAsync<EmpresaCxcDto>(sql, new { Hoy = hoy });
            return Ok(empresas);
        }


        /* /// <summary>
         /// GET /api/cxc/empresas
         /// </summary>
         [HttpGet("empresas")]
         public async Task<IActionResult> ListarEmpresas()
         {
             using var connection = _connectionFactory.Create();
             var hoy = DateTime.UtcNow.Date;

             const string sql = @"
                 SELECT 
                     d.EmpresaId,
                     e.Nombre AS EmpresaNombre,
                     e.DiaCorte,
                     COUNT(*) AS DocumentosPendientes,
                     SUM(d.MontoPendiente) AS TotalPorCobrar,
                     SUM(CASE WHEN d.Estado = 4 THEN d.MontoPendiente ELSE 0 END) AS TotalRefinanciado,
                     SUM(CASE WHEN d.FechaVencimiento < @Hoy THEN d.MontoPendiente ELSE 0 END) AS Vencido
                 FROM CxcDocumentos d
                 INNER JOIN Empresas e ON d.EmpresaId = e.Id
                 WHERE d.Anulado = 0 AND d.Estado != 2
                 GROUP BY d.EmpresaId, e.Nombre, e.DiaCorte
                 ORDER BY SUM(d.MontoPendiente) DESC";

             var empresas = await connection.QueryAsync<dynamic>(sql, new { Hoy = hoy });
             return Ok(empresas);
         } */

        #endregion

        #region Documentos CxC

        /// <summary>
        /// GET /api/cxc/empresas/{empresaId}/documentos
        /// </summary>
        [HttpGet("empresas/{empresaId:int}/documentos")]
        public async Task<IActionResult> ListarDocumentos(int empresaId, [FromQuery] bool? soloActivos)
        {
            using var connection = _connectionFactory.Create();

            // Verificar empresa
            var empresa = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT Id, Nombre, Rnc, DiaCorte FROM Empresas WHERE Id = @EmpresaId",
                new { EmpresaId = empresaId });

            if (empresa == null)
                return NotFound(new { message = "Empresa no encontrada." });

            var whereClause = "WHERE d.EmpresaId = @EmpresaId AND d.Anulado = 0";
            if (soloActivos == true)
                whereClause += " AND d.Estado != 2"; // != Pagado

            var sql = $@"
                SELECT 
                    d.Id,
                    d.NumeroDocumento,
                    d.PeriodoDesde,
                    d.PeriodoHasta,
                    d.CantidadConsumos,
                    d.CantidadEmpleados,
                    d.MontoTotal,
                    d.MontoPagado,
                    d.MontoPendiente,
                    d.Estado,
                    d.FechaEmision,
                    d.FechaVencimiento,
                    CASE WHEN d.FechaVencimiento < GETUTCDATE() 
                         THEN DATEDIFF(DAY, d.FechaVencimiento, GETUTCDATE()) 
                         ELSE 0 END AS DiasVencido
                FROM CxcDocumentos d
                {whereClause}
                ORDER BY d.FechaEmision DESC";

            var rawData = await connection.QueryAsync<dynamic>(sql, new { EmpresaId = empresaId });

            var documentos = rawData.Select(d => new
            {
                Id = (int)d.Id,
                NumeroDocumento = (string)d.NumeroDocumento,
                d.PeriodoDesde,
                d.PeriodoHasta,
                CantidadConsumos = (int)d.CantidadConsumos,
                CantidadEmpleados = (int)d.CantidadEmpleados,

                MontoTotal = (decimal)d.MontoTotal,
                MontoPagado = (decimal)d.MontoPagado,
                MontoPendiente = (decimal)d.MontoPendiente,

                Estado = (int)d.Estado,
                EstadoNombre = ((EstadoCxc)(int)d.Estado).ToString(),
                d.FechaEmision,
                d.FechaVencimiento,
                DiasVencido = (int)d.DiasVencido
            }).ToList();

            return Ok(new
            {
                empresa = new
                {
                    Id = (int)empresa.Id,
                    Nombre = (string)empresa.Nombre,
                    Rnc = (string?)empresa.Rnc,
                    DiaCorte = (int?)empresa.DiaCorte
                },
                documentos,
                resumen = new
                {
                    Total = documentos.Sum(d => d.MontoTotal),
                    Pagado = documentos.Sum(d => d.MontoPagado),
                    Pendiente = documentos.Sum(d => d.MontoPendiente)
                }
            });
        }

        /// <summary>
        /// GET /api/cxc/documentos/{id}
        /// </summary>
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

            // Obtener refinanciamiento activo
            const string sqlRefinanciamiento = @"
                SELECT TOP 1
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

        #endregion

        #region Generar Consolidado

        /// <summary>
        /// GET /api/cxc/empresas/{empresaId}/preview-consolidado
        /// </summary>
        [HttpGet("empresas/{empresaId:int}/preview-consolidado")]
        public async Task<IActionResult> PreviewConsolidado(int empresaId, [FromQuery] DateTime desde, [FromQuery] DateTime hasta)
        {
            using var connection = _connectionFactory.Create();

            var empresa = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT Id, Nombre FROM Empresas WHERE Id = @EmpresaId",
                new { EmpresaId = empresaId });

            if (empresa == null)
                return BadRequest(new { message = "Empresa no encontrada." });

            const string sql = @"
                SELECT 
                    c.Id, c.Fecha,
                    ISNULL(cli.Nombre, 'N/A') AS EmpleadoNombre,
                    ISNULL(cli.Codigo, '') AS EmpleadoCodigo,
                    ISNULL(p.Nombre, 'N/A') AS ProveedorNombre,
                    c.Monto
                FROM Consumos c
                INNER JOIN Clientes cli ON c.ClienteId = cli.Id
                LEFT JOIN Proveedores p ON c.ProveedorId = p.Id
                WHERE cli.EmpresaId = @EmpresaId
                  AND c.Reversado = 0
                  AND c.Fecha >= @Desde
                  AND c.Fecha < @Hasta
                  AND NOT EXISTS (SELECT 1 FROM CxcDocumentoDetalles d WHERE d.ConsumoId = c.Id)
                ORDER BY c.Fecha DESC";

            var consumos = (await connection.QueryAsync<dynamic>(sql, new
            {
                EmpresaId = empresaId,
                Desde = desde,
                Hasta = hasta.AddDays(1)
            })).ToList();

            if (!consumos.Any())
                return BadRequest(new { message = "No hay consumos para consolidar en este período." });

            var empleadosUnicos = consumos.Select(c => (string)c.EmpleadoNombre).Distinct().Count();
            var montoTotal = consumos.Sum(c => (decimal)c.Monto);

            return Ok(new
            {
                Empresa = new { Id = (int)empresa.Id, Nombre = (string)empresa.Nombre },
                PeriodoDesde = desde,
                PeriodoHasta = hasta,
                CantidadConsumos = consumos.Count,
                CantidadEmpleados = empleadosUnicos,
                MontoTotal = montoTotal,
                Consumos = consumos
            });
        }

        /// <summary>
        /// POST /api/cxc/empresas/{empresaId}/consolidar
        /// </summary>
        [HttpPost("empresas/{empresaId:int}/consolidar")]
        public async Task<IActionResult> GenerarConsolidado(int empresaId, [FromBody] GenerarConsolidadoCxcDto dto)
        {
            using var connection = _connectionFactory.Create();
            await connection.OpenAsync();

            var empresa = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT Id, Nombre FROM Empresas WHERE Id = @EmpresaId",
                new { EmpresaId = empresaId });

            if (empresa == null)
                return BadRequest(new { message = "Empresa no encontrada." });

            // Verificar si ya existe consolidado
            var existente = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM CxcDocumentos 
                WHERE EmpresaId = @EmpresaId 
                  AND PeriodoDesde = @PeriodoDesde 
                  AND PeriodoHasta = @PeriodoHasta 
                  AND Anulado = 0",
                new { EmpresaId = empresaId, dto.PeriodoDesde, dto.PeriodoHasta }) > 0;

            if (existente)
                return BadRequest(new { message = "Ya existe un consolidado para este período." });

            // Obtener consumos pendientes
            const string sqlConsumos = @"
                SELECT c.Id, c.Monto, c.ClienteId
                FROM Consumos c
                INNER JOIN Clientes cli ON c.ClienteId = cli.Id
                WHERE cli.EmpresaId = @EmpresaId
                  AND c.Reversado = 0
                  AND c.Fecha >= @PeriodoDesde
                  AND c.Fecha < @PeriodoHasta
                  AND NOT EXISTS (SELECT 1 FROM CxcDocumentoDetalles d WHERE d.ConsumoId = c.Id)";

            var consumos = (await connection.QueryAsync<(int Id, decimal Monto, int ClienteId)>(sqlConsumos, new
            {
                EmpresaId = empresaId,
                dto.PeriodoDesde,
                PeriodoHasta = dto.PeriodoHasta.AddDays(1)
            })).ToList();

            if (!consumos.Any())
                return BadRequest(new { message = "No hay consumos para consolidar." });

            var empleadosUnicos = consumos.Select(c => c.ClienteId).Distinct().Count();
            var montoTotal = consumos.Sum(c => c.Monto);

            using var transaction = connection.BeginTransaction();

            try
            {
                // Generar número de documento
                var año = DateTime.Now.Year;
                var ultimoDoc = await connection.ExecuteScalarAsync<string?>(@"
                    SELECT TOP 1 NumeroDocumento FROM CxcDocumentos 
                    WHERE NumeroDocumento LIKE @Pattern ORDER BY Id DESC",
                    new { Pattern = $"CXC-{año}-%" }, transaction);

                int secuencial = 1;
                if (ultimoDoc != null)
                {
                    var partes = ultimoDoc.Split('-');
                    if (partes.Length == 3 && int.TryParse(partes[2], out int num))
                        secuencial = num + 1;
                }
                var numeroDocumento = $"CXC-{año}-{secuencial:00000}";

                // Crear documento
                const string sqlInsertDoc = @"
                    INSERT INTO CxcDocumentos 
                        (NumeroDocumento, EmpresaId, PeriodoDesde, PeriodoHasta,
                         MontoTotal, MontoPendiente, CantidadConsumos, CantidadEmpleados,
                         FechaEmision, FechaVencimiento, Estado, CreadoPorUsuarioId, CreadoUtc, Anulado, Refinanciado)
                    OUTPUT INSERTED.Id
                    VALUES 
                        (@NumeroDocumento, @EmpresaId, @PeriodoDesde, @PeriodoHasta,
                         @MontoTotal, @MontoPendiente, @CantidadConsumos, @CantidadEmpleados,
                         @FechaEmision, @FechaVencimiento, @Estado, @UsuarioId, @CreadoUtc, 0, 0)";

                var documentoId = await connection.ExecuteScalarAsync<int>(sqlInsertDoc, new
                {
                    NumeroDocumento = numeroDocumento,
                    EmpresaId = empresaId,
                    dto.PeriodoDesde,
                    dto.PeriodoHasta,
                    MontoTotal = montoTotal,
                    MontoPendiente = montoTotal,
                    CantidadConsumos = consumos.Count,
                    CantidadEmpleados = empleadosUnicos,
                    FechaEmision = DateTime.UtcNow,
                    FechaVencimiento = DateTime.UtcNow.AddDays(dto.DiasParaPagar ?? 30),
                    Estado = (int)EstadoCxc.Pendiente,
                    UsuarioId = _user.Id,
                    CreadoUtc = DateTime.UtcNow
                }, transaction);

                // Insertar detalles
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
                    CantidadConsumos = consumos.Count,
                    CantidadEmpleados = empleadosUnicos,
                    FechaVencimiento = DateTime.UtcNow.AddDays(dto.DiasParaPagar ?? 30),
                    mensaje = "Consolidado CxC generado exitosamente."
                });
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        #endregion

        #region Registrar Cobro

        /// <summary>
        /// POST /api/cxc/documentos/{id}/cobros
        /// </summary>
        [HttpPost("documentos/{id:int}/cobros")]
        public async Task<IActionResult> RegistrarCobro(int id, [FromBody] RegistrarCobroCxcDto dto)
        {
            using var connection = _connectionFactory.Create();
            await connection.OpenAsync();

            // Obtener documento
            const string sqlDoc = @"
                SELECT Id, EmpresaId, MontoTotal, MontoPagado, MontoPendiente, Estado, Anulado
                FROM CxcDocumentos WHERE Id = @Id";

            var doc = await connection.QueryFirstOrDefaultAsync<dynamic>(sqlDoc, new { Id = id });

            if (doc == null || (bool)doc.Anulado)
                return NotFound(new { message = "Documento no encontrado." });

            if ((int)doc.Estado == (int)EstadoCxc.Pagado)
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
                    SELECT TOP 1 NumeroRecibo FROM CxcPagos 
                    WHERE NumeroRecibo LIKE @Pattern ORDER BY Id DESC",
                    new { Pattern = $"COB-{año}-%" }, transaction);

                int secuencial = 1;
                if (ultimoPago != null)
                {
                    var partes = ultimoPago.Split('-');
                    if (partes.Length == 3 && int.TryParse(partes[2], out int num))
                        secuencial = num + 1;
                }
                var numeroComprobante = $"COB-{año}-{secuencial:00000}";

                // Insertar pago
                const string sqlInsertPago = @"
                    INSERT INTO CxcPagos 
                        (CxcDocumentoId, NumeroRecibo, Fecha, Monto, MetodoPago, 
                         Referencia, Banco, Notas, Anulado, RegistradoPorUsuarioId, CreadoUtc)
                    OUTPUT INSERTED.Id
                    VALUES 
                        (@DocumentoId, @NumeroRecibo, @Fecha, @Monto, @MetodoPago,
                         @Referencia, @Banco, @Notas, 0, @UsuarioId, @CreadoUtc)";

                var pagoId = await connection.ExecuteScalarAsync<int>(sqlInsertPago, new
                {
                    DocumentoId = id,
                    NumeroRecibo = numeroComprobante,
                    Fecha = DateTime.UtcNow,
                    dto.Monto,
                    MetodoPago = (int)dto.MetodoPago,
                    dto.Referencia,
                    Banco = dto.BancoOrigen,
                    dto.Notas,
                    UsuarioId = _user.Id,
                    CreadoUtc = DateTime.UtcNow
                }, transaction);

                // Actualizar documento
                decimal nuevoMontoPagado = (decimal)doc.MontoPagado + dto.Monto;
                decimal nuevoMontoPendiente = montoPendiente - dto.Monto;

                EstadoCxc nuevoEstado = nuevoMontoPendiente <= 0
                    ? EstadoCxc.Pagado
                    : EstadoCxc.ParcialmentePagado;

                if (nuevoMontoPendiente < 0) nuevoMontoPendiente = 0;

                const string sqlUpdateDoc = @"
                    UPDATE CxcDocumentos 
                    SET MontoPagado = @MontoPagado, MontoPendiente = @MontoPendiente, Estado = @Estado
                    WHERE Id = @Id";

                await connection.ExecuteAsync(sqlUpdateDoc, new
                {
                    MontoPagado = nuevoMontoPagado,
                    MontoPendiente = nuevoMontoPendiente,
                    Estado = (int)nuevoEstado,
                    Id = id
                }, transaction);

                // Si está pagado completamente, restaurar crédito de empleados
                if (nuevoEstado == EstadoCxc.Pagado)
                {
                    await RestaurarCreditoEmpleados(connection, transaction, id);
                }

                transaction.Commit();

                return Ok(new
                {
                    Id = pagoId,
                    NumeroComprobante = numeroComprobante,
                    Monto = dto.Monto,
                    DocumentoNuevoSaldo = nuevoMontoPendiente,
                    DocumentoEstado = nuevoEstado.ToString(),
                    mensaje = nuevoEstado == EstadoCxc.Pagado
                        ? "Documento pagado completamente. Crédito de empleados restaurado."
                        : "Pago registrado exitosamente."
                });
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private async Task RestaurarCreditoEmpleados(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction transaction,
            int documentoId)
        {
            const string sql = @"
                WITH MontosPorCliente AS (
                    SELECT c.ClienteId, SUM(det.Monto) AS Monto
                    FROM CxcDocumentoDetalles det
                    INNER JOIN Consumos c ON det.ConsumoId = c.Id
                    WHERE det.CxcDocumentoId = @DocumentoId
                    GROUP BY c.ClienteId
                )
                UPDATE cli
                SET cli.Saldo = cli.Saldo + m.Monto
                FROM Clientes cli
                INNER JOIN MontosPorCliente m ON cli.Id = m.ClienteId";

            await connection.ExecuteAsync(sql, new { DocumentoId = documentoId }, transaction);
        }

        #endregion

        #region Historial de Cobros

        /// <summary>
        /// GET /api/cxc/cobros
        /// </summary>
        [HttpGet("cobros")]
        public async Task<IActionResult> HistorialCobros(
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
                whereClause += " AND p.Fecha < @Hasta";
                parameters.Add("Hasta", hasta.Value.AddDays(1));
            }

            // Contar y sumar
            var resumenSql = $@"
                SELECT COUNT(*) AS Total, ISNULL(SUM(p.Monto), 0) AS TotalMonto
                FROM CxcPagos p
                INNER JOIN CxcDocumentos d ON p.CxcDocumentoId = d.Id
                {whereClause}";

            var resumen = await connection.QueryFirstAsync<dynamic>(resumenSql, parameters);
            int total = resumen.Total;
            decimal totalMonto = resumen.TotalMonto;

            // Obtener datos paginados
            parameters.Add("Offset", (page - 1) * pageSize);
            parameters.Add("PageSize", pageSize);

            var dataSql = $@"
                SELECT 
                    p.Id, p.NumeroRecibo AS NumeroComprobante, p.Fecha, p.Monto,
                    p.MetodoPago, p.Referencia,
                    d.Id AS DocumentoId, d.NumeroDocumento AS DocumentoNumero,
                    e.Nombre AS EmpresaNombre
                FROM CxcPagos p
                INNER JOIN CxcDocumentos d ON p.CxcDocumentoId = d.Id
                INNER JOIN Empresas e ON d.EmpresaId = e.Id
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
                p.EmpresaNombre
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

    public class GenerarConsolidadoCxcDto
    {
        public DateTime PeriodoDesde { get; set; }
        public DateTime PeriodoHasta { get; set; }
        public int? DiasParaPagar { get; set; }
    }

    public class RegistrarCobroCxcDto
    {
        public decimal Monto { get; set; }
        public MetodoPago MetodoPago { get; set; }
        public string? Referencia { get; set; }
        public string? BancoOrigen { get; set; }
        public string? Notas { get; set; }
    }

    #endregion

    public class EmpresaCxcDto
    {
        public int EmpresaId { get; set; }
        public string EmpresaNombre { get; set; } = string.Empty;
        public int? DiaCorte { get; set; }
        public int DocumentosPendientes { get; set; }
        public decimal TotalPorCobrar { get; set; }
        public decimal TotalRefinanciado { get; set; }
        public decimal Vencido { get; set; }
    }
}