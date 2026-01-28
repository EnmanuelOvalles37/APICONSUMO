using Dapper;
using Consumo_App.Data.Sql;
using Consumo_App.Models;

namespace Consumo_App.Servicios
{
    public interface IConsumoService
    {
        Task<Consumo> CreateConsumoAsync(
            int empresaId, int clienteId, int proveedorId,
            DateTime fecha, decimal monto,
            string? concepto, string? referencia,
            int usuarioId);
        Task<IReadOnlyList<Consumo>> GetAllConsumosAsync();
        Task<Consumo?> GetConsumoByIdAsync(int id);
        Task<IReadOnlyList<Consumo>> GetConsumosByFechaAsync(DateTime desde, DateTime hasta);
        Task<IReadOnlyList<Consumo>> GetConsumosByUsuarioAsync(int usuarioId, DateTime? desde = null, DateTime? hasta = null);
        Task<IReadOnlyList<Consumo>> GetConsumosByClienteIdAsync(int clienteId);
        Task<IReadOnlyList<Consumo>> GetConsumosByClienteCedulaAsync(string cedula);
    }

    public class ConsumoService : IConsumoService
    {
        private readonly SqlConnectionFactory _connectionFactory;

        public ConsumoService(SqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<Consumo> CreateConsumoAsync(
            int empresaId, int clienteId, int proveedorId,
            DateTime fecha, decimal monto,
            string? concepto, string? referencia,
            int usuarioId)
        {
            if (monto <= 0)
                throw new InvalidOperationException("El monto debe ser mayor que 0.");

            using var connection = _connectionFactory.Create();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                // Validar cliente
                const string sqlCliente = @"
                    SELECT Id, Nombre, Saldo, SaldoOriginal, Activo, EmpresaId
                    FROM Clientes 
                    WHERE Id = @ClienteId AND EmpresaId = @EmpresaId";

                var cliente = await connection.QueryFirstOrDefaultAsync<dynamic>(
                    sqlCliente, new { ClienteId = clienteId, EmpresaId = empresaId }, transaction);

                if (cliente == null)
                    throw new InvalidOperationException("Cliente no encontrado para la empresa.");

                if (!(bool)cliente.Activo)
                    throw new InvalidOperationException("El cliente está inactivo.");

                if (monto > (decimal)cliente.Saldo)
                    throw new InvalidOperationException("Saldo insuficiente.");

                // Validar proveedor
                const string sqlProveedor = @"
                    SELECT Id, Nombre, Activo, PorcentajeComision
                    FROM Proveedores 
                    WHERE Id = @ProveedorId";

                var proveedor = await connection.QueryFirstOrDefaultAsync<dynamic>(
                    sqlProveedor, new { ProveedorId = proveedorId }, transaction);

                if (proveedor == null)
                    throw new InvalidOperationException("Proveedor no válido.");

                if (!(bool)proveedor.Activo)
                    throw new InvalidOperationException("Proveedor inactivo.");

                // Generar secuencia
                var maxId = await connection.ExecuteScalarAsync<int?>(
                    "SELECT MAX(Id) FROM Consumos", transaction: transaction) ?? 0;
                var secuencia = GenerarSecuencia(maxId + 1);

                // Calcular comisión
                decimal porcentajeComision = proveedor.PorcentajeComision ?? 0m;
                decimal montoComision = Math.Round(monto * porcentajeComision / 100, 2);
                decimal montoNetoProveedor = monto - montoComision;

                // Fecha efectiva
                var fechaEfectiva = fecha == default ? DateTime.UtcNow : fecha;

                // Insertar consumo
                const string sqlInsertConsumo = @"
                    INSERT INTO Consumos (
                        EmpresaId, ClienteId, ProveedorId, Fecha, Monto,
                        Concepto, Referencia, UsuarioRegistradorId,
                        PorcentajeComision, MontoComision, MontoNetoProveedor,
                        Reversado, Secuencia
                    )
                    OUTPUT INSERTED.Id
                    VALUES (
                        @EmpresaId, @ClienteId, @ProveedorId, @Fecha, @Monto,
                        @Concepto, @Referencia, @UsuarioRegistradorId,
                        @PorcentajeComision, @MontoComision, @MontoNetoProveedor,
                        0, @Secuencia
                    )";

                var consumoId = await connection.ExecuteScalarAsync<int>(sqlInsertConsumo, new
                {
                    EmpresaId = empresaId,
                    ClienteId = clienteId,
                    ProveedorId = proveedorId,
                    Fecha = fechaEfectiva,
                    Monto = Math.Round(monto, 2),
                    Concepto = concepto?.Trim(),
                    Referencia = referencia?.Trim(),
                    UsuarioRegistradorId = usuarioId,
                    PorcentajeComision = porcentajeComision,
                    MontoComision = montoComision,
                    MontoNetoProveedor = montoNetoProveedor,
                    Secuencia = secuencia
                }, transaction);

                // Descontar saldo del cliente
                const string sqlUpdateSaldo = "UPDATE Clientes SET Saldo = Saldo - @Monto WHERE Id = @ClienteId";
                await connection.ExecuteAsync(sqlUpdateSaldo, new { Monto = monto, ClienteId = clienteId }, transaction);

                // Crear movimiento CxP
                const string sqlInsertCxp = @"
                    INSERT INTO CxpMovimientos (
                        ProveedorId, Fecha, Tipo, Descripcion, 
                        Debe, Haber, ConsumoId, CreadoUtc
                    )
                    VALUES (
                        @ProveedorId, @Fecha, 'FACT', @Descripcion,
                        @Debe, 0, @ConsumoId, @CreadoUtc
                    )";

                var descripcionCxp = $"Consumo #{consumoId}" +
                    (string.IsNullOrWhiteSpace(concepto) ? "" : $" - {concepto}");

                await connection.ExecuteAsync(sqlInsertCxp, new
                {
                    ProveedorId = proveedorId,
                    Fecha = fechaEfectiva,
                    Descripcion = descripcionCxp,
                    Debe = monto,
                    ConsumoId = consumoId,
                    CreadoUtc = DateTime.UtcNow
                }, transaction);

                transaction.Commit();

                // Retornar el consumo creado
                return new Consumo
                {
                    Id = consumoId,
                    EmpresaId = empresaId,
                    ClienteId = clienteId,
                    ProveedorId = proveedorId,
                    Fecha = fechaEfectiva,
                    Monto = Math.Round(monto, 2),
                    Concepto = concepto?.Trim(),
                    Referencia = referencia?.Trim(),
                    UsuarioRegistradorId = usuarioId,
                    PorcentajeComision = porcentajeComision,
                    MontoComision = montoComision,
                    MontoNetoProveedor = montoNetoProveedor,
                    Reversado = false
                };
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<IReadOnlyList<Consumo>> GetAllConsumosAsync()
        {
            const string sql = @"
                SELECT 
                    Id, EmpresaId, ClienteId, ProveedorId, TiendaId, CajaId,
                    Fecha, Monto, Concepto, Referencia, Nota,
                    UsuarioRegistradorId, PorcentajeComision, MontoComision, MontoNetoProveedor,
                    Reversado, ReversadoUtc, ReversadoPorUsuarioId
                FROM Consumos
                ORDER BY Fecha DESC, Id DESC";

            using var connection = _connectionFactory.Create();
            var result = await connection.QueryAsync<Consumo>(sql);
            return result.ToList();
        }

        public async Task<Consumo?> GetConsumoByIdAsync(int id)
        {
            const string sql = @"
                SELECT 
                    Id, EmpresaId, ClienteId, ProveedorId, TiendaId, CajaId,
                    Fecha, Monto, Concepto, Referencia, Nota,
                    UsuarioRegistradorId, PorcentajeComision, MontoComision, MontoNetoProveedor,
                    Reversado, ReversadoUtc, ReversadoPorUsuarioId
                FROM Consumos
                WHERE Id = @Id";

            using var connection = _connectionFactory.Create();
            return await connection.QueryFirstOrDefaultAsync<Consumo>(sql, new { Id = id });
        }

        public async Task<IReadOnlyList<Consumo>> GetConsumosByFechaAsync(DateTime desde, DateTime hasta)
        {
            var d1 = desde.Date;
            var d2 = hasta.Date.AddDays(1).AddTicks(-1);

            const string sql = @"
                SELECT 
                    Id, EmpresaId, ClienteId, ProveedorId, TiendaId, CajaId,
                    Fecha, Monto, Concepto, Referencia, Nota,
                    UsuarioRegistradorId, PorcentajeComision, MontoComision, MontoNetoProveedor,
                    Reversado, ReversadoUtc, ReversadoPorUsuarioId
                FROM Consumos
                WHERE Fecha >= @FechaDesde AND Fecha <= @FechaHasta
                ORDER BY Fecha DESC, Id DESC";

            using var connection = _connectionFactory.Create();
            var result = await connection.QueryAsync<Consumo>(sql, new { FechaDesde = d1, FechaHasta = d2 });
            return result.ToList();
        }

        public async Task<IReadOnlyList<Consumo>> GetConsumosByUsuarioAsync(
            int usuarioId, DateTime? desde = null, DateTime? hasta = null)
        {
            var whereClause = "WHERE UsuarioRegistradorId = @UsuarioId";
            var parameters = new DynamicParameters();
            parameters.Add("UsuarioId", usuarioId);

            if (desde.HasValue)
            {
                whereClause += " AND Fecha >= @FechaDesde";
                parameters.Add("FechaDesde", desde.Value.Date);
            }

            if (hasta.HasValue)
            {
                whereClause += " AND Fecha <= @FechaHasta";
                parameters.Add("FechaHasta", hasta.Value.Date.AddDays(1).AddTicks(-1));
            }

            var sql = $@"
                SELECT 
                    Id, EmpresaId, ClienteId, ProveedorId, TiendaId, CajaId,
                    Fecha, Monto, Concepto, Referencia, Nota,
                    UsuarioRegistradorId, PorcentajeComision, MontoComision, MontoNetoProveedor,
                    Reversado, ReversadoUtc, ReversadoPorUsuarioId
                FROM Consumos
                {whereClause}
                ORDER BY Fecha DESC, Id DESC";

            using var connection = _connectionFactory.Create();
            var result = await connection.QueryAsync<Consumo>(sql, parameters);
            return result.ToList();
        }

        public async Task<IReadOnlyList<Consumo>> GetConsumosByClienteIdAsync(int clienteId)
        {
            const string sql = @"
                SELECT 
                    Id, EmpresaId, ClienteId, ProveedorId, TiendaId, CajaId,
                    Fecha, Monto, Concepto, Referencia, Nota,
                    UsuarioRegistradorId, PorcentajeComision, MontoComision, MontoNetoProveedor,
                    Reversado, ReversadoUtc, ReversadoPorUsuarioId
                FROM Consumos
                WHERE ClienteId = @ClienteId
                ORDER BY Fecha DESC, Id DESC";

            using var connection = _connectionFactory.Create();
            var result = await connection.QueryAsync<Consumo>(sql, new { ClienteId = clienteId });
            return result.ToList();
        }

        public async Task<IReadOnlyList<Consumo>> GetConsumosByClienteCedulaAsync(string cedula)
        {
            const string sql = @"
                SELECT 
                    c.Id, c.EmpresaId, c.ClienteId, c.ProveedorId, c.TiendaId, c.CajaId,
                    c.Fecha, c.Monto, c.Concepto, c.Referencia, c.Nota,
                    c.UsuarioRegistradorId, c.PorcentajeComision, c.MontoComision, c.MontoNetoProveedor,
                    c.Reversado, c.ReversadoUtc, c.ReversadoPorUsuarioId
                FROM Consumos c
                INNER JOIN Clientes cli ON c.ClienteId = cli.Id
                WHERE cli.Cedula = @Cedula
                ORDER BY c.Fecha DESC, c.Id DESC";

            using var connection = _connectionFactory.Create();
            var result = await connection.QueryAsync<Consumo>(sql, new { Cedula = cedula });
            return result.ToList();
        }

        #region Helpers

        private static string GenerarSecuencia(int numero)
        {
            var letra = (char)('A' + (numero - 1) / 9_999_999);
            return $"{letra}{numero:00000000}";
        }

        #endregion
    }
}