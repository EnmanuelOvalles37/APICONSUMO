using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using System.Data;
using Microsoft.Data.SqlClient;
using Consumo_App.Data;
using Consumo_App.Data.Sql;

namespace TuNamespace.Controllers
{
    [ApiController]
    [Route("api/dashboard-stats")]
    [Authorize]
    public class DashboardStatsController : ControllerBase
    {
        private readonly SqlConnectionFactory _connectionFactory;
        public DashboardStatsController(SqlConnectionFactory connectionFactory) => _connectionFactory = connectionFactory;


        /// GET /api/dashboard-stats

        [HttpGet]
        public async Task<IActionResult> GetStats()
        {
           using var connection = _connectionFactory.Create();

            try
            {
                if (connection.State != ConnectionState.Open)
                    await connection.OpenAsync();

                var hoy = DateTime.UtcNow.Date;
                var ayer = hoy.AddDays(-1);

                // Variables para estadísticas
                int totalClientes = 0, clientesActivos = 0;
                int consumosHoy = 0, consumosAyer = 0;
                decimal saldoTotalDisponible = 0, saldoTotalAsignado = 0;
                int totalProveedores = 0, proveedoresActivos = 0;
                int totalEmpresas = 0, empresasActivas = 0;
                decimal montoConsumosHoy = 0;

                // 1. Total de Clientes
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT 
                            COUNT(*) AS Total,
                            SUM(CASE WHEN Activo = 1 THEN 1 ELSE 0 END) AS Activos,
                            ISNULL(SUM(Saldo), 0) AS SaldoDisponible,
                            ISNULL(SUM(SaldoOriginal), 0) AS SaldoAsignado
                        FROM Clientes";

                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        totalClientes = reader.GetInt32(0);
                        clientesActivos = reader.GetInt32(1);
                        saldoTotalDisponible = reader.GetDecimal(2);
                        saldoTotalAsignado = reader.GetDecimal(3);
                    }
                }

                
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT 
                            SUM(CASE WHEN CAST(Fecha AS DATE) = @hoy AND Reversado = 0 THEN 1 ELSE 0 END) AS ConsumosHoy,
                            SUM(CASE WHEN CAST(Fecha AS DATE) = @ayer AND Reversado = 0 THEN 1 ELSE 0 END) AS ConsumosAyer,
                            ISNULL(SUM(CASE WHEN CAST(Fecha AS DATE) = @hoy AND Reversado = 0 THEN Monto ELSE 0 END), 0) AS MontoHoy
                        FROM Consumos
                        WHERE CAST(Fecha AS DATE) >= @ayer";

