// Controllers/CierresCajaController.cs
// Sistema de Cierre de Caja por Usuario/Cajero
// Migrado a Dapper

using Dapper;
using Consumo_App.Data.Sql;
using Consumo_App.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/cierres-caja")]
    [Authorize]
    public class CierresCajaController : ControllerBase
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly IUserContext _user;

        public CierresCajaController(SqlConnectionFactory connectionFactory, IUserContext user)
        {
            _connectionFactory = connectionFactory;
            _user = user;
        }

        #region Verificar Estado de Cierre

        /// <summary>
        /// Verificar si el usuario puede registrar consumos (si no ha cerrado la caja hoy)
        /// GET /api/cierres-caja/puede-registrar?cajaId=X
        /// </summary>
        [HttpGet("puede-registrar")]
        public async Task<IActionResult> PuedeRegistrar([FromQuery] int cajaId)
        {
            if (cajaId <= 0)
                return BadRequest(new { message = "Debe especificar una caja válida." });

            using var connection = _connectionFactory.Create();

            var hoy = DateTime.UtcNow.Date;
            var usuarioId = _user.Id;

            const string sql = @"
                SELECT CerradoUtc
                FROM CierresCaja 
                WHERE UsuarioId = @UsuarioId 
                  AND CajaId = @CajaId 
                  AND FechaCierre = @Fecha";

            var fechaCierre = await connection.QueryFirstOrDefaultAsync<DateTime?>(
                sql, new { UsuarioId = usuarioId, CajaId = cajaId, Fecha = hoy });

            var cajaCerrada = fechaCierre.HasValue;

            return Ok(new
            {
                PuedeRegistrar = !cajaCerrada,
                CajaCerrada = cajaCerrada,
                FechaCierre = fechaCierre,
                Motivo = cajaCerrada ? "La caja ya fue cerrada para el día de hoy" : null,
                Fecha = hoy,
                UsuarioId = usuarioId,
                CajaId = cajaId
            });
        }

        #endregion

        #region Resumen del Día (Pre-Cierre)

        /// <summary>
        /// Obtener resumen del día antes de cerrar
        /// GET /api/cierres-caja/resumen-dia?cajaId=X
        /// </summary>
        [HttpGet("resumen-dia")]
        public async Task<IActionResult> ResumenDia([FromQuery] int cajaId)
        {
            if (cajaId <= 0)
                return BadRequest(new { message = "Debe especificar una caja válida." });

            using var connection = _connectionFactory.Create();

            var hoy = DateTime.UtcNow.Date;
            var usuarioId = _user.Id;

            // Verificar si ya está cerrada
            var yaCerrada = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(*) FROM CierresCaja 
                WHERE UsuarioId = @UsuarioId AND CajaId = @CajaId AND FechaCierre = @Fecha",
                new { UsuarioId = usuarioId, CajaId = cajaId, Fecha = hoy }) > 0;

            if (yaCerrada)
                return BadRequest(new { message = "La caja ya fue cerrada para hoy." });

            // Obtener resumen de consumos del día
            const string sqlResumen = @"
                SELECT 
                    COUNT(*) AS TotalConsumos,
                    SUM(CASE WHEN Reversado = 1 THEN 1 ELSE 0 END) AS TotalReversos,
                    ISNULL(SUM(CASE WHEN Reversado = 0 THEN Monto ELSE 0 END), 0) AS MontoConsumos,
                    ISNULL(SUM(CASE WHEN Reversado = 1 THEN Monto ELSE 0 END), 0) AS MontoReversos
                FROM Consumos
                WHERE UsuarioRegistradorId = @UsuarioId
                  AND CajaId = @CajaId
                  AND CAST(Fecha AS DATE) = @Fecha";

            var resumen = await connection.QueryFirstAsync<dynamic>(
                sqlResumen, new { UsuarioId = usuarioId, CajaId = cajaId, Fecha = hoy });

            // Obtener lista de consumos del día
            const string sqlConsumos = @"
                SELECT 
                    c.Id,
                    c.Fecha,
                    cl.Nombre AS ClienteNombre,
                    cl.Cedula AS ClienteCedula,
                    e.Nombre AS EmpresaNombre,
                    c.Monto,
                    c.Concepto,
                    c.Reversado,
                    c.ReversadoUtc
                FROM Consumos c
                INNER JOIN Clientes cl ON c.ClienteId = cl.Id
                INNER JOIN Empresas e ON c.EmpresaId = e.Id
                WHERE c.UsuarioRegistradorId = @UsuarioId
                  AND c.CajaId = @CajaId
                  AND CAST(c.Fecha AS DATE) = @Fecha
                ORDER BY c.Fecha DESC";

            var consumos = await connection.QueryAsync<dynamic>(
                sqlConsumos, new { UsuarioId = usuarioId, CajaId = cajaId, Fecha = hoy });

            return Ok(new
            {
                Fecha = hoy,
                UsuarioId = usuarioId,
                CajaId = cajaId,
                Resumen = new
                {
                    TotalConsumos = (int)resumen.TotalConsumos,
                    TotalReversos = (int)resumen.TotalReversos,
                    ConsumosNetos = (int)resumen.TotalConsumos - (int)resumen.TotalReversos,
                    MontoConsumos = (decimal)resumen.MontoConsumos,
                    MontoReversos = (decimal)resumen.MontoReversos,
                    MontoNeto = (decimal)resumen.MontoConsumos - (decimal)resumen.MontoReversos
                },
                Consumos = consumos,
                PuedeCerrar = true
            });
        }

        #endregion

        #region Cerrar Caja

        /// <summary>
        /// Cerrar la caja del día actual
        /// POST /api/cierres-caja/cerrar
        /// </summary>
        [HttpPost("cerrar")]
        public async Task<IActionResult> CerrarCaja([FromBody] CerrarCajaRequest request)
        {
            if (request.CajaId <= 0 || request.ProveedorId <= 0 || request.TiendaId <= 0)
                return BadRequest(new { message = "Debe especificar CajaId, ProveedorId y TiendaId." });

            using var connection = _connectionFactory.Create();
            await connection.OpenAsync();

            var hoy = DateTime.UtcNow.Date;
            var usuarioId = _user.Id;

            // Verificar si ya está cerrada
            var yaCerrada = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(*) FROM CierresCaja 
                WHERE UsuarioId = @UsuarioId AND CajaId = @CajaId AND FechaCierre = @Fecha",
                new { UsuarioId = usuarioId, CajaId = request.CajaId, Fecha = hoy }) > 0;

            if (yaCerrada)
                return BadRequest(new { message = "La caja ya fue cerrada para el día de hoy. No se puede cerrar nuevamente." });

            // Calcular totales del día
            const string sqlTotales = @"
                SELECT 
                    COUNT(*) AS TotalConsumos,
                    SUM(CASE WHEN Reversado = 1 THEN 1 ELSE 0 END) AS TotalReversos,
                    ISNULL(SUM(CASE WHEN Reversado = 0 THEN Monto ELSE 0 END), 0) AS MontoConsumos,
                    ISNULL(SUM(CASE WHEN Reversado = 1 THEN Monto ELSE 0 END), 0) AS MontoReversos
                FROM Consumos
                WHERE UsuarioRegistradorId = @UsuarioId
                  AND CajaId = @CajaId
                  AND CAST(Fecha AS DATE) = @Fecha";

            var totales = await connection.QueryFirstAsync<dynamic>(
                sqlTotales, new { UsuarioId = usuarioId, CajaId = request.CajaId, Fecha = hoy });

            int totalConsumos = totales.TotalConsumos;
            int totalReversos = totales.TotalReversos;
            decimal montoConsumos = totales.MontoConsumos;
            decimal montoReversos = totales.MontoReversos;
            decimal montoNeto = montoConsumos - montoReversos;

            // Insertar cierre
            const string sqlInsert = @"
                INSERT INTO CierresCaja (
                    UsuarioId, ProveedorId, TiendaId, CajaId, FechaCierre, CerradoUtc,
                    TotalConsumos, TotalReversos, MontoConsumos, MontoReversos, MontoNeto,
                    TipoCierre, Observaciones
                )
                OUTPUT INSERTED.Id
                VALUES (
                    @UsuarioId, @ProveedorId, @TiendaId, @CajaId, @Fecha, GETUTCDATE(),
                    @TotalConsumos, @TotalReversos, @MontoConsumos, @MontoReversos, @MontoNeto,
                    'MANUAL', @Observaciones
                )";

            var cierreId = await connection.ExecuteScalarAsync<int>(sqlInsert, new
            {
                UsuarioId = usuarioId,
                request.ProveedorId,
                request.TiendaId,
                request.CajaId,
                Fecha = hoy,
                TotalConsumos = totalConsumos,
                TotalReversos = totalReversos,
                MontoConsumos = montoConsumos,
                MontoReversos = montoReversos,
                MontoNeto = montoNeto,
                Observaciones = string.IsNullOrEmpty(request.Observaciones) ? (string?)null : request.Observaciones
            });

            return Ok(new
            {
                Success = true,
                Message = "Caja cerrada exitosamente. No podrá registrar más consumos el día de hoy.",
                CierreId = cierreId,
                Resumen = new
                {
                    Fecha = hoy,
                    TotalConsumos = totalConsumos,
                    TotalReversos = totalReversos,
                    MontoConsumos = montoConsumos,
                    MontoReversos = montoReversos,
                    MontoNeto = montoNeto
                }
            });
        }

        public class CerrarCajaRequest
        {
            public int CajaId { get; set; }
            public int ProveedorId { get; set; }
            public int TiendaId { get; set; }
            public string? Observaciones { get; set; }
        }

        #endregion

        #region Listado de Cierres (Admin)

        /// <summary>
        /// Listar cierres de caja con filtros (para admin)
        /// GET /api/cierres-caja/lista
        /// </summary>
        [HttpGet("lista")]
        public async Task<IActionResult> ListarCierres(
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta,
            [FromQuery] int? proveedorId,
            [FromQuery] int? tiendaId,
            [FromQuery] int? cajaId,
            [FromQuery] int? usuarioId,
            [FromQuery] string? tipoCierre,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            using var connection = _connectionFactory.Create();

            var whereClause = "WHERE 1=1";
            var parameters = new DynamicParameters();

            if (desde.HasValue)
            {
                whereClause += " AND cc.FechaCierre >= @Desde";
                parameters.Add("Desde", desde.Value);
            }

            if (hasta.HasValue)
            {
                whereClause += " AND cc.FechaCierre <= @Hasta";
                parameters.Add("Hasta", hasta.Value);
            }

            if (proveedorId.HasValue)
            {
                whereClause += " AND cc.ProveedorId = @ProveedorId";
                parameters.Add("ProveedorId", proveedorId.Value);
            }

            if (tiendaId.HasValue)
            {
                whereClause += " AND cc.TiendaId = @TiendaId";
                parameters.Add("TiendaId", tiendaId.Value);
            }

            if (cajaId.HasValue)
            {
                whereClause += " AND cc.CajaId = @CajaId";
                parameters.Add("CajaId", cajaId.Value);
            }

            if (usuarioId.HasValue)
            {
                whereClause += " AND cc.UsuarioId = @UsuarioId";
                parameters.Add("UsuarioId", usuarioId.Value);
            }

            if (!string.IsNullOrEmpty(tipoCierre))
            {
                whereClause += " AND cc.TipoCierre = @TipoCierre";
                parameters.Add("TipoCierre", tipoCierre);
            }

            // Contar y sumar
            var resumenSql = $@"
                SELECT 
                    COUNT(*) AS Total,
                    ISNULL(SUM(cc.MontoNeto), 0) AS TotalMontoNeto
                FROM CierresCaja cc
                {whereClause}";

            var resumen = await connection.QueryFirstAsync<dynamic>(resumenSql, parameters);
            int totalCount = resumen.Total;
            decimal totalMontoNeto = resumen.TotalMontoNeto;

            // Obtener datos paginados
            parameters.Add("Offset", (page - 1) * pageSize);
            parameters.Add("PageSize", pageSize);

            var dataSql = $@"
                SELECT 
                    cc.Id,
                    cc.UsuarioId,
                    u.Nombre AS UsuarioNombre,
                    cc.ProveedorId,
                    p.Nombre AS ProveedorNombre,
                    cc.TiendaId,
                    t.Nombre AS TiendaNombre,
                    cc.CajaId,
                    c.Nombre AS CajaNombre,
                    cc.FechaCierre,
                    cc.CerradoUtc,
                    cc.TotalConsumos,
                    cc.TotalReversos,
                    cc.MontoConsumos,
                    cc.MontoReversos,
                    cc.MontoNeto,
                    cc.TipoCierre,
                    cc.Observaciones
                FROM CierresCaja cc
                INNER JOIN Usuarios u ON cc.UsuarioId = u.Id
                INNER JOIN Proveedores p ON cc.ProveedorId = p.Id
                INNER JOIN ProveedorTiendas t ON cc.TiendaId = t.Id
                INNER JOIN ProveedorCajas c ON cc.CajaId = c.Id
                {whereClause}
                ORDER BY cc.FechaCierre DESC, cc.CerradoUtc DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var data = await connection.QueryAsync<dynamic>(dataSql, parameters);

            return Ok(new
            {
                Data = data,
                Resumen = new
                {
                    TotalCierres = totalCount,
                    TotalMontoNeto = totalMontoNeto
                },
                Pagination = new
                {
                    Total = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
                }
            });
        }

        #endregion

        #region Detalle de Cierre

        /// <summary>
        /// Obtener detalle de un cierre específico con todos sus consumos
        /// GET /api/cierres-caja/{id}/detalle
        /// </summary>
        [HttpGet("{id:int}/detalle")]
        public async Task<IActionResult> DetalleCierre(int id)
        {
            using var connection = _connectionFactory.Create();

            // Obtener datos del cierre
            const string sqlCierre = @"
                SELECT 
                    cc.Id,
                    cc.UsuarioId,
                    u.Nombre AS UsuarioNombre,
                    cc.ProveedorId,
                    p.Nombre AS ProveedorNombre,
                    cc.TiendaId,
                    t.Nombre AS TiendaNombre,
                    cc.CajaId,
                    c.Nombre AS CajaNombre,
                    cc.FechaCierre,
                    cc.CerradoUtc,
                    cc.TotalConsumos,
                    cc.TotalReversos,
                    cc.MontoConsumos,
                    cc.MontoReversos,
                    cc.MontoNeto,
                    cc.TipoCierre,
                    cc.Observaciones
                FROM CierresCaja cc
                INNER JOIN Usuarios u ON cc.UsuarioId = u.Id
                INNER JOIN Proveedores p ON cc.ProveedorId = p.Id
                INNER JOIN ProveedorTiendas t ON cc.TiendaId = t.Id
                INNER JOIN ProveedorCajas c ON cc.CajaId = c.Id
                WHERE cc.Id = @Id";

            var cierre = await connection.QueryFirstOrDefaultAsync<dynamic>(sqlCierre, new { Id = id });

            if (cierre == null)
                return NotFound(new { message = "Cierre no encontrado." });

            // Obtener consumos del cierre
            const string sqlConsumos = @"
                SELECT 
                    c.Id,
                    c.Fecha,
                    c.ClienteId,
                    cl.Nombre AS ClienteNombre,
                    cl.Cedula AS ClienteCedula,
                    c.EmpresaId,
                    e.Nombre AS EmpresaNombre,
                    c.Monto,
                    c.Concepto,
                    c.Reversado,
                    c.ReversadoUtc
                FROM Consumos c
                INNER JOIN Clientes cl ON c.ClienteId = cl.Id
                INNER JOIN Empresas e ON c.EmpresaId = e.Id
                WHERE c.UsuarioRegistradorId = @UsuarioId
                  AND c.CajaId = @CajaId
                  AND CAST(c.Fecha AS DATE) = @Fecha
                ORDER BY c.Fecha ASC";

            var consumos = await connection.QueryAsync<dynamic>(sqlConsumos, new
            {
                UsuarioId = (int)cierre.UsuarioId,
                CajaId = (int)cierre.CajaId,
                Fecha = (DateTime)cierre.FechaCierre
            });

            return Ok(new
            {
                Cierre = cierre,
                Consumos = consumos
            });
        }

        #endregion
    }
}