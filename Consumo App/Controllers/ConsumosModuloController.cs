using Consumo_App.Data;
using Consumo_App.Data.Sql;
using Consumo_App.Servicios;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/modulo-consumos")]
    [Authorize]
    public class ConsumosModuloController : ControllerBase
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly IUserContext _user;

        public ConsumosModuloController(SqlConnectionFactory connectionFactory, IUserContext user)
        {
            _connectionFactory = connectionFactory;
            _user = user;
        }

        #region Dashboard

        /// <summary>
        /// Dashboard principal de consumos
        /// GET /api/modulo-consumos/dashboard
        /// </summary>
        [HttpGet("dashboard")]
        public async Task<IActionResult> Dashboard([FromQuery] DateTime? fecha = null)
        {
            try
            {
                using var connection = _connectionFactory.Create();

                var fechaConsulta = fecha?.Date ?? DateTime.UtcNow.Date;
                var inicioSemana = fechaConsulta.AddDays(-(int)fechaConsulta.DayOfWeek);

                // Estadísticas del día
                var resumenDia = await connection.QueryFirstOrDefaultAsync<ResumenDiaDto>(
                    @"SELECT 
                        COUNT(*) AS TotalConsumos,
                        ISNULL(SUM(Monto), 0) AS MontoTotal,
                        SUM(CASE WHEN Reversado = 1 THEN 1 ELSE 0 END) AS TotalReversos,
                        ISNULL(SUM(CASE WHEN Reversado = 1 THEN Monto ELSE 0 END), 0) AS MontoReversado
                    FROM Consumos
                    WHERE CAST(Fecha AS DATE) = @fecha",
                    new { fecha = fechaConsulta });

                // Consumos últimos 7 días
                var consumosPorDia = await connection.QueryAsync<ConsumoPorDiaDto>(
                    @"SELECT 
                        CAST(Fecha AS DATE) AS Fecha,
                        COUNT(*) AS Cantidad,
                        ISNULL(SUM(CASE WHEN Reversado = 0 THEN Monto ELSE 0 END), 0) AS Monto
                    FROM Consumos
                    WHERE Fecha >= @inicio AND Fecha < @fin
                    GROUP BY CAST(Fecha AS DATE)
                    ORDER BY Fecha",
                    new { inicio = inicioSemana, fin = fechaConsulta.AddDays(1) });

                // Top 5 empresas del día
                var topEmpresas = await connection.QueryAsync<TopEmpresaDto>(
                    @"SELECT TOP 5
                        e.Id AS EmpresaId,
                        e.Nombre AS EmpresaNombre,
                        COUNT(*) AS CantidadConsumos,
                        ISNULL(SUM(CASE WHEN c.Reversado = 0 THEN c.Monto ELSE 0 END), 0) AS MontoTotal
                    FROM Consumos c
                    INNER JOIN Empresas e ON c.EmpresaId = e.Id
                    WHERE CAST(c.Fecha AS DATE) = @fecha
                    GROUP BY e.Id, e.Nombre
                    ORDER BY MontoTotal DESC",
                    new { fecha = fechaConsulta });

                // Top 5 clientes del día
                var topClientes = await connection.QueryAsync<TopClienteDto>(
                    @"SELECT TOP 5
                        cl.Id AS ClienteId,
                        cl.Nombre AS ClienteNombre,
                        cl.Cedula AS ClienteCedula,
                        e.Nombre AS EmpresaNombre,
                        COUNT(*) AS CantidadConsumos,
                        ISNULL(SUM(CASE WHEN c.Reversado = 0 THEN c.Monto ELSE 0 END), 0) AS MontoTotal
                    FROM Consumos c
                    INNER JOIN Clientes cl ON c.ClienteId = cl.Id
                    INNER JOIN Empresas e ON c.EmpresaId = e.Id
                    WHERE CAST(c.Fecha AS DATE) = @fecha
                    GROUP BY cl.Id, cl.Nombre, cl.Cedula, e.Nombre
                    ORDER BY MontoTotal DESC",
                    new { fecha = fechaConsulta });

                // Últimos 10 consumos
                var ultimosConsumos = await connection.QueryAsync<UltimoConsumoDto>(
                    @"SELECT TOP 10
                        c.Id,
                        c.Fecha,
                        cl.Nombre AS ClienteNombre,
                        cl.Cedula AS ClienteCedula,
                        e.Nombre AS EmpresaNombre,
                        p.Nombre AS ProveedorNombre,
                        t.Nombre AS TiendaNombre,
                        c.Monto,
                        c.Concepto,
                        c.Reversado
                    FROM Consumos c
                    INNER JOIN Clientes cl ON c.ClienteId = cl.Id
                    INNER JOIN Empresas e ON c.EmpresaId = e.Id
                    INNER JOIN Proveedores p ON c.ProveedorId = p.Id
                    LEFT JOIN ProveedorTiendas t ON c.TiendaId = t.Id
                    WHERE CAST(c.Fecha AS DATE) = @fecha
                    ORDER BY c.Fecha DESC",
                    new { fecha = fechaConsulta });

                return Ok(new
                {
                    FechaConsulta = fechaConsulta,
                    Resumen = new
                    {
                        ConsumosHoy = resumenDia?.TotalConsumos ?? 0,
                        MontoHoy = resumenDia?.MontoTotal ?? 0,
                        ReversosHoy = resumenDia?.TotalReversos ?? 0,
                        MontoReversadoHoy = resumenDia?.MontoReversado ?? 0,
                        MontoNetoHoy = (resumenDia?.MontoTotal ?? 0) - (resumenDia?.MontoReversado ?? 0)
                    },
                    ConsumosPorDia = consumosPorDia,
                    TopEmpresas = topEmpresas,
                    TopClientes = topClientes,
                    UltimosConsumos = ultimosConsumos
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR Dashboard Consumos: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "Error interno al cargar el dashboard.", error = ex.Message });
            }
        }

        #endregion

        #region Listado de Consumos

        /// <summary>
        /// GET /api/modulo-consumos/lista
        /// </summary>
        [HttpGet("lista")]
        public async Task<IActionResult> Listar(
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta,
            [FromQuery] int? empresaId,
            [FromQuery] int? clienteId,
            [FromQuery] int? proveedorId,
            [FromQuery] int? tiendaId,
            [FromQuery] int? usuarioRegistradorId,
            [FromQuery] bool? soloMisConsumos,
            [FromQuery] bool? soloReversados,
            [FromQuery] string? busqueda,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            try
            {
                using var connection = _connectionFactory.Create();

                var whereBuilder = new StringBuilder();
                var parameters = new DynamicParameters();

                if (desde.HasValue)
                {
                    whereBuilder.Append(" AND c.Fecha >= @desde");
                    parameters.Add("desde", desde.Value);
                }

                if (hasta.HasValue)
                {
                    whereBuilder.Append(" AND c.Fecha < @hasta");
                    parameters.Add("hasta", hasta.Value.AddDays(1));
                }

                if (empresaId.HasValue)
                {
                    whereBuilder.Append(" AND c.EmpresaId = @empresaId");
                    parameters.Add("empresaId", empresaId.Value);
                }

                if (clienteId.HasValue)
                {
                    whereBuilder.Append(" AND c.ClienteId = @clienteId");
                    parameters.Add("clienteId", clienteId.Value);
                }

                if (proveedorId.HasValue)
                {
                    whereBuilder.Append(" AND c.ProveedorId = @proveedorId");
                    parameters.Add("proveedorId", proveedorId.Value);
                }

                if (tiendaId.HasValue)
                {
                    whereBuilder.Append(" AND c.TiendaId = @tiendaId");
                    parameters.Add("tiendaId", tiendaId.Value);
                }

                if (usuarioRegistradorId.HasValue)
                {
                    whereBuilder.Append(" AND c.UsuarioRegistradorId = @usuarioRegistradorId");
                    parameters.Add("usuarioRegistradorId", usuarioRegistradorId.Value);
                }

                if (soloMisConsumos == true)
                {
                    whereBuilder.Append(" AND c.UsuarioRegistradorId = @currentUserId");
                    parameters.Add("currentUserId", _user.Id);
                }

                if (soloReversados.HasValue)
                {
                    whereBuilder.Append(" AND c.Reversado = @reversado");
                    parameters.Add("reversado", soloReversados.Value);
                }

                if (!string.IsNullOrWhiteSpace(busqueda))
                {
                    whereBuilder.Append(" AND (cl.Nombre LIKE @busqueda OR cl.Cedula LIKE @busqueda OR c.Concepto LIKE @busqueda OR c.Referencia LIKE @busqueda)");
                    parameters.Add("busqueda", $"%{busqueda}%");
                }

                var whereClause = whereBuilder.Length > 0 ? "WHERE 1=1" + whereBuilder.ToString() : "";

                // Contar total y resumen
                var resumen = await connection.QueryFirstOrDefaultAsync<ResumenListaDto>(
                    $@"SELECT 
                        COUNT(*) AS Total,
                        ISNULL(SUM(CASE WHEN c.Reversado = 0 THEN c.Monto ELSE 0 END), 0) AS MontoActivo,
                        ISNULL(SUM(CASE WHEN c.Reversado = 1 THEN c.Monto ELSE 0 END), 0) AS MontoReversado
                    FROM Consumos c
                    INNER JOIN Clientes cl ON c.ClienteId = cl.Id
                    {whereClause}",
                    parameters);

                // Agregar parámetros de paginación
                parameters.Add("offset", (page - 1) * pageSize);
                parameters.Add("pageSize", pageSize);

                // Obtener datos paginados
                var data = await connection.QueryAsync<ConsumoListaDto>(
                    $@"SELECT 
                        c.Id,
                        c.Fecha,
                        c.ClienteId,
                        cl.Nombre AS ClienteNombre,
                        cl.Cedula AS ClienteCedula,
                        c.EmpresaId,
                        e.Nombre AS EmpresaNombre,
                        c.ProveedorId,
                        p.Nombre AS ProveedorNombre,
                        c.TiendaId,
                        t.Nombre AS TiendaNombre,
                        c.CajaId,
                        c.Monto,
                        c.Concepto,
                        c.Referencia,
                        c.Reversado,
                        c.ReversadoUtc,
                        c.UsuarioRegistradorId,
                        ur.Nombre AS RegistradoPor
                    FROM Consumos c
                    INNER JOIN Clientes cl ON c.ClienteId = cl.Id
                    INNER JOIN Empresas e ON c.EmpresaId = e.Id
                    INNER JOIN Proveedores p ON c.ProveedorId = p.Id
                    LEFT JOIN ProveedorTiendas t ON c.TiendaId = t.Id
                    LEFT JOIN Usuarios ur ON c.UsuarioRegistradorId = ur.Id
                    {whereClause}
                    ORDER BY c.Fecha DESC
                    OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY",
                    parameters);

                var totalCount = resumen?.Total ?? 0;

                return Ok(new
                {
                    Data = data,
                    Resumen = new
                    {
                        TotalConsumos = totalCount,
                        MontoActivo = resumen?.MontoActivo ?? 0,
                        MontoReversado = resumen?.MontoReversado ?? 0,
                        MontoNeto = resumen?.MontoActivo ?? 0
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
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR Listar Consumos: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "Error interno al listar consumos.", error = ex.Message });
            }
        }

        #endregion

        #region Detalle de Consumo

        /// <summary>
        /// GET /api/modulo-consumos/{id}
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> Detalle(int id)
        {
            try
            {
                using var connection = _connectionFactory.Create();

                var consumo = await connection.QueryFirstOrDefaultAsync<ConsumoDetalleDto>(
                    @"SELECT 
                        c.Id,
                        c.Fecha,
                        c.ClienteId,
                        cl.Nombre AS ClienteNombre,
                        cl.Cedula AS ClienteCedula,
                        cl.Saldo AS ClienteSaldoActual,
                        cl.SaldoOriginal AS ClienteLimite,
                        c.EmpresaId,
                        e.Nombre AS EmpresaNombre,
                        e.Rnc AS EmpresaRnc,
                        c.ProveedorId,
                        p.Nombre AS ProveedorNombre,
                        c.TiendaId,
                        t.Nombre AS TiendaNombre,
                        c.CajaId,
                        ca.Nombre AS CajaNombre,
                        c.Monto,
                        c.Concepto,
                        c.Nota,
                        c.Referencia,
                        c.Reversado,
                        c.ReversadoUtc,
                        c.ReversadoPorUsuarioId,
                        ur.Nombre AS ReversadoPor,
                        c.MotivoReverso,
                        c.UsuarioRegistradorId,
                        ureg.Nombre AS RegistradoPor
                    FROM Consumos c
                    INNER JOIN Clientes cl ON c.ClienteId = cl.Id
                    INNER JOIN Empresas e ON c.EmpresaId = e.Id
                    INNER JOIN Proveedores p ON c.ProveedorId = p.Id
                    LEFT JOIN ProveedorTiendas t ON c.TiendaId = t.Id
                    LEFT JOIN ProveedorCajas ca ON c.CajaId = ca.Id
                    LEFT JOIN Usuarios ur ON c.ReversadoPorUsuarioId = ur.Id
                    LEFT JOIN Usuarios ureg ON c.UsuarioRegistradorId = ureg.Id
                    WHERE c.Id = @id",
                    new { id });

                if (consumo == null)
                    return NotFound(new { message = "Consumo no encontrado." });

                var puedeReversarse = !consumo.Reversado && (DateTime.UtcNow - consumo.Fecha).TotalHours <= 24;

                return Ok(new
                {
                    consumo.Id,
                    consumo.Fecha,
                    Cliente = new
                    {
                        Id = consumo.ClienteId,
                        Nombre = consumo.ClienteNombre,
                        Cedula = consumo.ClienteCedula,
                        SaldoActual = consumo.ClienteSaldoActual,
                        Limite = consumo.ClienteLimite
                    },
                    Empresa = new
                    {
                        Id = consumo.EmpresaId,
                        Nombre = consumo.EmpresaNombre,
                        Rnc = consumo.EmpresaRnc
                    },
                    Proveedor = new
                    {
                        Id = consumo.ProveedorId,
                        Nombre = consumo.ProveedorNombre
                    },
                    consumo.TiendaId,
                    consumo.TiendaNombre,
                    consumo.CajaId,
                    consumo.CajaNombre,
                    consumo.Monto,
                    consumo.Concepto,
                    consumo.Nota,
                    consumo.Referencia,
                    consumo.Reversado,
                    consumo.ReversadoUtc,
                    consumo.ReversadoPor,
                    consumo.MotivoReverso,
                    consumo.RegistradoPor,
                    PuedeReversarse = puedeReversarse,
                    HorasParaReversar = puedeReversarse ? Math.Max(0, 24 - (DateTime.UtcNow - consumo.Fecha).TotalHours) : 0
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR Detalle Consumo: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "Error interno al obtener el consumo.", error = ex.Message });
            }
        }

        #endregion

        #region Reversos

        /// <summary>
        /// POST /api/modulo-consumos/{id}/reversar
        /// </summary>
        [HttpPost("{id:int}/reversar")]
        public async Task<IActionResult> Reversar(int id, [FromBody] ReversarConsumoRequest? dto = null)
        {
            try
            {
                using var connection = _connectionFactory.Create();
                await connection.OpenAsync();
                using var transaction = connection.BeginTransaction();

                try
                {
                    // Obtener consumo con datos del cliente
                    var consumoInfo = await connection.QueryFirstOrDefaultAsync<ConsumoReversoInfoDto>(
                        @"SELECT c.Fecha, c.Reversado, c.Monto, c.ClienteId, 
                               cl.Saldo AS ClienteSaldo, cl.SaldoOriginal AS ClienteSaldoOriginal
                        FROM Consumos c
                        INNER JOIN Clientes cl ON c.ClienteId = cl.Id
                        WHERE c.Id = @id",
                        new { id },
                        transaction);

                    if (consumoInfo == null)
                        return NotFound(new { message = "Consumo no encontrado." });

                    if (consumoInfo.Reversado)
                        return BadRequest(new { message = "Este consumo ya fue reversado." });

                    var horasTranscurridas = (DateTime.UtcNow - consumoInfo.Fecha).TotalHours;
                    if (horasTranscurridas > 24)
                    {
                        return BadRequest(new
                        {
                            message = "No se puede reversar este consumo. Ha pasado el límite de 24 horas.",
                            horasTranscurridas = Math.Round(horasTranscurridas, 1),
                            limiteHoras = 24
                        });
                    }

                    // Marcar como reversado
                    await connection.ExecuteAsync(
                        @"UPDATE Consumos 
                        SET Reversado = 1,
                            ReversadoUtc = @reversadoUtc,
                            ReversadoPorUsuarioId = @usuarioId,
                            MotivoReverso = @motivo
                        WHERE Id = @id",
                        new
                        {
                            reversadoUtc = DateTime.UtcNow,
                            usuarioId = _user.Id,
                            motivo = dto?.Motivo,
                            id
                        },
                        transaction);

                    // Devolver saldo al cliente (sin exceder el saldo original)
                    var nuevoSaldo = Math.Min(consumoInfo.ClienteSaldo + consumoInfo.Monto, consumoInfo.ClienteSaldoOriginal);

                    await connection.ExecuteAsync(
                        @"UPDATE Clientes SET Saldo = @nuevoSaldo WHERE Id = @clienteId",
                        new { nuevoSaldo, clienteId = consumoInfo.ClienteId },
                        transaction);

                    transaction.Commit();

                    return Ok(new
                    {
                        mensaje = "Consumo reversado exitosamente.",
                        consumoId = id,
                        montoDevuelto = consumoInfo.Monto,
                        nuevoSaldoCliente = nuevoSaldo
                    });
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR Reversar Consumo: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "Error interno al reversar el consumo.", error = ex.Message });
            }
        }

        /// <summary>
        /// GET /api/modulo-consumos/reversos
        /// </summary>
        [HttpGet("reversos")]
        public async Task<IActionResult> ListarReversos(
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta,
            [FromQuery] int? empresaId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            try
            {
                using var connection = _connectionFactory.Create();

                var whereBuilder = new StringBuilder("WHERE c.Reversado = 1");
                var parameters = new DynamicParameters();

                if (desde.HasValue)
                {
                    whereBuilder.Append(" AND c.ReversadoUtc >= @desde");
                    parameters.Add("desde", desde.Value);
                }

                if (hasta.HasValue)
                {
                    whereBuilder.Append(" AND c.ReversadoUtc < @hasta");
                    parameters.Add("hasta", hasta.Value.AddDays(1));
                }

                if (empresaId.HasValue)
                {
                    whereBuilder.Append(" AND c.EmpresaId = @empresaId");
                    parameters.Add("empresaId", empresaId.Value);
                }

                var whereClause = whereBuilder.ToString();

                // Contar total
                var resumen = await connection.QueryFirstOrDefaultAsync<(int Total, decimal MontoTotal)>(
                    $@"SELECT COUNT(*) AS Total, ISNULL(SUM(c.Monto), 0) AS MontoTotal
                    FROM Consumos c
                    {whereClause}",
                    parameters);

                parameters.Add("offset", (page - 1) * pageSize);
                parameters.Add("pageSize", pageSize);

                var data = await connection.QueryAsync<ReversoListaDto>(
                    $@"SELECT 
                        c.Id,
                        c.Fecha AS FechaConsumo,
                        c.ReversadoUtc AS FechaReverso,
                        cl.Nombre AS ClienteNombre,
                        cl.Cedula AS ClienteCedula,
                        e.Nombre AS EmpresaNombre,
                        p.Nombre AS ProveedorNombre,
                        t.Nombre AS TiendaNombre,
                        c.Monto,
                        c.Concepto,
                        c.MotivoReverso,
                        ur.Nombre AS ReversadoPor
                    FROM Consumos c
                    INNER JOIN Clientes cl ON c.ClienteId = cl.Id
                    INNER JOIN Empresas e ON c.EmpresaId = e.Id
                    INNER JOIN Proveedores p ON c.ProveedorId = p.Id
                    LEFT JOIN ProveedorTiendas t ON c.TiendaId = t.Id
                    LEFT JOIN Usuarios ur ON c.ReversadoPorUsuarioId = ur.Id
                    {whereClause}
                    ORDER BY c.ReversadoUtc DESC
                    OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY",
                    parameters);

                return Ok(new
                {
                    Data = data,
                    Resumen = new
                    {
                        TotalReversos = resumen.Total,
                        MontoTotalReversado = resumen.MontoTotal
                    },
                    Pagination = new
                    {
                        Total = resumen.Total,
                        Page = page,
                        PageSize = pageSize,
                        TotalPages = (int)Math.Ceiling(resumen.Total / (double)pageSize)
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR Listar Reversos: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "Error interno al listar reversos.", error = ex.Message });
            }
        }

        #endregion

        #region Consumos por Empresa

        /// <summary>
        /// GET /api/modulo-consumos/por-empresa
        /// </summary>
        [HttpGet("por-empresa")]
        public async Task<IActionResult> PorEmpresa(
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta)
        {
            try
            {
                using var connection = _connectionFactory.Create();

                var whereBuilder = new StringBuilder();
                var parameters = new DynamicParameters();

                if (desde.HasValue)
                {
                    whereBuilder.Append(" AND c.Fecha >= @desde");
                    parameters.Add("desde", desde.Value);
                }

                if (hasta.HasValue)
                {
                    whereBuilder.Append(" AND c.Fecha < @hasta");
                    parameters.Add("hasta", hasta.Value.AddDays(1));
                }

                var whereClause = whereBuilder.Length > 0 ? "WHERE 1=1" + whereBuilder.ToString() : "";

                var data = await connection.QueryAsync<ConsumoPorEmpresaDto>(
                    $@"SELECT 
                        e.Id AS EmpresaId,
                        e.Nombre AS EmpresaNombre,
                        e.Rnc AS EmpresaRnc,
                        COUNT(*) AS TotalConsumos,
                        SUM(CASE WHEN c.Reversado = 0 THEN 1 ELSE 0 END) AS ConsumosActivos,
                        SUM(CASE WHEN c.Reversado = 1 THEN 1 ELSE 0 END) AS ConsumosReversados,
                        ISNULL(SUM(CASE WHEN c.Reversado = 0 THEN c.Monto ELSE 0 END), 0) AS MontoActivo,
                        ISNULL(SUM(CASE WHEN c.Reversado = 1 THEN c.Monto ELSE 0 END), 0) AS MontoReversado,
                        COUNT(DISTINCT c.ClienteId) AS ClientesUnicos
                    FROM Consumos c
                    INNER JOIN Empresas e ON c.EmpresaId = e.Id
                    {whereClause}
                    GROUP BY e.Id, e.Nombre, e.Rnc
                    ORDER BY MontoActivo DESC",
                    parameters);

                var dataList = data.ToList();

                return Ok(new
                {
                    Data = dataList,
                    Resumen = new
                    {
                        TotalEmpresas = dataList.Count,
                        TotalConsumos = dataList.Sum(x => x.TotalConsumos),
                        MontoTotal = dataList.Sum(x => x.MontoActivo),
                        MontoReversado = dataList.Sum(x => x.MontoReversado)
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR PorEmpresa: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "Error interno.", error = ex.Message });
            }
        }

        #endregion

        #region Consumos por Cliente

        /// <summary>
        /// GET /api/modulo-consumos/por-cliente
        /// </summary>
        [HttpGet("por-cliente")]
        public async Task<IActionResult> PorCliente(
            [FromQuery] int? empresaId,
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            try
            {
                using var connection = _connectionFactory.Create();

                var whereBuilder = new StringBuilder();
                var parameters = new DynamicParameters();

                if (empresaId.HasValue)
                {
                    whereBuilder.Append(" AND c.EmpresaId = @empresaId");
                    parameters.Add("empresaId", empresaId.Value);
                }

                if (desde.HasValue)
                {
                    whereBuilder.Append(" AND c.Fecha >= @desde");
                    parameters.Add("desde", desde.Value);
                }

                if (hasta.HasValue)
                {
                    whereBuilder.Append(" AND c.Fecha < @hasta");
                    parameters.Add("hasta", hasta.Value.AddDays(1));
                }

                var whereClause = whereBuilder.Length > 0 ? "WHERE 1=1" + whereBuilder.ToString() : "";

                // Contar total de clientes únicos
                var totalCount = await connection.ExecuteScalarAsync<int>(
                    $@"SELECT COUNT(DISTINCT c.ClienteId) FROM Consumos c {whereClause}",
                    parameters);

                parameters.Add("offset", (page - 1) * pageSize);
                parameters.Add("pageSize", pageSize);

                var data = await connection.QueryAsync<ConsumoPorClienteDto>(
                    $@"SELECT 
                        cl.Id AS ClienteId,
                        cl.Nombre AS ClienteNombre,
                        cl.Cedula AS ClienteCedula,
                        cl.Saldo AS SaldoActual,
                        cl.SaldoOriginal AS Limite,
                        e.Nombre AS EmpresaNombre,
                        COUNT(*) AS TotalConsumos,
                        ISNULL(SUM(CASE WHEN c.Reversado = 0 THEN c.Monto ELSE 0 END), 0) AS MontoConsumido,
                        SUM(CASE WHEN c.Reversado = 1 THEN 1 ELSE 0 END) AS Reversos,
                        MAX(c.Fecha) AS UltimoConsumo
                    FROM Consumos c
                    INNER JOIN Clientes cl ON c.ClienteId = cl.Id
                    INNER JOIN Empresas e ON c.EmpresaId = e.Id
                    {whereClause}
                    GROUP BY cl.Id, cl.Nombre, cl.Cedula, cl.Saldo, cl.SaldoOriginal, e.Nombre
                    ORDER BY MontoConsumido DESC
                    OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY",
                    parameters);

                return Ok(new
                {
                    Data = data,
                    Pagination = new
                    {
                        Total = totalCount,
                        Page = page,
                        PageSize = pageSize,
                        TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR PorCliente: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "Error interno.", error = ex.Message });
            }
        }

        #endregion

        #region Consumos por Proveedor

        /// <summary>
        /// GET /api/modulo-consumos/por-proveedor
        /// </summary>
        [HttpGet("por-proveedor")]
        public async Task<IActionResult> PorProveedor(
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta)
        {
            try
            {
                using var connection = _connectionFactory.Create();

                var whereBuilder = new StringBuilder();
                var parameters = new DynamicParameters();

                if (desde.HasValue)
                {
                    whereBuilder.Append(" AND c.Fecha >= @desde");
                    parameters.Add("desde", desde.Value);
                }

                if (hasta.HasValue)
                {
                    whereBuilder.Append(" AND c.Fecha < @hasta");
                    parameters.Add("hasta", hasta.Value.AddDays(1));
                }

                var whereClause = whereBuilder.Length > 0 ? "WHERE 1=1" + whereBuilder.ToString() : "";

                var data = await connection.QueryAsync<ConsumoPorProveedorDto>(
                    $@"SELECT 
                        p.Id AS ProveedorId,
                        p.Nombre AS ProveedorNombre,
                        COUNT(*) AS TotalConsumos,
                        ISNULL(SUM(CASE WHEN c.Reversado = 0 THEN c.Monto ELSE 0 END), 0) AS MontoActivo,
                        ISNULL(SUM(CASE WHEN c.Reversado = 1 THEN c.Monto ELSE 0 END), 0) AS MontoReversado,
                        COUNT(DISTINCT c.TiendaId) AS TiendasActivas,
                        COUNT(DISTINCT c.EmpresaId) AS EmpresasAtendidas
                    FROM Consumos c
                    INNER JOIN Proveedores p ON c.ProveedorId = p.Id
                    {whereClause}
                    GROUP BY p.Id, p.Nombre
                    ORDER BY MontoActivo DESC",
                    parameters);

                return Ok(new { Data = data });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR PorProveedor: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "Error interno.", error = ex.Message });
            }
        }

        #endregion

        #region Reportes

        /// <summary>
        /// GET /api/modulo-consumos/reportes/diario
        /// </summary>
        [HttpGet("reportes/diario")]
        public async Task<IActionResult> ReporteDiario([FromQuery] DateTime? fecha = null)
        {
            try
            {
                using var connection = _connectionFactory.Create();

                var fechaReporte = fecha?.Date ?? DateTime.UtcNow.Date;

                // Resumen general
                var resumen = await connection.QueryFirstOrDefaultAsync<ReporteDiarioResumenDto>(
                    @"SELECT 
                        COUNT(*) AS TotalConsumos,
                        SUM(CASE WHEN Reversado = 1 THEN 1 ELSE 0 END) AS TotalReversos,
                        ISNULL(SUM(CASE WHEN Reversado = 0 THEN Monto ELSE 0 END), 0) AS MontoTotal,
                        ISNULL(SUM(CASE WHEN Reversado = 1 THEN Monto ELSE 0 END), 0) AS MontoReversado
                    FROM Consumos
                    WHERE CAST(Fecha AS DATE) = @fecha",
                    new { fecha = fechaReporte });

                // Por hora
                var porHora = await connection.QueryAsync<ReportePorHoraDto>(
                    @"SELECT 
                        DATEPART(HOUR, Fecha) AS Hora,
                        COUNT(*) AS Cantidad,
                        ISNULL(SUM(CASE WHEN Reversado = 0 THEN Monto ELSE 0 END), 0) AS Monto
                    FROM Consumos
                    WHERE CAST(Fecha AS DATE) = @fecha
                    GROUP BY DATEPART(HOUR, Fecha)
                    ORDER BY Hora",
                    new { fecha = fechaReporte });

                // Por empresa
                var porEmpresa = await connection.QueryAsync<ReportePorEntidadDto>(
                    @"SELECT 
                        e.Nombre AS Nombre,
                        COUNT(*) AS Cantidad,
                        ISNULL(SUM(CASE WHEN c.Reversado = 0 THEN c.Monto ELSE 0 END), 0) AS Monto
                    FROM Consumos c
                    INNER JOIN Empresas e ON c.EmpresaId = e.Id
                    WHERE CAST(c.Fecha AS DATE) = @fecha
                    GROUP BY e.Nombre
                    ORDER BY Monto DESC",
                    new { fecha = fechaReporte });

                // Por proveedor
                var porProveedor = await connection.QueryAsync<ReportePorEntidadDto>(
                    @"SELECT 
                        p.Nombre AS Nombre,
                        COUNT(*) AS Cantidad,
                        ISNULL(SUM(CASE WHEN c.Reversado = 0 THEN c.Monto ELSE 0 END), 0) AS Monto
                    FROM Consumos c
                    INNER JOIN Proveedores p ON c.ProveedorId = p.Id
                    WHERE CAST(c.Fecha AS DATE) = @fecha
                    GROUP BY p.Nombre
                    ORDER BY Monto DESC",
                    new { fecha = fechaReporte });

                return Ok(new
                {
                    Fecha = fechaReporte,
                    Resumen = new
                    {
                        TotalConsumos = resumen?.TotalConsumos ?? 0,
                        TotalReversos = resumen?.TotalReversos ?? 0,
                        MontoTotal = resumen?.MontoTotal ?? 0,
                        MontoReversado = resumen?.MontoReversado ?? 0,
                        MontoNeto = resumen?.MontoTotal ?? 0
                    },
                    PorHora = porHora,
                    PorEmpresa = porEmpresa,
                    PorProveedor = porProveedor
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR ReporteDiario: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "Error interno.", error = ex.Message });
            }
        }

        /// <summary>
        /// GET /api/modulo-consumos/reportes/periodo
        /// </summary>
        [HttpGet("reportes/periodo")]
        public async Task<IActionResult> ReportePeriodo(
            [FromQuery] DateTime desde,
            [FromQuery] DateTime hasta)
        {
            try
            {
                using var connection = _connectionFactory.Create();

                var porDia = await connection.QueryAsync<ReportePorDiaDto>(
                    @"SELECT 
                        CAST(Fecha AS DATE) AS Fecha,
                        COUNT(*) AS Cantidad,
                        ISNULL(SUM(CASE WHEN Reversado = 0 THEN Monto ELSE 0 END), 0) AS Monto,
                        SUM(CASE WHEN Reversado = 1 THEN 1 ELSE 0 END) AS Reversos
                    FROM Consumos
                    WHERE Fecha >= @desde AND Fecha < @hasta
                    GROUP BY CAST(Fecha AS DATE)
                    ORDER BY Fecha",
                    new { desde, hasta = hasta.AddDays(1) });

                var porDiaList = porDia.ToList();
                var totalConsumos = porDiaList.Sum(x => x.Cantidad);
                var totalMonto = porDiaList.Sum(x => x.Monto);
                var totalReversos = porDiaList.Sum(x => x.Reversos);

                return Ok(new
                {
                    Periodo = new { Desde = desde, Hasta = hasta },
                    Resumen = new
                    {
                        TotalConsumos = totalConsumos,
                        TotalMonto = totalMonto,
                        TotalReversos = totalReversos,
                        PromedioDiario = porDiaList.Count > 0 ? totalMonto / porDiaList.Count : 0
                    },
                    PorDia = porDiaList
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR ReportePeriodo: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                return StatusCode(500, new { message = "Error interno.", error = ex.Message });
            }
        }

        #endregion
    }

    #region DTOs

    public class ReversarConsumoRequest
    {
        public string? Motivo { get; set; }
    }

    // DTOs para Dashboard
    internal class ResumenDiaDto
    {
        public int TotalConsumos { get; set; }
        public decimal MontoTotal { get; set; }
        public int TotalReversos { get; set; }
        public decimal MontoReversado { get; set; }
    }

    internal class ConsumoPorDiaDto
    {
        public DateTime Fecha { get; set; }
        public int Cantidad { get; set; }
        public decimal Monto { get; set; }
    }

    internal class TopEmpresaDto
    {
        public int EmpresaId { get; set; }
        public string EmpresaNombre { get; set; } = null!;
        public int CantidadConsumos { get; set; }
        public decimal MontoTotal { get; set; }
    }

    internal class TopClienteDto
    {
        public int ClienteId { get; set; }
        public string ClienteNombre { get; set; } = null!;
        public string? ClienteCedula { get; set; }
        public string EmpresaNombre { get; set; } = null!;
        public int CantidadConsumos { get; set; }
        public decimal MontoTotal { get; set; }
    }

    internal class UltimoConsumoDto
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public string ClienteNombre { get; set; } = null!;
        public string? ClienteCedula { get; set; }
        public string EmpresaNombre { get; set; } = null!;
        public string ProveedorNombre { get; set; } = null!;
        public string? TiendaNombre { get; set; }
        public decimal Monto { get; set; }
        public string? Concepto { get; set; }
        public bool Reversado { get; set; }
    }

    // DTOs para Lista
    internal class ResumenListaDto
    {
        public int Total { get; set; }
        public decimal MontoActivo { get; set; }
        public decimal MontoReversado { get; set; }
    }

    internal class ConsumoListaDto
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public int ClienteId { get; set; }
        public string ClienteNombre { get; set; } = null!;
        public string? ClienteCedula { get; set; }
        public int EmpresaId { get; set; }
        public string EmpresaNombre { get; set; } = null!;
        public int ProveedorId { get; set; }
        public string ProveedorNombre { get; set; } = null!;
        public int? TiendaId { get; set; }
        public string? TiendaNombre { get; set; }
        public int? CajaId { get; set; }
        public decimal Monto { get; set; }
        public string? Concepto { get; set; }
        public string? Referencia { get; set; }
        public bool Reversado { get; set; }
        public DateTime? ReversadoUtc { get; set; }
        public string? RegistradoPor { get; set; }
    }

    // DTOs para Detalle
    internal class ConsumoDetalleDto
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public int ClienteId { get; set; }
        public string ClienteNombre { get; set; } = null!;
        public string? ClienteCedula { get; set; }
        public decimal ClienteSaldoActual { get; set; }
        public decimal ClienteLimite { get; set; }
        public int EmpresaId { get; set; }
        public string EmpresaNombre { get; set; } = null!;
        public string? EmpresaRnc { get; set; }
        public int ProveedorId { get; set; }
        public string ProveedorNombre { get; set; } = null!;
        public int? TiendaId { get; set; }
        public string? TiendaNombre { get; set; }
        public int? CajaId { get; set; }
        public string? CajaNombre { get; set; }
        public decimal Monto { get; set; }
        public string? Concepto { get; set; }
        public string? Nota { get; set; }
        public string? Referencia { get; set; }
        public bool Reversado { get; set; }
        public DateTime? ReversadoUtc { get; set; }
        public string? ReversadoPor { get; set; }
        public string? MotivoReverso { get; set; }
        public string? RegistradoPor { get; set; }
    }

    // DTOs para Reverso
    internal class ConsumoReversoInfoDto
    {
        public DateTime Fecha { get; set; }
        public bool Reversado { get; set; }
        public decimal Monto { get; set; }
        public int ClienteId { get; set; }
        public decimal ClienteSaldo { get; set; }
        public decimal ClienteSaldoOriginal { get; set; }
    }

    internal class ReversoListaDto
    {
        public int Id { get; set; }
        public DateTime FechaConsumo { get; set; }
        public DateTime? FechaReverso { get; set; }
        public string ClienteNombre { get; set; } = null!;
        public string? ClienteCedula { get; set; }
        public string EmpresaNombre { get; set; } = null!;
        public string ProveedorNombre { get; set; } = null!;
        public string? TiendaNombre { get; set; }
        public decimal Monto { get; set; }
        public string? Concepto { get; set; }
        public string? MotivoReverso { get; set; }
        public string? ReversadoPor { get; set; }
    }

    // DTOs para agrupaciones
    internal class ConsumoPorEmpresaDto
    {
        public int EmpresaId { get; set; }
        public string EmpresaNombre { get; set; } = null!;
        public string? EmpresaRnc { get; set; }
        public int TotalConsumos { get; set; }
        public int ConsumosActivos { get; set; }
        public int ConsumosReversados { get; set; }
        public decimal MontoActivo { get; set; }
        public decimal MontoReversado { get; set; }
        public int ClientesUnicos { get; set; }
    }

    internal class ConsumoPorClienteDto
    {
        public int ClienteId { get; set; }
        public string ClienteNombre { get; set; } = null!;
        public string? ClienteCedula { get; set; }
        public decimal SaldoActual { get; set; }
        public decimal Limite { get; set; }
        public string EmpresaNombre { get; set; } = null!;
        public int TotalConsumos { get; set; }
        public decimal MontoConsumido { get; set; }
        public int Reversos { get; set; }
        public DateTime? UltimoConsumo { get; set; }
    }

    internal class ConsumoPorProveedorDto
    {
        public int ProveedorId { get; set; }
        public string ProveedorNombre { get; set; } = null!;
        public int TotalConsumos { get; set; }
        public decimal MontoActivo { get; set; }
        public decimal MontoReversado { get; set; }
        public int TiendasActivas { get; set; }
        public int EmpresasAtendidas { get; set; }
    }

    // DTOs para Reportes
    internal class ReporteDiarioResumenDto
    {
        public int TotalConsumos { get; set; }
        public int TotalReversos { get; set; }
        public decimal MontoTotal { get; set; }
        public decimal MontoReversado { get; set; }
    }

    internal class ReportePorHoraDto
    {
        public int Hora { get; set; }
        public int Cantidad { get; set; }
        public decimal Monto { get; set; }
    }

    internal class ReportePorEntidadDto
    {
        public string Nombre { get; set; } = null!;
        public int Cantidad { get; set; }
        public decimal Monto { get; set; }
    }

    internal class ReportePorDiaDto
    {
        public DateTime Fecha { get; set; }
        public int Cantidad { get; set; }
        public decimal Monto { get; set; }
        public int Reversos { get; set; }
    }

    #endregion
}