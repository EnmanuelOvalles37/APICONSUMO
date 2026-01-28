// AdminClientesAccesoController.cs
// Endpoints para que el admin gestione accesos de clientes

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/admin/clientes-acceso")]
    [Authorize(Roles = "admin,administrador")]
    public class AdminClientesAccesoController : ControllerBase
    {
        private readonly string _connectionString;
        private const string CONTRASENA_INICIAL = "123456";

        public AdminClientesAccesoController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")!;
        }

        // =====================================================
        // LISTAR CLIENTES CON ESTADO DE ACCESO
        // GET /api/admin/clientes-acceso?empresaId=1&buscar=juan
        // =====================================================
        [HttpGet]
        public async Task<IActionResult> ListarClientesAcceso(
            [FromQuery] int? empresaId,
            [FromQuery] string? buscar,
            [FromQuery] string? estado, // todos, activos, bloqueados, sinAcceso, pendientes
            [FromQuery] int pagina = 1,
            [FromQuery] int porPagina = 50)
        {
            using var conn = new SqlConnection(_connectionString);

            var whereClause = "WHERE c.Activo = 1";
            var parameters = new DynamicParameters();

            if (empresaId.HasValue)
            {
                whereClause += " AND c.EmpresaId = @EmpresaId";
                parameters.Add("EmpresaId", empresaId.Value);
            }

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                whereClause += " AND (c.Nombre LIKE @Buscar OR c.Cedula LIKE @Buscar OR c.Codigo LIKE @Buscar)";
                parameters.Add("Buscar", $"%{buscar}%");
            }

            switch (estado?.ToLower())
            {
                case "bloqueados":
                    whereClause += " AND c.BloqueadoHasta > GETUTCDATE()";
                    break;
                case "sinacceso":
                    whereClause += " AND c.AccesoHabilitado = 0";
                    break;
                case "pendientes":
                    whereClause += " AND (c.Contrasena IS NULL OR c.RequiereCambioContrasena = 1)";
                    break;
                case "activos":
                    whereClause += " AND c.AccesoHabilitado = 1 AND (c.BloqueadoHasta IS NULL OR c.BloqueadoHasta <= GETUTCDATE())";
                    break;
            }

            var offset = (pagina - 1) * porPagina;
            parameters.Add("Offset", offset);
            parameters.Add("PorPagina", porPagina);

            var sql = $@"
                SELECT 
                    c.Id,
                    c.Codigo,
                    c.Nombre,
                    c.Cedula,
                    e.Nombre AS EmpresaNombre,
                    c.Saldo AS SaldoDisponible,
                    c.SaldoOriginal AS LimiteCredito,
                    CASE 
                        WHEN c.AccesoHabilitado = 0 THEN 'deshabilitado'
                        WHEN c.BloqueadoHasta > GETUTCDATE() THEN 'bloqueado'
                        WHEN c.Contrasena IS NULL THEN 'sin_configurar'
                        WHEN c.RequiereCambioContrasena = 1 THEN 'pendiente_cambio'
                        ELSE 'activo'
                    END AS EstadoAcceso,
                    c.UltimoLoginUtc,
                    c.ContadorLoginsFallidos,
                    c.BloqueadoHasta,
                    c.AccesoHabilitado
                FROM Clientes c
                INNER JOIN Empresas e ON c.EmpresaId = e.Id
                {whereClause}
                ORDER BY e.Nombre, c.Nombre
                OFFSET @Offset ROWS FETCH NEXT @PorPagina ROWS ONLY";

            var countSql = $@"
                SELECT COUNT(*) FROM Clientes c
                INNER JOIN Empresas e ON c.EmpresaId = e.Id
                {whereClause}";

            var clientes = await conn.QueryAsync<ClienteAccesoDto>(sql, parameters);
            var total = await conn.QueryFirstAsync<int>(countSql, parameters);

            return Ok(new
            {
                data = clientes,
                total,
                pagina,
                porPagina,
                totalPaginas = (int)Math.Ceiling((double)total / porPagina)
            });
        }

        // =====================================================
        // RESETEAR CONTRASEÑA
        // POST /api/admin/clientes-acceso/{id}/resetear-contrasena
        // =====================================================
        [HttpPost("{id:int}/resetear-contrasena")]
        public async Task<IActionResult> ResetearContrasena(int id)
        {
            using var conn = new SqlConnection(_connectionString);

            var cliente = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT Id, Nombre FROM Clientes WHERE Id = @Id",
                new { Id = id });

            if (cliente == null)
                return NotFound(new { message = "Cliente no encontrado" });

            // Resetear a contraseña inicial (sin hash, se validará como texto plano en login)
            await conn.ExecuteAsync(@"
                UPDATE Clientes 
                SET Contrasena = NULL,
                    RequiereCambioContrasena = 1,
                    ContadorLoginsFallidos = 0,
                    BloqueadoHasta = NULL
                WHERE Id = @Id",
                new { Id = id });

            return Ok(new
            {
                message = $"Contraseña de {cliente.Nombre} reseteada. Nueva contraseña: {CONTRASENA_INICIAL}",
                contrasenaInicial = CONTRASENA_INICIAL
            });
        }

        // =====================================================
        // DESBLOQUEAR CUENTA
        // POST /api/admin/clientes-acceso/{id}/desbloquear
        // =====================================================
        [HttpPost("{id:int}/desbloquear")]
        public async Task<IActionResult> DesbloquearCuenta(int id)
        {
            using var conn = new SqlConnection(_connectionString);

            var result = await conn.ExecuteAsync(@"
                UPDATE Clientes 
                SET ContadorLoginsFallidos = 0,
                    BloqueadoHasta = NULL
                WHERE Id = @Id",
                new { Id = id });

            if (result == 0)
                return NotFound(new { message = "Cliente no encontrado" });

            return Ok(new { message = "Cuenta desbloqueada exitosamente" });
        }

        // =====================================================
        // HABILITAR/DESHABILITAR ACCESO
        // POST /api/admin/clientes-acceso/{id}/toggle-acceso
        // =====================================================
        [HttpPost("{id:int}/toggle-acceso")]
        public async Task<IActionResult> ToggleAcceso(int id)
        {
            using var conn = new SqlConnection(_connectionString);

            var cliente = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT Id, Nombre, AccesoHabilitado FROM Clientes WHERE Id = @Id",
                new { Id = id });

            if (cliente == null)
                return NotFound(new { message = "Cliente no encontrado" });

            var nuevoEstado = !(cliente.AccesoHabilitado ?? true);

            await conn.ExecuteAsync(@"
                UPDATE Clientes SET AccesoHabilitado = @Estado WHERE Id = @Id",
                new { Estado = nuevoEstado, Id = id });

            return Ok(new
            {
                message = nuevoEstado ? "Acceso habilitado" : "Acceso deshabilitado",
                accesoHabilitado = nuevoEstado
            });
        }

        // =====================================================
        // HABILITAR ACCESO MASIVO POR EMPRESA
        // POST /api/admin/clientes-acceso/habilitar-empresa/{empresaId}
        // =====================================================
        [HttpPost("habilitar-empresa/{empresaId:int}")]
        public async Task<IActionResult> HabilitarAccesoEmpresa(int empresaId)
        {
            using var conn = new SqlConnection(_connectionString);

            var result = await conn.ExecuteAsync(@"
                UPDATE Clientes 
                SET AccesoHabilitado = 1,
                    RequiereCambioContrasena = CASE WHEN Contrasena IS NULL THEN 1 ELSE RequiereCambioContrasena END
                WHERE EmpresaId = @EmpresaId AND Activo = 1",
                new { EmpresaId = empresaId });

            return Ok(new
            {
                message = $"Acceso habilitado para {result} clientes",
                clientesActualizados = result
            });
        }

        // =====================================================
        // ESTADÍSTICAS DE ACCESO
        // GET /api/admin/clientes-acceso/estadisticas
        // =====================================================
        [HttpGet("estadisticas")]
        public async Task<IActionResult> ObtenerEstadisticas([FromQuery] int? empresaId)
        {
            using var conn = new SqlConnection(_connectionString);

            var whereEmpresa = empresaId.HasValue ? "AND c.EmpresaId = @EmpresaId" : "";

            var sql = $@"
                SELECT 
                    COUNT(*) AS TotalClientes,
                    SUM(CASE WHEN c.AccesoHabilitado = 1 
                             AND (c.BloqueadoHasta IS NULL OR c.BloqueadoHasta <= GETUTCDATE())
                             AND c.Contrasena IS NOT NULL 
                             AND c.RequiereCambioContrasena = 0 THEN 1 ELSE 0 END) AS Activos,
                    SUM(CASE WHEN c.BloqueadoHasta > GETUTCDATE() THEN 1 ELSE 0 END) AS Bloqueados,
                    SUM(CASE WHEN c.AccesoHabilitado = 0 THEN 1 ELSE 0 END) AS Deshabilitados,
                    SUM(CASE WHEN c.Contrasena IS NULL THEN 1 ELSE 0 END) AS SinConfigurar,
                    SUM(CASE WHEN c.RequiereCambioContrasena = 1 AND c.Contrasena IS NOT NULL THEN 1 ELSE 0 END) AS PendienteCambio,
                    SUM(CASE WHEN c.UltimoLoginUtc >= DATEADD(DAY, -7, GETUTCDATE()) THEN 1 ELSE 0 END) AS LoginUltimos7Dias,
                    SUM(CASE WHEN c.UltimoLoginUtc >= DATEADD(DAY, -30, GETUTCDATE()) THEN 1 ELSE 0 END) AS LoginUltimos30Dias
                FROM Clientes c
                WHERE c.Activo = 1 {whereEmpresa}";

            var stats = await conn.QueryFirstAsync<EstadisticasAccesoDto>(sql, new { EmpresaId = empresaId });

            return Ok(stats);
        }

        // =====================================================
        // RESETEAR CONTRASEÑAS MASIVO
        // POST /api/admin/clientes-acceso/resetear-masivo
        // =====================================================
        [HttpPost("resetear-masivo")]
        public async Task<IActionResult> ResetearMasivo([FromBody] ResetearMasivoRequest request)
        {
            if (request.ClienteIds == null || !request.ClienteIds.Any())
                return BadRequest(new { message = "Debe seleccionar al menos un cliente" });

            using var conn = new SqlConnection(_connectionString);

            var result = await conn.ExecuteAsync(@"
                UPDATE Clientes 
                SET Contrasena = NULL,
                    RequiereCambioContrasena = 1,
                    ContadorLoginsFallidos = 0,
                    BloqueadoHasta = NULL
                WHERE Id IN @Ids",
                new { Ids = request.ClienteIds });

            return Ok(new
            {
                message = $"Contraseñas reseteadas para {result} clientes",
                clientesActualizados = result,
                contrasenaInicial = CONTRASENA_INICIAL
            });
        }
    }

    // DTOs
    public class ClienteAccesoDto
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string? Cedula { get; set; }
        public string EmpresaNombre { get; set; } = "";
        public decimal SaldoDisponible { get; set; }
        public decimal LimiteCredito { get; set; }
        public string EstadoAcceso { get; set; } = "";
        public DateTime? UltimoLoginUtc { get; set; }
        public int ContadorLoginsFallidos { get; set; }
        public DateTime? BloqueadoHasta { get; set; }
        public bool AccesoHabilitado { get; set; }
    }

    public class EstadisticasAccesoDto
    {
        public int TotalClientes { get; set; }
        public int Activos { get; set; }
        public int Bloqueados { get; set; }
        public int Deshabilitados { get; set; }
        public int SinConfigurar { get; set; }
        public int PendienteCambio { get; set; }
        public int LoginUltimos7Dias { get; set; }
        public int LoginUltimos30Dias { get; set; }
    }

    public class ResetearMasivoRequest
    {
        public List<int> ClienteIds { get; set; } = new();
    }
}