                    cmd.Parameters.Add(new SqlParameter("@hoy", SqlDbType.Date) { Value = hoy });
                    cmd.Parameters.Add(new SqlParameter("@ayer", SqlDbType.Date) { Value = ayer });

                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        consumosHoy = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                        consumosAyer = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                        montoConsumosHoy = reader.GetDecimal(2);
                    }
                }

                
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT 
                            COUNT(*) AS Total,
                            SUM(CASE WHEN Activo = 1 THEN 1 ELSE 0 END) AS Activos
                        FROM Proveedores";

                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        totalProveedores = reader.GetInt32(0);
                        proveedoresActivos = reader.GetInt32(1);
                    }
                }

                
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT 
                            COUNT(*) AS Total,
                            SUM(CASE WHEN Activo = 1 THEN 1 ELSE 0 END) AS Activas
                        FROM Empresas";

                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        totalEmpresas = reader.GetInt32(0);
                        empresasActivas = reader.GetInt32(1);
                    }
                }

                // Calcular cambios porcentuales
                var cambioConsumos = consumosAyer > 0
                    ? Math.Round(((decimal)(consumosHoy - consumosAyer) / consumosAyer) * 100, 1)
                    : (consumosHoy > 0 ? 100 : 0);

                
                var porcentajeSaldoUtilizado = saldoTotalAsignado > 0
                    ? Math.Round(((saldoTotalAsignado - saldoTotalDisponible) / saldoTotalAsignado) * 100, 1)
                    : 0;

                return Ok(new
                {
                    
                    TotalClientes = totalClientes,
                    ClientesActivos = clientesActivos,

                    ConsumosHoy = consumosHoy,
                    ConsumosAyer = consumosAyer,
                    CambioConsumos = cambioConsumos,
                    MontoConsumosHoy = montoConsumosHoy,

                    SaldoTotalDisponible = saldoTotalDisponible,
                    SaldoTotalAsignado = saldoTotalAsignado,
                    PorcentajeSaldoUtilizado = porcentajeSaldoUtilizado,

                    TotalProveedores = totalProveedores,
                    ProveedoresActivos = proveedoresActivos,

                    TotalEmpresas = totalEmpresas,
                    EmpresasActivas = empresasActivas,

                    // Metadata
                    FechaConsulta = DateTime.UtcNow,
                    Fecha = hoy
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR Dashboard Stats: {ex.Message}");
                return StatusCode(500, new { message = "Error interno al cargar estadísticas." });
            }
        }

        
        /// GET /api/dashboard-stats/extended
        
        [HttpGet("extended")]
        public async Task<IActionResult> GetExtendedStats()
        {
           using var connection = _connectionFactory.Create();

            try
            {
                if (connection.State != ConnectionState.Open)
                    await connection.OpenAsync();

                var hoy = DateTime.UtcNow.Date;
                var hace7Dias = hoy.AddDays(-7);
                var hace30Dias = hoy.AddDays(-30);

                // Consumos últimos 7 días
                var consumosPorDia = new List<object>();
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT 
                            CAST(Fecha AS DATE) AS Dia,
                            COUNT(*) AS Cantidad,
                            ISNULL(SUM(CASE WHEN Reversado = 0 THEN Monto ELSE 0 END), 0) AS Monto
                        FROM Consumos
                        WHERE Fecha >= @inicio
                        GROUP BY CAST(Fecha AS DATE)
                        ORDER BY Dia";
                    cmd.Parameters.Add(new SqlParameter("@inicio", SqlDbType.DateTime2) { Value = hace7Dias });

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        consumosPorDia.Add(new
                        {
                            Fecha = reader.GetDateTime(0),
                            Cantidad = reader.GetInt32(1),
                            Monto = reader.GetDecimal(2)
                        });
                    }
                }

               
                var topEmpresas = new List<object>();
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT TOP 5
                            e.Id,
                            e.Nombre,
                            COUNT(*) AS CantidadConsumos,
                            ISNULL(SUM(CASE WHEN c.Reversado = 0 THEN c.Monto ELSE 0 END), 0) AS MontoTotal
                        FROM Consumos c
                        INNER JOIN Empresas e ON c.EmpresaId = e.Id
                        WHERE c.Fecha >= @inicio
                        GROUP BY e.Id, e.Nombre
                        ORDER BY MontoTotal DESC";
                    cmd.Parameters.Add(new SqlParameter("@inicio", SqlDbType.DateTime2) { Value = hace30Dias });

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        topEmpresas.Add(new
                        {
                            Id = reader.GetInt32(0),
                            Nombre = reader.GetString(1),
                            CantidadConsumos = reader.GetInt32(2),
                            MontoTotal = reader.GetDecimal(3)
                        });
                    }
                }

                
                var topProveedores = new List<object>();
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT TOP 5
                            p.Id,
                            p.Nombre,
                            COUNT(*) AS CantidadConsumos,
                            ISNULL(SUM(CASE WHEN c.Reversado = 0 THEN c.Monto ELSE 0 END), 0) AS MontoTotal
                        FROM Consumos c
                        INNER JOIN Proveedores p ON c.ProveedorId = p.Id
                        WHERE c.Fecha >= @inicio
                        GROUP BY p.Id, p.Nombre
                        ORDER BY MontoTotal DESC";
                    cmd.Parameters.Add(new SqlParameter("@inicio", SqlDbType.DateTime2) { Value = hace30Dias });

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        topProveedores.Add(new
                        {
                            Id = reader.GetInt32(0),
                            Nombre = reader.GetString(1),
                            CantidadConsumos = reader.GetInt32(2),
                            MontoTotal = reader.GetDecimal(3)
                        });
                    }
                }

               
                var clientesSaldoBajo = new List<object>();
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT TOP 10
                            c.Id,
                            c.Nombre,
                            c.Cedula,
                            e.Nombre AS EmpresaNombre,
                            c.Saldo,
                            c.SaldoOriginal,
                            CASE WHEN c.SaldoOriginal > 0 
                                THEN CAST((c.Saldo * 100.0 / c.SaldoOriginal) AS DECIMAL(5,2))
                                ELSE 0 
                            END AS PorcentajeDisponible
                        FROM Clientes c
                        INNER JOIN Empresas e ON c.EmpresaId = e.Id
                        WHERE c.Activo = 1 
                          AND c.SaldoOriginal > 0
                          AND (c.Saldo * 100.0 / c.SaldoOriginal) <= 20
                        ORDER BY PorcentajeDisponible ASC";

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        clientesSaldoBajo.Add(new
                        {
                            Id = reader.GetInt32(0),
                            Nombre = reader.GetString(1),
                            Cedula = reader.IsDBNull(2) ? null : reader.GetString(2),
                            EmpresaNombre = reader.GetString(3),
                            Saldo = reader.GetDecimal(4),
                            SaldoOriginal = reader.GetDecimal(5),
                            PorcentajeDisponible = reader.GetDecimal(6)
                        });
                    }
                }

                
                var ultimosConsumos = new List<object>();
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT TOP 10
                            c.Id,
                            c.Fecha,
                            cl.Nombre AS ClienteNombre,
                            e.Nombre AS EmpresaNombre,
                            p.Nombre AS ProveedorNombre,
                            c.Monto,
                            c.Reversado
                        FROM Consumos c
                        INNER JOIN Clientes cl ON c.ClienteId = cl.Id
                        INNER JOIN Empresas e ON c.EmpresaId = e.Id
                        INNER JOIN Proveedores p ON c.ProveedorId = p.Id
                        ORDER BY c.Fecha DESC";

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        ultimosConsumos.Add(new
                        {
                            Id = reader.GetInt32(0),
                            Fecha = reader.GetDateTime(1),
                            ClienteNombre = reader.GetString(2),
                            EmpresaNombre = reader.GetString(3),
                            ProveedorNombre = reader.GetString(4),
                            Monto = reader.GetDecimal(5),
                            Reversado = reader.GetBoolean(6)
                        });
                    }
                }

                return Ok(new
                {
                    ConsumosPorDia = consumosPorDia,
                    TopEmpresas = topEmpresas,
                    TopProveedores = topProveedores,
                    ClientesSaldoBajo = clientesSaldoBajo,
                    UltimosConsumos = ultimosConsumos
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR Dashboard Extended Stats: {ex.Message}");
                return StatusCode(500, new { message = "Error interno al cargar estadísticas extendidas." });
            }
        }
    }
}