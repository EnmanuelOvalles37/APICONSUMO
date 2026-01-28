// ClienteController.cs
// Controller para el dashboard personal de empleados (clientes)

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Security.Claims;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/cliente")]
    [Authorize]
    public class ClienteController : ControllerBase
    {
        private readonly string _connectionString;

        public ClienteController(IConfiguration configuration)
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

        [HttpGet("mi-perfil")]
        public async Task<IActionResult> ObtenerMiPerfil()
        {
            var tipoUsuario = User.FindFirst("TipoUsuario")?.Value;
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!int.TryParse(userIdClaim, out var id))
                return Unauthorized();

            using var conn = new SqlConnection(_connectionString);

            if (tipoUsuario == "empleado")
            {
                var sqlEmpleado = @"
            SELECT 
                c.Id,
                c.Codigo,
                c.Nombre,
                c.Cedula,
                e.Nombre AS EmpresaNombre,
                c.Grupo,
                c.SaldoOriginal AS LimiteCredito,
                c.Saldo AS SaldoDisponible,
                (c.SaldoOriginal - c.Saldo) AS CreditoUtilizado,
                CASE 
                    WHEN c.SaldoOriginal > 0 
                    THEN ROUND(((c.SaldoOriginal - c.Saldo) / c.SaldoOriginal) * 100, 1) 
                    ELSE 0 
                END AS PorcentajeUtilizado
            FROM Clientes c
            INNER JOIN Empresas e ON c.EmpresaId = e.Id
            WHERE c.Id = @Id AND c.Activo = 1";

                var perfil = await conn.QueryFirstOrDefaultAsync<MiPerfilDto>(
                    sqlEmpleado, new { Id = id });

                if (perfil == null)
                    return NotFound();

                return Ok(perfil);
            }

            // Usuario del sistema
            var sqlUsuario = @"
        SELECT 
            c.Id,
            c.Codigo,
            c.Nombre,
            c.Cedula,
            e.Nombre AS EmpresaNombre,
            c.Grupo,
            c.SaldoOriginal AS LimiteCredito,
            c.Saldo AS SaldoDisponible,
            (c.SaldoOriginal - c.Saldo) AS CreditoUtilizado,
            CASE 
                WHEN c.SaldoOriginal > 0 
                THEN ROUND(((c.SaldoOriginal - c.Saldo) / c.SaldoOriginal) * 100, 1) 
                ELSE 0 
            END AS PorcentajeUtilizado
        FROM Clientes c
        INNER JOIN Empresas e ON c.EmpresaId = e.Id
        INNER JOIN UsuarioCliente uc ON c.Id = uc.ClienteId
        WHERE uc.UsuarioId = @UsuarioId AND c.Activo = 1";

            var perfilUsuario = await conn.QueryFirstOrDefaultAsync<MiPerfilDto>(
                sqlUsuario, new { UsuarioId = id });

            if (perfilUsuario == null)
                return NotFound();

            return Ok(perfilUsuario);
        }

        

        private async Task<int?> ObtenerClienteIdDelUsuario(SqlConnection conn)
        {
            var tipoUsuario = User.FindFirst("TipoUsuario")?.Value;
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!int.TryParse(userIdClaim, out var id))
                return null;

            // 👇 SI ES EMPLEADO, EL ID YA ES EL CLIENTE
            if (tipoUsuario == "empleado")
            {
                return id;
            }

            // 👇 SI ES USUARIO DEL SISTEMA, BUSCAR RELACIÓN
            var clienteId = await conn.QueryFirstOrDefaultAsync<int?>(@"
        SELECT uc.ClienteId
        FROM UsuarioCliente uc
        WHERE uc.UsuarioId = @UsuarioId",
                new { UsuarioId = id });

            return clienteId;
        }



        
        /*[HttpGet("mis-consumos")]
        public async Task<IActionResult> ObtenerMisConsumos(
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta)
        {
            var usuarioId = GetUsuarioIdFromToken();
            if (usuarioId == 0)
                return Unauthorized(new { message = "Token inválido" });

            using var conn = new SqlConnection(_connectionString);

            // Primero obtener el ClienteId del usuario
            var clienteId = await ObtenerClienteIdDelUsuario(conn, usuarioId);

            if (clienteId == null)
                return NotFound(new { message = "No se encontró perfil de empleado asociado" });

            var fechaDesde = desde ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var fechaHasta = (hasta ?? DateTime.Now.Date).AddDays(1);

            var sql = @"
                SELECT 
                    c.Id,
                    c.Fecha,
                    p.Nombre AS ProveedorNombre,
                    t.Nombre AS TiendaNombre,
                    c.Concepto,
                    c.Monto,
                    c.Reversado
                FROM Consumos c
                INNER JOIN Proveedores p ON c.ProveedorId = p.Id
                LEFT JOIN ProveedorTiendas t ON c.TiendaId = t.Id
                WHERE c.ClienteId = @ClienteId
                  AND c.Fecha >= @FechaDesde
                  AND c.Fecha < @FechaHasta
                ORDER BY c.Fecha DESC";

            var consumos = await conn.QueryAsync<MiConsumoDto>(sql, new
            {
                ClienteId = clienteId,
                FechaDesde = fechaDesde,
                FechaHasta = fechaHasta
            });

            // Calcular resumen (solo consumos no reversados)
            var consumosActivos = consumos.Where(c => !c.Reversado).ToList();
            var resumen = new
            {
                totalConsumos = consumosActivos.Count,
                montoTotal = consumosActivos.Sum(c => c.Monto)
            };

            return Ok(new
            {
                consumos,
                resumen,
                fechaDesde,
                fechaHasta = fechaHasta.AddDays(-1)
            });
        }*/

        [HttpGet("mis-consumos")]
        public async Task<IActionResult> ObtenerMisConsumos(
    [FromQuery] DateTime? desde,
    [FromQuery] DateTime? hasta)
        {
            using var conn = new SqlConnection(_connectionString);

            var clienteId = await ObtenerClienteIdDelUsuario(conn);

            if (clienteId == null)
                return Unauthorized(new { message = "No se pudo determinar el cliente" });

            var fechaDesde = desde ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var fechaHasta = (hasta ?? DateTime.Now.Date).AddDays(1);

            var sql = @"
        SELECT 
            c.Id,
            c.Fecha,
            p.Nombre AS ProveedorNombre,
            t.Nombre AS TiendaNombre,
            c.Concepto,
            c.Monto,
            c.Reversado
        FROM Consumos c
        INNER JOIN Proveedores p ON c.ProveedorId = p.Id
        LEFT JOIN ProveedorTiendas t ON c.TiendaId = t.Id
        WHERE c.ClienteId = @ClienteId
          AND c.Fecha >= @FechaDesde
          AND c.Fecha < @FechaHasta
        ORDER BY c.Fecha DESC";

            var consumos = await conn.QueryAsync<MiConsumoDto>(sql, new
            {
                ClienteId = clienteId,
                FechaDesde = fechaDesde,
                FechaHasta = fechaHasta
            });

            return Ok(consumos);
        }


        // =====================================================
        // OBTENER RESUMEN MENSUAL
        // GET /api/cliente/resumen-mensual?meses=6
        // =====================================================
        [HttpGet("resumen-mensual")]
        public async Task<IActionResult> ResumenMensual([FromQuery] int meses = 6)
        {
            var usuarioId = GetUsuarioIdFromToken();
            if (usuarioId == 0)
                return Unauthorized(new { message = "Token inválido" });

            using var conn = new SqlConnection(_connectionString);

            
                       

            var sql = @"
                SELECT 
                    YEAR(c.Fecha) AS Anio,
                    MONTH(c.Fecha) AS Mes,
                    COUNT(*) AS TotalConsumos,
                    SUM(c.Monto) AS MontoTotal
                FROM Consumos c
                WHERE c.ClienteId = @ClienteId
                  AND c.Reversado = 0
                  AND c.Fecha >= DATEADD(MONTH, -@Meses, GETDATE())
                GROUP BY YEAR(c.Fecha), MONTH(c.Fecha)
                ORDER BY Anio DESC, Mes DESC";

            var resumen = await conn.QueryAsync<ResumenMensualDto>(sql, new
            {                
                Meses = meses
            });

            return Ok(resumen);
        }

       
    }

    // =====================================================
    // DTOs
    // =====================================================
    public class MiPerfilDto
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string? Cedula { get; set; }
        public string EmpresaNombre { get; set; } = "";
        public string? Grupo { get; set; }
        public decimal LimiteCredito { get; set; }
        public decimal SaldoDisponible { get; set; }
        public decimal CreditoUtilizado { get; set; }
        public decimal PorcentajeUtilizado { get; set; }
    }

    public class MiConsumoDto
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public string ProveedorNombre { get; set; } = "";
        public string? TiendaNombre { get; set; }
        public string? Concepto { get; set; }
        public decimal Monto { get; set; }
        public bool Reversado { get; set; }
    }

    public class ResumenMensualDto
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public int TotalConsumos { get; set; }
        public decimal MontoTotal { get; set; }
    }
}