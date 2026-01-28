// Controllers/AdminConsumosController.cs
using Consumo_App.Data.Sql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/admin/consumos")]
    [Authorize]
    public class AdminConsumosController : ControllerBase
    {
        private readonly SqlConnectionFactory _db;

        public AdminConsumosController(SqlConnectionFactory db)
        {
            _db = db;
        }

        /// <summary>
        /// Lista todos los consumos del sistema con filtros opcionales
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetTodosLosConsumos(
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta,
            [FromQuery] int? proveedorId,
            [FromQuery] int? tiendaId,
            [FromQuery] int? cajaId,
            [FromQuery] string? clienteCedula,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            using var conn = _db.Create();

            var whereClause = "WHERE 1=1";
            if (desde.HasValue) whereClause += " AND c.Fecha >= @Desde";
            if (hasta.HasValue) whereClause += " AND c.Fecha <= @Hasta";
            if (proveedorId.HasValue) whereClause += " AND c.ProveedorId = @ProveedorId";
            if (tiendaId.HasValue) whereClause += " AND c.TiendaId = @TiendaId";
            if (cajaId.HasValue) whereClause += " AND c.CajaId = @CajaId";
            if (!string.IsNullOrWhiteSpace(clienteCedula)) whereClause += " AND cl.Cedula LIKE @ClienteCedula";

            var countSql = $@"
                SELECT COUNT(*)
                FROM Consumos c
                LEFT JOIN Clientes cl ON c.ClienteId = cl.Id
                {whereClause}";

            var total = await conn.QueryFirstAsync<int>(countSql, new
            {
                Desde = desde,
                Hasta = hasta,
                ProveedorId = proveedorId,
                TiendaId = tiendaId,
                CajaId = cajaId,
                ClienteCedula = $"%{clienteCedula}%"
            });

            var dataSql = $@"
                SELECT 
                    c.Id,
                    c.Fecha,
                    c.ProveedorId,
                    p.Nombre AS ProveedorNombre,
                    c.TiendaId,
                    t.Nombre AS TiendaNombre,
                    c.CajaId,
                    ca.Nombre AS CajaNombre,
                    c.ClienteId,
                    cl.Nombre AS ClienteNombre,
                    cl.Cedula AS ClienteCedula,
                    c.EmpresaId,
                    c.Monto,
                    c.Concepto,
                    c.Referencia,
                    c.Reversado,
                    c.UsuarioRegistradorId,
                    u.Nombre AS UsuarioRegistrador
                FROM Consumos c
                LEFT JOIN Clientes cl ON c.ClienteId = cl.Id
                LEFT JOIN Proveedores p ON c.ProveedorId = p.Id
                LEFT JOIN ProveedorTiendas t ON c.TiendaId = t.Id
                LEFT JOIN ProveedorCajas ca ON c.CajaId = ca.Id
                LEFT JOIN Usuarios u ON c.UsuarioRegistradorId = u.Id
                {whereClause}
                ORDER BY c.Fecha DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var data = await conn.QueryAsync<ConsumoAdminDto>(dataSql, new
            {
                Desde = desde,
                Hasta = hasta,
                ProveedorId = proveedorId,
                TiendaId = tiendaId,
                CajaId = cajaId,
                ClienteCedula = $"%{clienteCedula}%",
                Offset = (page - 1) * pageSize,
                PageSize = pageSize
            });

            var montoTotalSql = $@"
                SELECT ISNULL(SUM(c.Monto), 0)
                FROM Consumos c
                LEFT JOIN Clientes cl ON c.ClienteId = cl.Id
                {whereClause} AND c.Reversado = 0";

            var montoTotal = await conn.QueryFirstAsync<decimal>(montoTotalSql, new
            {
                Desde = desde,
                Hasta = hasta,
                ProveedorId = proveedorId,
                TiendaId = tiendaId,
                CajaId = cajaId,
                ClienteCedula = $"%{clienteCedula}%"
            });

            return Ok(new
            {
                Data = data,
                Total = total,
                Page = page,
                PageSize = pageSize,
                MontoTotal = montoTotal,
                Filtros = new { desde, hasta, proveedorId, tiendaId, cajaId, clienteCedula }
            });
        }

        /// <summary>
        /// Obtiene estadísticas de consumos por proveedor
        /// </summary>
        [HttpGet("estadisticas")]
        public async Task<IActionResult> GetEstadisticas(
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta)
        {
            using var conn = _db.Create();

            var whereClause = "WHERE c.Reversado = 0";
            if (desde.HasValue) whereClause += " AND c.Fecha >= @Desde";
            if (hasta.HasValue) whereClause += " AND c.Fecha <= @Hasta";

            var porProveedorSql = $@"
                SELECT 
                    c.ProveedorId,
                    p.Nombre AS ProveedorNombre,
                    COUNT(*) AS TotalConsumos,
                    SUM(c.Monto) AS MontoTotal,
                    AVG(c.Monto) AS Promedio,
                    COUNT(DISTINCT c.ClienteId) AS ClientesUnicos
                FROM Consumos c
                INNER JOIN Proveedores p ON c.ProveedorId = p.Id
                {whereClause}
                GROUP BY c.ProveedorId, p.Nombre
                ORDER BY SUM(c.Monto) DESC";

            var porProveedor = await conn.QueryAsync<EstadisticaProveedorDto>(porProveedorSql, new { Desde = desde, Hasta = hasta });

            var estadisticasGeneralesSql = $@"
                SELECT 
                    COUNT(*) AS TotalConsumos,
                    ISNULL(SUM(c.Monto), 0) AS MontoTotal,
                    ISNULL(AVG(c.Monto), 0) AS Promedio,
                    COUNT(DISTINCT c.ClienteId) AS ClientesUnicos,
                    COUNT(DISTINCT c.ProveedorId) AS ProveedoresActivos
                FROM Consumos c
                {whereClause}";

            var estadisticasGenerales = await conn.QueryFirstOrDefaultAsync<EstadisticasGeneralesDto>(
                estadisticasGeneralesSql, new { Desde = desde, Hasta = hasta });

            return Ok(new
            {
                Estadisticas = estadisticasGenerales,
                PorProveedor = porProveedor,
                Periodo = new { desde, hasta }
            });
        }

        /// <summary>
        /// Obtiene consumos detallados de un cliente específico
        /// </summary>
        [HttpGet("cliente/{clienteId}")]
        public async Task<IActionResult> GetConsumosCliente(
            int clienteId,
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta)
        {
            using var conn = _db.Create();

            var cliente = await conn.QueryFirstOrDefaultAsync<ClienteBasicoDto>(
                "SELECT Id, Nombre, Cedula FROM Clientes WHERE Id = @Id",
                new { Id = clienteId });

            if (cliente == null)
                return NotFound(new { message = "Cliente no encontrado" });

            var whereClause = "WHERE c.ClienteId = @ClienteId AND c.Reversado = 0";
            if (desde.HasValue) whereClause += " AND c.Fecha >= @Desde";
            if (hasta.HasValue) whereClause += " AND c.Fecha <= @Hasta";

            var consumosSql = $@"
                SELECT 
                    c.Id,
                    c.Fecha,
                    p.Nombre AS ProveedorNombre,
                    t.Nombre AS TiendaNombre,
                    ca.Nombre AS CajaNombre,
                    c.Monto,
                    c.Concepto,
                    u.Nombre AS UsuarioRegistrador
                FROM Consumos c
                LEFT JOIN Proveedores p ON c.ProveedorId = p.Id
                LEFT JOIN ProveedorTiendas t ON c.TiendaId = t.Id
                LEFT JOIN ProveedorCajas ca ON c.CajaId = ca.Id
                LEFT JOIN Usuarios u ON c.UsuarioRegistradorId = u.Id
                {whereClause}
                ORDER BY c.Fecha DESC";

            var consumos = await conn.QueryAsync<ConsumoClienteDto>(consumosSql, new
            {
                ClienteId = clienteId,
                Desde = desde,
                Hasta = hasta
            });

            var consumosList = consumos.ToList();
            var montoTotal = consumosList.Sum(c => c.Monto);
            var cantidadConsumos = consumosList.Count;

            return Ok(new
            {
                Cliente = cliente,
                Consumos = consumosList,
                Estadisticas = new
                {
                    CantidadConsumos = cantidadConsumos,
                    MontoTotal = montoTotal,
                    Promedio = cantidadConsumos > 0 ? montoTotal / cantidadConsumos : 0
                },
                Periodo = new { desde, hasta }
            });
        }

        /// <summary>
        /// Obtiene consumos por tienda
        /// </summary>
        [HttpGet("tienda/{tiendaId}")]
        public async Task<IActionResult> GetConsumosTienda(
            int tiendaId,
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            using var conn = _db.Create();

            var whereClause = "WHERE c.TiendaId = @TiendaId AND c.Reversado = 0";
            if (desde.HasValue) whereClause += " AND c.Fecha >= @Desde";
            if (hasta.HasValue) whereClause += " AND c.Fecha <= @Hasta";

            var countSql = $"SELECT COUNT(*) FROM Consumos c {whereClause}";
            var total = await conn.QueryFirstAsync<int>(countSql, new
            {
                TiendaId = tiendaId,
                Desde = desde,
                Hasta = hasta
            });

            var dataSql = $@"
                SELECT 
                    c.Id,
                    c.Fecha,
                    cl.Nombre AS ClienteNombre,
                    ca.Nombre AS CajaNombre,
                    c.Monto,
                    u.Nombre AS UsuarioRegistrador
                FROM Consumos c
                LEFT JOIN Clientes cl ON c.ClienteId = cl.Id
                LEFT JOIN ProveedorCajas ca ON c.CajaId = ca.Id
                LEFT JOIN Usuarios u ON c.UsuarioRegistradorId = u.Id
                {whereClause}
                ORDER BY c.Fecha DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var data = await conn.QueryAsync<ConsumoTiendaDto>(dataSql, new
            {
                TiendaId = tiendaId,
                Desde = desde,
                Hasta = hasta,
                Offset = (page - 1) * pageSize,
                PageSize = pageSize
            });

            var montoTotalSql = $"SELECT ISNULL(SUM(c.Monto), 0) FROM Consumos c {whereClause}";
            var montoTotal = await conn.QueryFirstAsync<decimal>(montoTotalSql, new
            {
                TiendaId = tiendaId,
                Desde = desde,
                Hasta = hasta
            });

            return Ok(new
            {
                Data = data,
                Total = total,
                Page = page,
                PageSize = pageSize,
                MontoTotal = montoTotal
            });
        }
    }

    #region DTOs

    public class ConsumoAdminDto
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public int ProveedorId { get; set; }
        public string? ProveedorNombre { get; set; }
        public int? TiendaId { get; set; }
        public string? TiendaNombre { get; set; }
        public int? CajaId { get; set; }
        public string? CajaNombre { get; set; }
        public int ClienteId { get; set; }
        public string? ClienteNombre { get; set; }
        public string? ClienteCedula { get; set; }
        public int? EmpresaId { get; set; }
        public decimal Monto { get; set; }
        public string? Concepto { get; set; }
        public string? Referencia { get; set; }
        public bool Reversado { get; set; }
        public int? UsuarioRegistradorId { get; set; }
        public string? UsuarioRegistrador { get; set; }
    }

    public class EstadisticaProveedorDto
    {
        public int ProveedorId { get; set; }
        public string ProveedorNombre { get; set; } = "";
        public int TotalConsumos { get; set; }
        public decimal MontoTotal { get; set; }
        public decimal Promedio { get; set; }
        public int ClientesUnicos { get; set; }
    }

    public class EstadisticasGeneralesDto
    {
        public int TotalConsumos { get; set; }
        public decimal MontoTotal { get; set; }
        public decimal Promedio { get; set; }
        public int ClientesUnicos { get; set; }
        public int ProveedoresActivos { get; set; }
    }

    public class ClienteBasicoDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
        public string? Cedula { get; set; }
    }

    public class ConsumoClienteDto
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public string? ProveedorNombre { get; set; }
        public string? TiendaNombre { get; set; }
        public string? CajaNombre { get; set; }
        public decimal Monto { get; set; }
        public string? Concepto { get; set; }
        public string? UsuarioRegistrador { get; set; }
    }

    public class ConsumoTiendaDto
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public string? ClienteNombre { get; set; }
        public string? CajaNombre { get; set; }
        public decimal Monto { get; set; }
        public string? UsuarioRegistrador { get; set; }
    }

    #endregion
}