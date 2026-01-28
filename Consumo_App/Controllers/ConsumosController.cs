using Dapper;
using Consumo_App.Data.Sql;
using Consumo_App.DTOs;
using Consumo_App.Models;
using Consumo_App.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ConsumosController : ControllerBase
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly IUserContext _user;

        public ConsumosController(SqlConnectionFactory connectionFactory, IUserContext user)
        {
            _connectionFactory = connectionFactory;
            _user = user;
        }

        #region Listados y Consultas

        /// <summary>
        /// GET /api/consumos?q=&amp;desde=&amp;hasta=&amp;page=&amp;pageSize=
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<PagedResult<ConsumoListDto>>> List(
            [FromQuery] string? q,
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            using var connection = _connectionFactory.Create();

            var whereClause = "WHERE 1=1";
            var parameters = new DynamicParameters();

            if (desde.HasValue)
            {
                whereClause += " AND c.Fecha >= @Desde";
                parameters.Add("Desde", desde.Value);
            }

            if (hasta.HasValue)
            {
                whereClause += " AND c.Fecha <= @Hasta";
                parameters.Add("Hasta", hasta.Value);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                whereClause += @" AND (
                    c.Concepto LIKE @Criterio OR 
                    c.Referencia LIKE @Criterio OR 
                    cli.Nombre LIKE @Criterio)";
                parameters.Add("Criterio", $"%{q.Trim()}%");
            }

            // Contar total
            var countSql = $@"
                SELECT COUNT(*)
                FROM Consumos c
                INNER JOIN Clientes cli ON c.ClienteId = cli.Id
                INNER JOIN Proveedores p ON c.ProveedorId = p.Id
                {whereClause}";

            var total = await connection.ExecuteScalarAsync<int>(countSql, parameters);

            // Obtener datos paginados
            var offset = (page - 1) * pageSize;
            parameters.Add("Offset", offset);
            parameters.Add("PageSize", pageSize);

            var dataSql = $@"
                SELECT 
                    c.Id,
                    c.Fecha,
                    c.EmpresaId,
                    cli.Nombre AS ClienteNombre,
                    p.Nombre AS ProveedorNombre,
                    c.Monto,
                    c.Referencia
                FROM Consumos c
                INNER JOIN Clientes cli ON c.ClienteId = cli.Id
                INNER JOIN Proveedores p ON c.ProveedorId = p.Id
                {whereClause}
                ORDER BY c.Fecha DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var data = (await connection.QueryAsync<ConsumoListDto>(dataSql, parameters)).ToList();

            return Ok(new PagedResult<ConsumoListDto>
            {
                Data = data,
                Total = total,
                Page = page,
                PageSize = pageSize
            });
        }

        /// <summary>
        /// GET /api/consumos/{id}
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<ActionResult<ConsumoDetailDto>> GetById(int id)
        {
            const string sql = @"
                SELECT 
                    c.Id,
                    c.EmpresaId,
                    c.ClienteId,
                    ISNULL(cli.Nombre, '') AS ClienteNombre,
                    c.ProveedorId,
                    ISNULL(p.Nombre, '') AS ProveedorNombre,
                    c.Fecha,
                    c.Monto,
                    c.Concepto
                FROM Consumos c
                LEFT JOIN Clientes cli ON c.ClienteId = cli.Id
                LEFT JOIN Proveedores p ON c.ProveedorId = p.Id
                WHERE c.Id = @Id";

            using var connection = _connectionFactory.Create();
            var result = await connection.QueryFirstOrDefaultAsync<ConsumoDetailDto>(sql, new { Id = id });

            if (result is null) return NotFound();

            return Ok(result);
        }

        /// <summary>
        /// GET /api/consumos/{id}/detalle-comision
        /// </summary>
        [HttpGet("{id:int}/detalle-comision")]
        public async Task<IActionResult> GetDetalleConComision(int id)
        {
            const string sql = @"
                SELECT 
                    c.Id,
                    c.Fecha,
                    ISNULL(cli.Nombre, '') AS ClienteNombre,
                    ISNULL(p.Nombre, '') AS ProveedorNombre,
                    c.Concepto,
                    c.Referencia,
                    c.Monto AS MontoBruto,
                    c.PorcentajeComision,
                    c.MontoComision,
                    c.MontoNetoProveedor,
                    c.Reversado,
                    c.ReversadoUtc
                FROM Consumos c
                LEFT JOIN Clientes cli ON c.ClienteId = cli.Id
                LEFT JOIN Proveedores p ON c.ProveedorId = p.Id
                WHERE c.Id = @Id";

            using var connection = _connectionFactory.Create();
            var result = await connection.QueryFirstOrDefaultAsync(sql, new { Id = id });

            if (result is null) return NotFound();

            return Ok(result);
        }

        #endregion

        #region Registrar Consumo

        [HttpPost]
        public async Task<IActionResult> Registrar([FromBody] RegistrarConsumoDto dto)
        {
            var uid = _user.Id;

            using var connection = _connectionFactory.Create();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                // 1) Validar que Caja existe y pertenece a la jerarquía
                const string sqlCaja = @"
                    SELECT c.Id, c.TiendaId, t.ProveedorId
                    FROM ProveedorCajas c
                    INNER JOIN ProveedorTiendas t ON c.TiendaId = t.Id
                    WHERE c.Id = @CajaId";

                var caja = await connection.QueryFirstOrDefaultAsync<dynamic>(
                    sqlCaja, new { dto.CajaId }, transaction);

                if (caja == null)
                    return BadRequest(new { message = "La caja especificada no existe." });

                if ((int)caja.TiendaId != dto.TiendaId)
                    return BadRequest(new { message = "La caja no pertenece a la tienda indicada." });

                if ((int)caja.ProveedorId != dto.ProveedorId)
                    return BadRequest(new { message = "La tienda no pertenece al proveedor indicado." });

                // 2) Validar autorización del usuario
                const string sqlAutorizado = @"
                    SELECT COUNT(1) FROM ProveedorAsignaciones
                    WHERE UsuarioId = @UsuarioId
                      AND ProveedorId = @ProveedorId
                      AND Activo = 1
                      AND (
                          (TiendaId IS NULL AND CajaId IS NULL) OR
                          (TiendaId = @TiendaId AND CajaId IS NULL) OR
                          (TiendaId = @TiendaId AND CajaId = @CajaId)
                      )";

                var autorizado = await connection.ExecuteScalarAsync<int>(sqlAutorizado, new
                {
                    UsuarioId = uid,
                    dto.ProveedorId,
                    dto.TiendaId,
                    dto.CajaId
                }, transaction) > 0;

                if (!autorizado) return Forbid();

                // 3) Validar cliente y obtener empresa
                const string sqlCliente = @"
                    SELECT 
                        c.Id, c.Nombre, c.Saldo, c.SaldoOriginal, c.Activo, c.EmpresaId,
                        e.Id AS EmpresaId, e.Nombre AS EmpresaNombre, e.Activo AS EmpresaActiva, e.LimiteCredito
                    FROM Clientes c
                    INNER JOIN Empresas e ON c.EmpresaId = e.Id
                    WHERE c.Id = @ClienteId AND c.Activo = 1";

                var clienteData = await connection.QueryFirstOrDefaultAsync<dynamic>(
                    sqlCliente, new { dto.ClienteId }, transaction);

                if (clienteData == null)
                    return BadRequest(new { message = "Cliente inválido o inactivo." });

                if (!(bool)clienteData.EmpresaActiva)
                    return BadRequest(new { message = "La empresa del cliente está inactiva." });

                decimal saldoCliente = clienteData.Saldo;
                int empresaId = clienteData.EmpresaId;
                decimal limiteCredito = clienteData.LimiteCredito ?? 0m;

                // 4) Validar saldo disponible del cliente
                if (saldoCliente < dto.Monto)
                {
                    return BadRequest(new
                    {
                        message = "Saldo insuficiente.",
                        saldoDisponible = saldoCliente,
                        montoSolicitado = dto.Monto,
                        diferencia = dto.Monto - saldoCliente
                    });
                }

                // 5) Validar límite de crédito de la empresa
                const string sqlConsumoEmpresa = @"
                    SELECT ISNULL(SUM(Monto), 0) 
                    FROM Consumos 
                    WHERE EmpresaId = @EmpresaId AND Reversado = 0";

                var consumoActualEmpresa = await connection.ExecuteScalarAsync<decimal>(
                    sqlConsumoEmpresa, new { EmpresaId = empresaId }, transaction);

                var nuevoTotalEmpresa = consumoActualEmpresa + dto.Monto;

                if (limiteCredito > 0 && nuevoTotalEmpresa > limiteCredito)
                {
                    return BadRequest(new
                    {
                        message = "La empresa ha excedido su límite de crédito.",
                        limiteCredito,
                        consumoActual = consumoActualEmpresa,
                        montoSolicitado = dto.Monto,
                        excedente = nuevoTotalEmpresa - limiteCredito
                    });
                }

                // 6) Obtener porcentaje de comisión del proveedor
                const string sqlProveedor = "SELECT PorcentajeComision FROM Proveedores WHERE Id = @ProveedorId";
                var porcentajeComision = await connection.ExecuteScalarAsync<decimal?>(
                    sqlProveedor, new { dto.ProveedorId }, transaction);

                if (porcentajeComision == null)
                    return BadRequest(new { message = "Proveedor no encontrado." });

                decimal montoComision = dto.Monto * porcentajeComision.Value / 100;
                decimal montoNetoProveedor = dto.Monto - montoComision;

                // 7) Insertar consumo
                const string sqlInsertConsumo = @"
                    INSERT INTO Consumos (
                        ClienteId, EmpresaId, ProveedorId, TiendaId, CajaId,
                        Monto, Nota, Concepto, Referencia, UsuarioRegistradorId, Fecha,
                        PorcentajeComision, MontoComision, MontoNetoProveedor,
                        Reversado
                    )
                    OUTPUT INSERTED.Id
                    VALUES (
                        @ClienteId, @EmpresaId, @ProveedorId, @TiendaId, @CajaId,
                        @Monto, @Nota, @Concepto, @Referencia, @UsuarioRegistradorId, @Fecha,
                        @PorcentajeComision, @MontoComision, @MontoNetoProveedor,
                        0
                    )";

                var consumoId = await connection.ExecuteScalarAsync<int>(sqlInsertConsumo, new
                {
                    dto.ClienteId,
                    EmpresaId = empresaId,
                    dto.ProveedorId,
                    dto.TiendaId,
                    dto.CajaId,
                    dto.Monto,
                    dto.Nota,
                    dto.Concepto,
                    dto.Referencia,
                    UsuarioRegistradorId = uid,
                    Fecha = DateTime.UtcNow,
                    PorcentajeComision = porcentajeComision.Value,
                    MontoComision = montoComision,
                    MontoNetoProveedor = montoNetoProveedor
                }, transaction);

                // 8) Descontar saldo del cliente
                const string sqlUpdateSaldo = "UPDATE Clientes SET Saldo = Saldo - @Monto WHERE Id = @ClienteId";
                await connection.ExecuteAsync(sqlUpdateSaldo, new { dto.Monto, dto.ClienteId }, transaction);

                var nuevoSaldo = saldoCliente - dto.Monto;

                transaction.Commit();

                return Ok(new
                {
                    Id = consumoId,
                    mensaje = "Consumo registrado exitosamente.",
                    nuevoSaldoCliente = nuevoSaldo,
                    montoConsumido = dto.Monto
                });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                Console.WriteLine($"ERROR Registrar: {ex.Message}");
                return StatusCode(500, new { message = "Error interno al registrar el consumo." });
            }
        }

        #endregion

        #region Reversar Consumo

        [HttpPost("{id:int}/reversar")]
        public async Task<IActionResult> Reversar(int id, [FromBody] ReversarConsumoDto? dto = null)
        {
            var uid = _user.Id;

            using var connection = _connectionFactory.Create();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                // 1) Buscar el consumo
                const string sqlConsumo = @"
                    SELECT 
                        c.Id, c.ClienteId, c.ProveedorId, c.TiendaId, c.CajaId,
                        c.Monto, c.Reversado, c.ReversadoUtc, c.ReversadoPorUsuarioId,
                        cli.Saldo AS ClienteSaldo, cli.SaldoOriginal AS ClienteSaldoOriginal
                    FROM Consumos c
                    LEFT JOIN Clientes cli ON c.ClienteId = cli.Id
                    WHERE c.Id = @Id";

                var consumo = await connection.QueryFirstOrDefaultAsync<dynamic>(
                    sqlConsumo, new { Id = id }, transaction);

                if (consumo == null)
                    return NotFound(new { message = "Consumo no encontrado." });

                if ((bool)consumo.Reversado)
                {
                    return BadRequest(new
                    {
                        message = "Este consumo ya fue reversado.",
                        reversadoEn = consumo.ReversadoUtc,
                        reversadoPor = consumo.ReversadoPorUsuarioId
                    });
                }

                // 2) Validar autorización
                const string sqlAutorizado = @"
                    SELECT COUNT(1) FROM ProveedorAsignaciones
                    WHERE UsuarioId = @UsuarioId
                      AND ProveedorId = @ProveedorId
                      AND Activo = 1
                      AND (
                          (TiendaId IS NULL AND CajaId IS NULL) OR
                          (TiendaId = @TiendaId AND CajaId IS NULL) OR
                          (TiendaId = @TiendaId AND CajaId = @CajaId)
                      )";

                var autorizado = await connection.ExecuteScalarAsync<int>(sqlAutorizado, new
                {
                    UsuarioId = uid,
                    ProveedorId = (int)consumo.ProveedorId,
                    TiendaId = (int?)consumo.TiendaId,
                    CajaId = (int?)consumo.CajaId
                }, transaction) > 0;

                if (!autorizado) return Forbid();

                // 3) Marcar como reversado
                const string sqlReversar = @"
                    UPDATE Consumos 
                    SET Reversado = 1, 
                        ReversadoUtc = @ReversadoUtc, 
                        ReversadoPorUsuarioId = @ReversadoPorUsuarioId
                    WHERE Id = @Id";

                await connection.ExecuteAsync(sqlReversar, new
                {
                    Id = id,
                    ReversadoUtc = DateTime.UtcNow,
                    ReversadoPorUsuarioId = uid
                }, transaction);

                // 4) Restaurar saldo del cliente
                decimal nuevoSaldo = 0;
                if (consumo.ClienteId != null)
                {
                    decimal saldoActual = consumo.ClienteSaldo ?? 0;
                    decimal saldoOriginal = consumo.ClienteSaldoOriginal ?? 0;
                    decimal monto = consumo.Monto;

                    nuevoSaldo = Math.Min(saldoActual + monto, saldoOriginal);

                    const string sqlUpdateSaldo = "UPDATE Clientes SET Saldo = @NuevoSaldo WHERE Id = @ClienteId";
                    await connection.ExecuteAsync(sqlUpdateSaldo, new
                    {
                        NuevoSaldo = nuevoSaldo,
                        ClienteId = (int)consumo.ClienteId
                    }, transaction);
                }

                transaction.Commit();

                return Ok(new
                {
                    mensaje = "Consumo reversado exitosamente.",
                    consumoId = id,
                    montoDevuelto = (decimal)consumo.Monto,
                    nuevoSaldoCliente = nuevoSaldo
                });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                Console.WriteLine($"ERROR Reversar: {ex.Message}");
                return StatusCode(500, new { message = "Error interno al reversar el consumo." });
            }
        }

        #endregion

        #region Consultas de Cliente

        /// <summary>
        /// GET /api/consumos/cliente/{clienteId}/saldo
        /// </summary>
        [HttpGet("cliente/{clienteId:int}/saldo")]
        public async Task<IActionResult> ObtenerSaldoCliente(int clienteId)
        {
            const string sqlCliente = @"
                SELECT 
                    c.Id, c.Nombre, c.Cedula, c.Saldo, c.SaldoOriginal, c.EmpresaId,
                    e.Nombre AS EmpresaNombre, e.LimiteCredito AS EmpresaLimiteCredito
                FROM Clientes c
                LEFT JOIN Empresas e ON c.EmpresaId = e.Id
                WHERE c.Id = @ClienteId";

            using var connection = _connectionFactory.Create();
            var cliente = await connection.QueryFirstOrDefaultAsync<dynamic>(sqlCliente, new { ClienteId = clienteId });

            if (cliente == null)
                return NotFound(new { message = "Cliente no encontrado." });

            const string sqlConsumoEmpresa = @"
                SELECT ISNULL(SUM(Monto), 0) 
                FROM Consumos 
                WHERE EmpresaId = @EmpresaId AND Reversado = 0";

            var consumoActualEmpresa = await connection.ExecuteScalarAsync<decimal>(
                sqlConsumoEmpresa, new { EmpresaId = (int)cliente.EmpresaId });

            decimal limiteEmpresa = cliente.EmpresaLimiteCredito ?? 0m;
            decimal disponibleEmpresa = limiteEmpresa > 0 ? limiteEmpresa - consumoActualEmpresa : decimal.MaxValue;

            return Ok(new
            {
                clienteId = (int)cliente.Id,
                clienteNombre = (string)cliente.Nombre,
                saldoDisponible = (decimal)cliente.Saldo,
                limiteCredito = (decimal)cliente.SaldoOriginal,
                saldoUtilizado = (decimal)cliente.SaldoOriginal - (decimal)cliente.Saldo,
                empresaId = (int)cliente.EmpresaId,
                empresaNombre = (string?)cliente.EmpresaNombre ?? "",
                empresaLimiteCredito = limiteEmpresa,
                empresaConsumoActual = consumoActualEmpresa,
                empresaDisponible = disponibleEmpresa > 0 ? disponibleEmpresa : 0,
                montoMaximoDisponible = Math.Min((decimal)cliente.Saldo, disponibleEmpresa > 0 ? disponibleEmpresa : (decimal)cliente.Saldo)
            });
        }

        /// <summary>
        /// GET /api/consumos/cliente/{clienteId}/historial
        /// </summary>
        [HttpGet("cliente/{clienteId:int}/historial")]
        public async Task<IActionResult> HistorialCliente(
            int clienteId,
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            using var connection = _connectionFactory.Create();

            // Verificar cliente
            const string sqlCliente = @"
                SELECT c.Id, c.Nombre, c.Cedula, c.Saldo, c.SaldoOriginal, c.EmpresaId,
                       e.Nombre AS EmpresaNombre
                FROM Clientes c
                LEFT JOIN Empresas e ON c.EmpresaId = e.Id
                WHERE c.Id = @ClienteId";

            var cliente = await connection.QueryFirstOrDefaultAsync<dynamic>(sqlCliente, new { ClienteId = clienteId });

            if (cliente == null)
                return NotFound(new { message = "Cliente no encontrado." });

            // Construir filtros
            var whereClause = "WHERE c.ClienteId = @ClienteId";
            var parameters = new DynamicParameters();
            parameters.Add("ClienteId", clienteId);

            if (desde.HasValue)
            {
                whereClause += " AND c.Fecha >= @Desde";
                parameters.Add("Desde", desde.Value);
            }

            if (hasta.HasValue)
            {
                whereClause += " AND c.Fecha <= @Hasta";
                parameters.Add("Hasta", hasta.Value);
            }

            // Contar total
            var countSql = $"SELECT COUNT(*) FROM Consumos c {whereClause}";
            var total = await connection.ExecuteScalarAsync<int>(countSql, parameters);

            // Obtener consumos paginados
            parameters.Add("Offset", (page - 1) * pageSize);
            parameters.Add("PageSize", pageSize);

            var dataSql = $@"
                SELECT 
                    c.Id, c.Fecha, c.Monto, c.Concepto, c.Referencia,
                    p.Nombre AS ProveedorNombre,
                    t.Nombre AS TiendaNombre,
                    c.Reversado, c.ReversadoUtc
                FROM Consumos c
                LEFT JOIN Proveedores p ON c.ProveedorId = p.Id
                LEFT JOIN ProveedorTiendas t ON c.TiendaId = t.Id
                {whereClause}
                ORDER BY c.Fecha DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var consumos = await connection.QueryAsync(dataSql, parameters);

            // Totales
            var sqlTotalConsumos = @"
                SELECT ISNULL(SUM(Monto), 0) FROM Consumos 
                WHERE ClienteId = @ClienteId AND Reversado = 0";
            var totalConsumos = await connection.ExecuteScalarAsync<decimal>(
                sqlTotalConsumos, new { ClienteId = clienteId });

            var sqlTotalReversas = @"
                SELECT ISNULL(SUM(Monto), 0) FROM Consumos 
                WHERE ClienteId = @ClienteId AND Reversado = 1";
            var totalReversas = await connection.ExecuteScalarAsync<decimal>(
                sqlTotalReversas, new { ClienteId = clienteId });

            return Ok(new
            {
                cliente = new
                {
                    Id = (int)cliente.Id,
                    Nombre = (string)cliente.Nombre,
                    Cedula = (string?)cliente.Cedula,
                    Saldo = (decimal)cliente.Saldo,
                    LimiteCredito = (decimal)cliente.SaldoOriginal,
                    EmpresaNombre = (string?)cliente.EmpresaNombre ?? ""
                },
                resumen = new
                {
                    TotalConsumido = totalConsumos,
                    TotalReversado = totalReversas,
                    Neto = totalConsumos - totalReversas
                },
                consumos,
                pagination = new
                {
                    total,
                    page,
                    pageSize,
                    totalPages = (int)Math.Ceiling(total / (double)pageSize)
                }
            });
        }

        #endregion
    }

    public record ReversarConsumoDto(string? Motivo = null);
}