using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Security.Claims;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "backoffice")]  // Solo usuarios con rol "backoffice" pueden acceder
    public class BackofficeController : ControllerBase
    {
        private readonly string _connectionString;

        public BackofficeController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")!;
        }

        // =====================================================
        // METODO HELPER: Obtener UsuarioId del Token JWT
        // =====================================================
        private int GetUsuarioIdFromToken()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out var userId))
                return userId;
            return 0;
        }

        // =====================================================
        // OBTENER DATOS DEL PROVEEDOR DEL USUARIO LOGUEADO
        // GET /api/backoffice/mi-proveedor
        // =====================================================
        [HttpGet("mi-proveedor")]
        public async Task<IActionResult> ObtenerMiProveedor()
        {
            var usuarioId = GetUsuarioIdFromToken();
            if (usuarioId == 0)
                return Unauthorized(new { message = "Token invalido" });

            var sql = @"
                SELECT 
                    p.Id,
                    p.Nombre,
                    p.Rnc,
                    p.Telefono,
                    p.Email,
                    p.Direccion,
                    p.Contacto,
                    p.DiasCorte,
                    p.PorcentajeComision,
                    p.Activo
                FROM Proveedores p
                INNER JOIN UsuarioProveedor up ON p.Rnc = up.RncProveedor
                WHERE up.UsuarioId = @UsuarioId AND p.Activo = 1";

            using var conn = new SqlConnection(_connectionString);
            var proveedor = await conn.QueryFirstOrDefaultAsync<ProveedorBackofficeDto>(sql, new { UsuarioId = usuarioId });

            if (proveedor == null)
                return NotFound(new { message = "No se encontro proveedor asociado a este usuario" });

            return Ok(proveedor);
        }

        // =====================================================
        // DASHBOARD RESUMEN
        // GET /api/backoffice/dashboard
        // =====================================================
        [HttpGet("dashboard")]
        public async Task<IActionResult> ObtenerDashboard()
        {
            var usuarioId = GetUsuarioIdFromToken();
            if (usuarioId == 0)
                return Unauthorized(new { message = "Token invalido" });

            using var conn = new SqlConnection(_connectionString);

            // Obtener ProveedorId del usuario
            var proveedorSql = @"
                SELECT p.Id 
                FROM Proveedores p
                INNER JOIN UsuarioProveedor up ON p.Rnc = up.RncProveedor
                WHERE up.UsuarioId = @UsuarioId AND p.Activo = 1";

            var proveedorId = await conn.QueryFirstOrDefaultAsync<int?>(proveedorSql, new { UsuarioId = usuarioId });

            if (proveedorId == null)
                return NotFound(new { message = "No se encontro proveedor asociado" });

            // Estadisticas
            var statsSql = @"
                -- Total tiendas
                SELECT COUNT(*) FROM ProveedorTiendas WHERE ProveedorId = @ProveedorId AND Activo = 1;
                
                -- Total cajas
                SELECT COUNT(*) FROM ProveedorCajas pc
                INNER JOIN ProveedorTiendas pt ON pc.TiendaId = pt.Id
                WHERE pt.ProveedorId = @ProveedorId AND pc.Activo = 1;
                
                -- Total cajeros/usuarios asignados
                SELECT COUNT(DISTINCT pa.UsuarioId) FROM ProveedorAsignaciones pa
                INNER JOIN ProveedorTiendas pt ON pa.TiendaId = pt.Id
                WHERE pt.ProveedorId = @ProveedorId;
                
                -- Consumos del mes actual
                SELECT ISNULL(SUM(Monto), 0) FROM Consumos 
                WHERE ProveedorId = @ProveedorId 
                AND Reversado = 0
                AND MONTH(Fecha) = MONTH(GETDATE()) AND YEAR(Fecha) = YEAR(GETDATE());
                
                -- Consumos de hoy
                SELECT ISNULL(SUM(Monto), 0) FROM Consumos 
                WHERE ProveedorId = @ProveedorId 
                AND Reversado = 0
                AND CAST(Fecha AS DATE) = CAST(GETDATE() AS DATE);
                
                -- Total pendiente por cobrar (CxP)
                SELECT ISNULL(SUM(MontoPendiente), 0) FROM CxpDocumentos 
                WHERE ProveedorId = @ProveedorId AND Anulado = 0 AND MontoPendiente > 0;
                
                -- Cantidad de documentos pendientes
                SELECT COUNT(*) FROM CxpDocumentos 
                WHERE ProveedorId = @ProveedorId AND Anulado = 0 AND MontoPendiente > 0;";

            using var multi = await conn.QueryMultipleAsync(statsSql, new { ProveedorId = proveedorId });

            var dashboard = new
            {
                proveedorId,
                totalTiendas = await multi.ReadFirstAsync<int>(),
                totalCajas = await multi.ReadFirstAsync<int>(),
                totalCajeros = await multi.ReadFirstAsync<int>(),
                consumosMes = await multi.ReadFirstAsync<decimal>(),
                consumosHoy = await multi.ReadFirstAsync<decimal>(),
                montoPendienteCobro = await multi.ReadFirstAsync<decimal>(),
                documentosPendientes = await multi.ReadFirstAsync<int>()
            };

            return Ok(dashboard);
        }

        // =====================================================
        // MIS TIENDAS
        // GET /api/backoffice/tiendas
        // =====================================================
        [HttpGet("tiendas")]
        public async Task<IActionResult> ObtenerTiendas()
        {
            var usuarioId = GetUsuarioIdFromToken();
            if (usuarioId == 0)
                return Unauthorized(new { message = "Token invalido" });

            var sql = @"
                SELECT 
                    t.Id,
                    t.Nombre,
                    t.Activo,
                    (SELECT COUNT(*) FROM ProveedorCajas WHERE TiendaId = t.Id AND Activo = 1) AS TotalCajas,
                    (SELECT COUNT(DISTINCT pa.UsuarioId) FROM ProveedorAsignaciones pa WHERE pa.TiendaId = t.Id) AS TotalCajeros,
                    (SELECT ISNULL(SUM(c.Monto), 0) FROM Consumos c WHERE c.TiendaId = t.Id AND c.Reversado = 0 
                     AND MONTH(c.Fecha) = MONTH(GETDATE()) AND YEAR(c.Fecha) = YEAR(GETDATE())) AS ConsumosMes
                FROM ProveedorTiendas t
                INNER JOIN Proveedores p ON t.ProveedorId = p.Id
                INNER JOIN UsuarioProveedor up ON p.Rnc = up.RncProveedor
                WHERE up.UsuarioId = @UsuarioId AND t.Activo = 1
                ORDER BY t.Nombre";

            using var conn = new SqlConnection(_connectionString);
            var tiendas = await conn.QueryAsync<TiendaBackofficeDto>(sql, new { UsuarioId = usuarioId });

            return Ok(tiendas);
        }

        // =====================================================
        // MIS CAJEROS
        // GET /api/backoffice/cajeros
        // =====================================================
        [HttpGet("cajeros")]
        public async Task<IActionResult> ObtenerCajeros()
        {
            var usuarioId = GetUsuarioIdFromToken();
            if (usuarioId == 0)
                return Unauthorized(new { message = "Token invalido" });

            var sql = @"
                SELECT DISTINCT
                    u.Id,
                    u.Nombre,
                    u.Activo,
                    t.Nombre AS TiendaNombre,
                    ca.Nombre AS CajaNombre,
                    (SELECT COUNT(*) FROM Consumos c WHERE c.UsuarioRegistradorId = u.Id AND c.Reversado = 0
                     AND CAST(c.Fecha AS DATE) = CAST(GETDATE() AS DATE)) AS ConsumosHoy,
                    (SELECT ISNULL(SUM(c.Monto), 0) FROM Consumos c WHERE c.UsuarioRegistradorId = u.Id AND c.Reversado = 0
                     AND CAST(c.Fecha AS DATE) = CAST(GETDATE() AS DATE)) AS MontoHoy,
                    u.UltimoLoginUtc
                FROM Usuarios u
                INNER JOIN ProveedorAsignaciones pa ON u.Id = pa.UsuarioId
                INNER JOIN ProveedorTiendas t ON pa.TiendaId = t.Id
                LEFT JOIN ProveedorCajas ca ON pa.CajaId = ca.Id
                INNER JOIN Proveedores p ON t.ProveedorId = p.Id
                INNER JOIN UsuarioProveedor up ON p.Rnc = up.RncProveedor
                WHERE up.UsuarioId = @UsuarioId
                ORDER BY t.Nombre, u.Nombre";

            using var conn = new SqlConnection(_connectionString);
            var cajeros = await conn.QueryAsync<CajeroBackofficeDto>(sql, new { UsuarioId = usuarioId });

            return Ok(cajeros);
        }

        // =====================================================
        // MIS DOCUMENTOS CXP
        // GET /api/backoffice/documentos-cxp
        // =====================================================
        [HttpGet("documentos-cxp")]
        public async Task<IActionResult> ObtenerDocumentosCxp()
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
                    d.MontoBruto,
                    d.MontoComision,
                    d.MontoTotal,
                    d.MontoPagado,
                    d.MontoPendiente,
                    d.Estado,
                    DATEDIFF(DAY, d.FechaVencimiento, GETDATE()) AS DiasVencido
                FROM CxpDocumentos d
                INNER JOIN Proveedores p ON d.ProveedorId = p.Id
                INNER JOIN UsuarioProveedor up ON p.Rnc = up.RncProveedor
                WHERE up.UsuarioId = @UsuarioId AND d.Anulado = 0
                ORDER BY d.FechaVencimiento DESC";

            using var conn = new SqlConnection(_connectionString);
            var documentos = await conn.QueryAsync<DocumentoCxpBackofficeDto>(sql, new { UsuarioId = usuarioId });

            return Ok(documentos);
        }

        // =====================================================
        // REPORTE CONSUMOS POR CAJERO
        // GET /api/backoffice/reporte/por-cajero?desde=2024-01-01&hasta=2024-12-31
        // =====================================================
        [HttpGet("reporte/por-cajero")]
        public async Task<IActionResult> ReportePorCajero(
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
                    u.Id AS CajeroId,
                    u.Nombre AS CajeroNombre,
                    COUNT(*) AS TotalConsumos,
                    SUM(c.Monto) AS MontoTotal,
                    AVG(c.Monto) AS PromedioConsumo,
                    MIN(c.Fecha) AS PrimerConsumo,
                    MAX(c.Fecha) AS UltimoConsumo
                FROM Consumos c
                INNER JOIN Usuarios u ON c.UsuarioRegistradorId = u.Id
                INNER JOIN Proveedores p ON c.ProveedorId = p.Id
                INNER JOIN UsuarioProveedor up ON p.Rnc = up.RncProveedor
                WHERE up.UsuarioId = @UsuarioId 
                AND c.Reversado = 0
                AND c.Fecha >= @FechaDesde AND c.Fecha < @FechaHasta
                GROUP BY u.Id, u.Nombre
                ORDER BY MontoTotal DESC";

            using var conn = new SqlConnection(_connectionString);
            var reporte = await conn.QueryAsync<ReporteCajeroDto>(sql, new
            {
                UsuarioId = usuarioId,
                FechaDesde = fechaDesde,
                FechaHasta = fechaHasta
            });

            var resumen = new
            {
                totalCajeros = reporte.Count(),
                totalConsumos = reporte.Sum(r => r.TotalConsumos),
                montoTotal = reporte.Sum(r => r.MontoTotal)
            };

            return Ok(new { data = reporte, resumen, fechaDesde, fechaHasta = fechaHasta.AddDays(-1) });
        }

        // =====================================================
        // REPORTE CONSUMOS POR TIENDA
        // GET /api/backoffice/reporte/por-tienda?desde=2024-01-01&hasta=2024-12-31
        // =====================================================
        [HttpGet("reporte/por-tienda")]
        public async Task<IActionResult> ReportePorTienda(
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
                    t.Id AS TiendaId,
                    t.Nombre AS TiendaNombre,
                    COUNT(*) AS TotalConsumos,
                    SUM(c.Monto) AS MontoTotal,
                    AVG(c.Monto) AS PromedioConsumo,
                    COUNT(DISTINCT c.ClienteId) AS ClientesAtendidos,
                    COUNT(DISTINCT c.UsuarioRegistradorId) AS CajerosActivos
                FROM Consumos c
                INNER JOIN ProveedorTiendas t ON c.TiendaId = t.Id
                INNER JOIN Proveedores p ON c.ProveedorId = p.Id
                INNER JOIN UsuarioProveedor up ON p.Rnc = up.RncProveedor
                WHERE up.UsuarioId = @UsuarioId 
                AND c.Reversado = 0
                AND c.Fecha >= @FechaDesde AND c.Fecha < @FechaHasta
                GROUP BY t.Id, t.Nombre
                ORDER BY MontoTotal DESC";

            using var conn = new SqlConnection(_connectionString);
            var reporte = await conn.QueryAsync<ReporteTiendaDto>(sql, new
            {
                UsuarioId = usuarioId,
                FechaDesde = fechaDesde,
                FechaHasta = fechaHasta
            });

            var resumen = new
            {
                totalTiendas = reporte.Count(),
                totalConsumos = reporte.Sum(r => r.TotalConsumos),
                montoTotal = reporte.Sum(r => r.MontoTotal)
            };

            return Ok(new { data = reporte, resumen, fechaDesde, fechaHasta = fechaHasta.AddDays(-1) });
        }

        // =====================================================
        // ULTIMOS CONSUMOS
        // GET /api/backoffice/ultimos-consumos?limit=20
        // =====================================================
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
                    cl.Nombre AS ClienteNombre,
                    cl.Codigo AS ClienteCodigo,
                    e.Nombre AS EmpresaNombre,
                    t.Nombre AS TiendaNombre,
                    u.Nombre AS CajeroNombre,
                    c.Monto,
                    c.Reversado
                FROM Consumos c
                INNER JOIN Clientes cl ON c.ClienteId = cl.Id
                INNER JOIN Empresas e ON c.EmpresaId = e.Id
                INNER JOIN ProveedorTiendas t ON c.TiendaId = t.Id
                INNER JOIN Usuarios u ON c.UsuarioRegistradorId = u.Id
                INNER JOIN Proveedores p ON c.ProveedorId = p.Id
                INNER JOIN UsuarioProveedor up ON p.Rnc = up.RncProveedor
                WHERE up.UsuarioId = @UsuarioId
                ORDER BY c.Fecha DESC";

            using var conn = new SqlConnection(_connectionString);
            var consumos = await conn.QueryAsync<ConsumoBackofficeDto>(sql, new { UsuarioId = usuarioId, Limit = limit });

            return Ok(consumos);
        }

        // <summary>
        /// GET /api/backoffice/cajeros/{cajeroId}/consumos
        /// Detalle de consumos de un cajero específico
        /// </summary>
        [HttpGet("cajeros/{cajeroId:int}/consumos")]
        public async Task<IActionResult> ConsumosCajero(
            int cajeroId,
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta)
        {
            var usuarioId = GetUsuarioIdFromToken();
            if (usuarioId == 0)
                return Unauthorized(new { message = "Token inválido" });

            using var conn = new SqlConnection(_connectionString);

            // Verificar que el cajero pertenece al proveedor del usuario
            var verificar = await conn.QueryFirstOrDefaultAsync<int?>(@"
        SELECT u.Id 
        FROM Usuarios u
        INNER JOIN ProveedorAsignaciones pa ON u.Id = pa.UsuarioId
        INNER JOIN ProveedorTiendas pt ON pa.TiendaId = pt.Id
        INNER JOIN Proveedores p ON pt.ProveedorId = p.Id
        INNER JOIN UsuarioProveedor up ON p.Rnc = up.RncProveedor
        WHERE u.Id = @CajeroId AND up.UsuarioId = @UsuarioId",
                new { CajeroId = cajeroId, UsuarioId = usuarioId });

            if (verificar == null)
                return NotFound(new { message = "Cajero no encontrado" });

            var fechaDesde = desde ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var fechaHasta = (hasta ?? DateTime.Now).AddDays(1);

            var sql = @"
        SELECT 
            c.Id,
            c.Fecha,
            cl.Nombre AS ClienteNombre,
            cl.Codigo AS ClienteCodigo,
            e.Nombre AS EmpresaNombre,
            ISNULL(c.Concepto, '') AS Concepto,
            c.Monto,
            c.Reversado
        FROM Consumos c
        INNER JOIN Clientes cl ON c.ClienteId = cl.Id
        INNER JOIN Empresas e ON c.EmpresaId = e.Id
        WHERE c.UsuarioRegistradorId = @CajeroId
          AND c.Fecha >= @Desde
          AND c.Fecha < @Hasta
        ORDER BY c.Fecha DESC";

            var consumos = await conn.QueryAsync<ConsumoDetalleCajeroDto>(sql, new
            {
                CajeroId = cajeroId,
                Desde = fechaDesde,
                Hasta = fechaHasta
            });

            return Ok(consumos);
        }

        /// <summary>
        /// GET /api/backoffice/tiendas/{tiendaId}/consumos
        /// Detalle de consumos de una tienda específica
        /// </summary>
        [HttpGet("tiendas/{tiendaId:int}/consumos")]
        public async Task<IActionResult> ConsumosTienda(
            int tiendaId,
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta)
        {
            var usuarioId = GetUsuarioIdFromToken();
            if (usuarioId == 0)
                return Unauthorized(new { message = "Token inválido" });

            using var conn = new SqlConnection(_connectionString);

            // Verificar que la tienda pertenece al proveedor del usuario
            var verificar = await conn.QueryFirstOrDefaultAsync<int?>(@"
        SELECT t.Id 
        FROM ProveedorTiendas t
        INNER JOIN Proveedores p ON t.ProveedorId = p.Id
        INNER JOIN UsuarioProveedor up ON p.Rnc = up.RncProveedor
        WHERE t.Id = @TiendaId AND up.UsuarioId = @UsuarioId",
                new { TiendaId = tiendaId, UsuarioId = usuarioId });

            if (verificar == null)
                return NotFound(new { message = "Tienda no encontrada" });

            var fechaDesde = desde ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var fechaHasta = (hasta ?? DateTime.Now).AddDays(1);

            var sql = @"
        SELECT 
            c.Id,
            c.Fecha,
            ISNULL(u.Nombre, 'Sin cajero') AS CajeroNombre,
            cl.Nombre AS ClienteNombre,
            cl.Codigo AS ClienteCodigo,
            e.Nombre AS EmpresaNombre,
            ISNULL(c.Concepto, '') AS Concepto,
            c.Monto,
            c.Reversado
        FROM Consumos c
        INNER JOIN Clientes cl ON c.ClienteId = cl.Id
        INNER JOIN Empresas e ON c.EmpresaId = e.Id
        LEFT JOIN Usuarios u ON c.UsuarioRegistradorId = u.Id
        WHERE c.TiendaId = @TiendaId
          AND c.Fecha >= @Desde
          AND c.Fecha < @Hasta
        ORDER BY c.Fecha DESC";

            var consumos = await conn.QueryAsync<ConsumoDetalleTiendaDto>(sql, new
            {
                TiendaId = tiendaId,
                Desde = fechaDesde,
                Hasta = fechaHasta
            });

            return Ok(consumos);
        }

    }

    // =====================================================
    // DTOs
    // =====================================================
    public class ProveedorBackofficeDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
        public string Rnc { get; set; } = "";
        public string? Telefono { get; set; }
        public string? Email { get; set; }
        public string? Direccion { get; set; }
        public string? Contacto { get; set; }
        public int DiasCorte { get; set; }
        public decimal PorcentajeComision { get; set; }
        public bool Activo { get; set; }
    }

    public class TiendaBackofficeDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
        public bool Activo { get; set; }
        public int TotalCajas { get; set; }
        public int TotalCajeros { get; set; }
        public decimal ConsumosMes { get; set; }
    }

    public class CajeroBackofficeDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
        public bool Activo { get; set; }
        public string TiendaNombre { get; set; } = "";
        public string? CajaNombre { get; set; }
        public int ConsumosHoy { get; set; }
        public decimal MontoHoy { get; set; }
        public DateTime? UltimoLoginUtc { get; set; }
    }

    public class DocumentoCxpBackofficeDto
    {
        public int Id { get; set; }
        public string NumeroDocumento { get; set; } = "";
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

    public class ReporteCajeroDto
    {
        public int CajeroId { get; set; }
        public string CajeroNombre { get; set; } = "";
        public int TotalConsumos { get; set; }
        public decimal MontoTotal { get; set; }
        public decimal PromedioConsumo { get; set; }
        public DateTime PrimerConsumo { get; set; }
        public DateTime UltimoConsumo { get; set; }
    }

    public class ReporteTiendaDto
    {
        public int TiendaId { get; set; }
        public string TiendaNombre { get; set; } = "";
        public int TotalConsumos { get; set; }
        public decimal MontoTotal { get; set; }
        public decimal PromedioConsumo { get; set; }
        public int ClientesAtendidos { get; set; }
        public int CajerosActivos { get; set; }
    }

    public class ConsumoBackofficeDto
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public string ClienteNombre { get; set; } = "";
        public string ClienteCodigo { get; set; } = "";
        public string EmpresaNombre { get; set; } = "";
        public string TiendaNombre { get; set; } = "";
        public string CajeroNombre { get; set; } = "";
        public decimal Monto { get; set; }
        public bool Reversado { get; set; }
    }

    public class ConsumoDetalleCajeroDto
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public string ClienteNombre { get; set; } = "";
        public string ClienteCodigo { get; set; } = "";
        public string EmpresaNombre { get; set; } = "";
        public string Concepto { get; set; } = "";
        public decimal Monto { get; set; }
        public bool Reversado { get; set; }
    }

    public class ConsumoDetalleTiendaDto
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public string CajeroNombre { get; set; } = "";
        public string ClienteNombre { get; set; } = "";
        public string ClienteCodigo { get; set; } = "";
        public string EmpresaNombre { get; set; } = "";
        public string Concepto { get; set; } = "";
        public decimal Monto { get; set; }
        public bool Reversado { get; set; }
    }
}