// Services/CorteAutomaticoService.cs
// Servicio de background que ejecuta cortes automáticos según configuración por empresa
// Migrado a Dapper

using Dapper;
using Consumo_App.Data.Sql;
using Consumo_App.Models.Pagos;
using System.Globalization;

namespace Consumo_App.Services
{
    public class CorteAutomaticoService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<CorteAutomaticoService> _logger;
        private readonly TimeSpan _intervaloVerificacion = TimeSpan.FromMinutes(30);

        public CorteAutomaticoService(IServiceProvider serviceProvider, ILogger<CorteAutomaticoService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Servicio de Corte Automático iniciado.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcesarCortesAutomaticos(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error en el servicio de corte automático");
                }

                await Task.Delay(_intervaloVerificacion, stoppingToken);
            }

            _logger.LogInformation("Servicio de Corte Automático detenido.");
        }

        private async Task ProcesarCortesAutomaticos(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var connectionFactory = scope.ServiceProvider.GetRequiredService<SqlConnectionFactory>();

            // Obtener zona horaria de RD
            TimeZoneInfo tz = ObtenerZonaHoraria();

            var ahora = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            var diaActual = ahora.Day;
            var mesActual = ahora.Month;
            var anioActual = ahora.Year;

            _logger.LogInformation($"Verificando cortes automáticos para día {diaActual} del mes {mesActual}/{anioActual}");

            // Obtener configuraciones con corte automático habilitado
            using var connection = connectionFactory.Create();

            var dbName = await connection.ExecuteScalarAsync<string>("SELECT DB_NAME()");
            _logger.LogWarning($"🔍 Conectado a base de datos: {dbName}");

            const string sqlConfiguraciones = @"
                SELECT 
                    c.Id, c.EmpresaId, c.DiaCorte, c.DiasGracia, c.CorteAutomatico,
                    e.Nombre AS EmpresaNombre
                FROM ConfiguracionCortes c
                INNER JOIN Empresas e ON c.EmpresaId = e.Id
                WHERE c.CorteAutomatico = 1 AND c.DiaCorte = @DiaActual";

            var configuraciones = await connection.QueryAsync<ConfiguracionCorteDto>(
                sqlConfiguraciones, new { DiaActual = diaActual });

            foreach (var config in configuraciones)
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    await ProcesarCorteEmpresa(connectionFactory, config, ahora, tz, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error procesando corte automático para empresa {config.EmpresaId} - {config.EmpresaNombre}");
                }
            }
        }

        private async Task ProcesarCorteEmpresa(
            SqlConnectionFactory connectionFactory,
            ConfiguracionCorteDto config,
            DateTime ahora,
            TimeZoneInfo tz,
            CancellationToken stoppingToken)
        {
            var empresaId = config.EmpresaId;
            var empresaNombre = config.EmpresaNombre ?? $"ID:{empresaId}";

            // Calcular período del corte
            var periodoHasta = new DateTime(ahora.Year, ahora.Month, config.DiaCorte).AddDays(-1);
            var periodoDesde = periodoHasta.AddMonths(-1).AddDays(1);

            if (ahora.Day < config.DiaCorte)
            {
                periodoHasta = periodoHasta.AddMonths(-1);
                periodoDesde = periodoDesde.AddMonths(-1);
            }

            _logger.LogInformation($"Procesando corte automático para {empresaNombre}: {periodoDesde:yyyy-MM-dd} a {periodoHasta:yyyy-MM-dd}");

            using var connection = connectionFactory.Create();
            await connection.OpenAsync(stoppingToken);

            // Verificar si ya existe un corte para este período
            const string sqlExiste = @"
                SELECT COUNT(1) FROM CxcDocumentos 
                WHERE EmpresaId = @EmpresaId 
                  AND PeriodoDesde = @PeriodoDesde 
                  AND PeriodoHasta = @PeriodoHasta 
                  AND Estado != 5";

            var existe = await connection.ExecuteScalarAsync<int>(sqlExiste, new
            {
                EmpresaId = empresaId,
                PeriodoDesde = periodoDesde,
                PeriodoHasta = periodoHasta
            }) > 0;

            if (existe)
            {
                _logger.LogInformation($"Ya existe corte para {empresaNombre} en período {periodoDesde:yyyy-MM-dd} a {periodoHasta:yyyy-MM-dd}. Saltando.");
                return;
            }

            // Convertir fechas a UTC para la consulta
            var fechaDesdeUtc = TimeZoneInfo.ConvertTimeToUtc(periodoDesde, tz);
            var fechaHastaUtc = TimeZoneInfo.ConvertTimeToUtc(periodoHasta.AddDays(1), tz);

            using var transaction = connection.BeginTransaction();

            try
            {
                // Obtener consumos pendientes
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
                    EmpresaId = empresaId,
                    FechaDesde = fechaDesdeUtc,
                    FechaHasta = fechaHastaUtc
                }, transaction)).ToList();

                if (!consumos.Any())
                {
                    _logger.LogInformation($"No hay consumos para facturar en {empresaNombre} para el período {periodoDesde:yyyy-MM-dd} a {periodoHasta:yyyy-MM-dd}");
                    transaction.Rollback();
                    return;
                }

                var montoTotal = consumos.Sum(c => c.Monto);

                // Generar número de documento con lock
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
                var fechaVencimiento = DateTime.UtcNow.AddDays(config.DiasGracia);

                // Crear documento
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
                    EmpresaId = empresaId,
                    NumeroDocumento = numeroDocumento,
                    FechaEmision = DateTime.UtcNow,
                    FechaVencimiento = fechaVencimiento,
                    PeriodoDesde = periodoDesde,
                    PeriodoHasta = periodoHasta,
                    MontoTotal = montoTotal,
                    MontoPendiente = montoTotal,
                    Estado = (int)EstadoCxc.Pendiente,
                    Notas = "Corte automático generado por el sistema",
                    CreadoUtc = DateTime.UtcNow,
                    UsuarioId = 1 // Usuario sistema
                }, transaction);

                // Crear detalles (batch insert)
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

                // Actualizar fecha de modificación en configuración
                await connection.ExecuteAsync(
                    "UPDATE ConfiguracionCortes SET ModificadoUtc = @Fecha WHERE Id = @Id",
                    new { Fecha = DateTime.UtcNow, Id = config.Id }, transaction);

                transaction.Commit();

                _logger.LogInformation($"✅ Corte automático generado para {empresaNombre}: {numeroDocumento}, Monto: {montoTotal:N2}, Consumos: {consumos.Count}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error generando corte automático para empresa {empresaId}");
                try { transaction.Rollback(); } catch { }
                throw;
            }
        }

        private static TimeZoneInfo ObtenerZonaHoraria()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");
            }
            catch
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById("SA Western Standard Time");
                }
                catch
                {
                    return TimeZoneInfo.Utc;
                }
            }
        }

        // DTO interno para configuración
        private class ConfiguracionCorteDto
        {
            public int Id { get; set; }
            public int EmpresaId { get; set; }
            public int DiaCorte { get; set; }
            public int DiasGracia { get; set; }
            public bool CorteAutomatico { get; set; }
            public string? EmpresaNombre { get; set; }
        }
    }
}