// Controllers/ClientesController.cs
using Consumo_App.Data.Sql;
using Consumo_App.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using System.Text;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/empresas/{empresaId:int}/clientes")]
    [Authorize]
    public class ClientesController : ControllerBase
    {
        private readonly SqlConnectionFactory _db;

        public ClientesController(SqlConnectionFactory db)
        {
            _db = db;
        }

        // GET /api/empresas/{empresaId}/clientes?q=
        [HttpGet]
        public async Task<IActionResult> List(int empresaId, [FromQuery] string? q)
        {
            using var conn = _db.Create();

            var sql = @"
                SELECT Id, Codigo, Nombre, Cedula, Grupo, Saldo, SaldoOriginal, Activo
                FROM Clientes
                WHERE EmpresaId = @EmpresaId";

            if (!string.IsNullOrWhiteSpace(q))
            {
                sql += " AND (Nombre LIKE @Query OR Codigo LIKE @Query OR Cedula LIKE @Query)";
            }

            sql += " ORDER BY Nombre";

            var data = await conn.QueryAsync<ClienteListDto>(sql, new
            {
                EmpresaId = empresaId,
                Query = $"%{q}%"
            });

            return Ok(data);
        }

        // GET /api/empresas/{empresaId}/credito-disponible
        [HttpGet("~/api/empresas/{empresaId:int}/credito-disponible")]
        public async Task<IActionResult> GetCreditoDisponible(int empresaId)
        {
            using var conn = _db.Create();

            var empresa = await conn.QueryFirstOrDefaultAsync<EmpresaBasicDto>(
                "SELECT Id, Nombre, Limite_Credito as LimiteCredito FROM Empresas WHERE Id = @Id",
                new { Id = empresaId });

            if (empresa == null)
                return NotFound(new { message = "Empresa no encontrada." });

            var limiteAsignadoEmpleados = await conn.QueryFirstOrDefaultAsync<decimal>(
                "SELECT ISNULL(SUM(SaldoOriginal), 0) FROM Clientes WHERE EmpresaId = @EmpresaId AND Activo = 1",
                new { EmpresaId = empresaId });

            var disponibleParaAsignar = empresa.LimiteCredito - limiteAsignadoEmpleados;

            var empleadosResumen = await conn.QueryAsync<EmpleadoResumenDto>(@"
                SELECT 
                    Id,
                    Nombre,
                    Codigo,
                    SaldoOriginal AS LimiteAsignado,
                    Saldo AS SaldoDisponible,
                    (SaldoOriginal - Saldo) AS SaldoUtilizado,
                    Activo
                FROM Clientes
                WHERE EmpresaId = @EmpresaId
                ORDER BY Nombre",
                new { EmpresaId = empresaId });

            var empleadosList = empleadosResumen.ToList();

            return Ok(new
            {
                empresaId,
                empresaNombre = empresa.Nombre,
                limiteEmpresa = empresa.LimiteCredito,
                limiteAsignadoEmpleados,
                disponibleParaAsignar = disponibleParaAsignar > 0 ? disponibleParaAsignar : 0,
                porcentajeUtilizado = empresa.LimiteCredito > 0
                    ? Math.Round((limiteAsignadoEmpleados / empresa.LimiteCredito) * 100, 2)
                    : 0,
                totalEmpleados = empleadosList.Count,
                empleadosActivos = empleadosList.Count(e => e.Activo),
                empleados = empleadosList
            });
        }

        // GET /api/clientes/cedula/{cedula}
        [HttpGet("~/api/clientes/cedula/{cedula}")]
        public async Task<IActionResult> GetByCedulaGlobal(string cedula)
        {
            using var conn = _db.Create();

            var clean = new string(cedula.Where(char.IsDigit).ToArray());

            var matches = await conn.QueryAsync<ClienteMatchDto>(@"
                SELECT 
                    c.Id,
                    c.Nombre,
                    c.EmpresaId,
                    e.Nombre AS EmpresaNombre,
                    c.Saldo
                FROM Clientes c
                INNER JOIN Empresas e ON c.EmpresaId = e.Id
                WHERE REPLACE(REPLACE(c.Cedula, '-', ''), ' ', '') = @Cedula",
                new { Cedula = clean });

            var matchesList = matches.ToList();

            if (matchesList.Count == 0)
                return NotFound(new { message = "Cliente no encontrado" });

            return Ok(new { cedula = clean, matches = matchesList });
        }

        // GET /api/empresas/{empresaId}/clientes/{id}
        [HttpGet("{id:int}")]
        [Authorize(Policy = "perm:clientes_ver")]
        public async Task<IActionResult> Get(int empresaId, int id)
        {
            using var conn = _db.Create();

            var c = await conn.QueryFirstOrDefaultAsync<ClienteListDto>(@"
                SELECT Id, Codigo, Nombre, Cedula, Grupo, Saldo, SaldoOriginal, Activo
                FROM Clientes
                WHERE EmpresaId = @EmpresaId AND Id = @Id",
                new { EmpresaId = empresaId, Id = id });

            if (c == null) return NotFound();

            return Ok(c);
        }

        // POST /api/empresas/{empresaId}/clientes
        [HttpPost]
        public async Task<IActionResult> Create(int empresaId, [FromBody] ClienteCreateDto dto)
        {
            using var conn = _db.Create();

            // Validar que la empresa existe
            var empresa = await conn.QueryFirstOrDefaultAsync<EmpresaBasicDto>(
                "SELECT Id, Nombre, Limite_Credito FROM Empresas WHERE Id = @Id",
                new { Id = empresaId });

            if (empresa == null)
                return NotFound(new { message = "Empresa no existe." });

            // Validar límite de crédito de la empresa
            var validacion = await ValidarLimiteCreditoEmpresa(conn, empresaId, dto.SaldoOriginal, null, empresa.LimiteCredito);
            if (!validacion.EsValido)
            {
                return BadRequest(new
                {
                    message = validacion.Mensaje,
                    limiteEmpresa = validacion.LimiteEmpresa,
                    limiteAsignado = validacion.LimiteAsignado,
                    disponible = validacion.Disponible,
                    montoSolicitado = dto.SaldoOriginal,
                    excedente = validacion.Excedente
                });
            }

            // Generar código automáticamente si no se proporciona
            var codigo = dto.Codigo?.Trim();
            if (string.IsNullOrWhiteSpace(codigo))
            {
                var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                var random = new Random().Next(100, 999);
                codigo = $"EMP-{empresaId}-{timestamp}-{random}";
            }
            else
            {
                // Validar código único dentro de la empresa solo si se proporcionó
                var codigoExiste = await conn.QueryFirstOrDefaultAsync<int?>(
                    "SELECT 1 FROM Clientes WHERE EmpresaId = @EmpresaId AND Codigo = @Codigo",
                    new { EmpresaId = empresaId, Codigo = codigo });

                if (codigoExiste.HasValue)
                    return BadRequest(new { message = $"Ya existe un empleado con el código '{codigo}' en esta empresa." });
            }

            // Grupo por defecto si no se proporciona
            var grupo = string.IsNullOrWhiteSpace(dto.Grupo) ? "General" : dto.Grupo.Trim();

            // Validar cédula única (global)
            if (!string.IsNullOrWhiteSpace(dto.Cedula))
            {
                var cedulaLimpia = new string(dto.Cedula.Where(char.IsDigit).ToArray());
                var cedulaExiste = await conn.QueryFirstOrDefaultAsync<int?>(
                    "SELECT 1 FROM Clientes WHERE REPLACE(REPLACE(Cedula, '-', ''), ' ', '') = @Cedula",
                    new { Cedula = cedulaLimpia });

                if (cedulaExiste.HasValue)
                    return BadRequest(new { message = $"Ya existe un empleado con la cédula '{dto.Cedula}'." });
            }

            // Insertar cliente
            var newId = await conn.QuerySingleAsync<int>(@"
        INSERT INTO Clientes (EmpresaId, Codigo, Nombre, Cedula, Grupo, SaldoOriginal, Saldo, Activo)
        OUTPUT INSERTED.Id
        VALUES (@EmpresaId, @Codigo, @Nombre, @Cedula, @Grupo, @SaldoOriginal, @Saldo, @Activo)",
                new
                {
                    EmpresaId = empresaId,
                    Codigo = codigo,
                    Nombre = dto.Nombre.Trim(),
                    Cedula = dto.Cedula?.Trim(),
                    Grupo = grupo,
                    SaldoOriginal = dto.SaldoOriginal,
                    Saldo = dto.SaldoOriginal,
                    Activo = dto.Activo
                });

            return Created($"/api/empresas/{empresaId}/clientes/{newId}", new
            {
                id = newId,
                codigo = codigo,
                mensaje = "Empleado creado exitosamente.",
                limiteAsignado = dto.SaldoOriginal,
                saldoDisponible = dto.SaldoOriginal
            });
        }


        /*
        // POST /api/empresas/{empresaId}/clientes
        [HttpPost]
        public async Task<IActionResult> Create(int empresaId, [FromBody] ClienteCreateDto dto)
        {
            using var conn = _db.Create();

            // Validar que la empresa existe
            var empresa = await conn.QueryFirstOrDefaultAsync<EmpresaBasicDto>(
                "SELECT Id, Nombre, LimiteCredito FROM Empresas WHERE Id = @Id",
                new { Id = empresaId });

            if (empresa == null)
                return NotFound(new { message = "Empresa no existe." });

            // Validar límite de crédito de la empresa
            var validacion = await ValidarLimiteCreditoEmpresa(conn, empresaId, dto.SaldoOriginal, null, empresa.LimiteCredito);
            if (!validacion.EsValido)
            {
                return BadRequest(new
                {
                    message = validacion.Mensaje,
                    limiteEmpresa = validacion.LimiteEmpresa,
                    limiteAsignado = validacion.LimiteAsignado,
                    disponible = validacion.Disponible,
                    montoSolicitado = dto.SaldoOriginal,
                    excedente = validacion.Excedente
                });
            }

            // Validar código único dentro de la empresa
            var codigoExiste = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT 1 FROM Clientes WHERE EmpresaId = @EmpresaId AND Codigo = @Codigo",
                new { EmpresaId = empresaId, Codigo = dto.Codigo.Trim() });

            if (codigoExiste.HasValue)
                return BadRequest(new { message = $"Ya existe un empleado con el código '{dto.Codigo}' en esta empresa." });

            // Validar cédula única (global)
            if (!string.IsNullOrWhiteSpace(dto.Cedula))
            {
                var cedulaLimpia = new string(dto.Cedula.Where(char.IsDigit).ToArray());
                var cedulaExiste = await conn.QueryFirstOrDefaultAsync<int?>(
                    "SELECT 1 FROM Clientes WHERE REPLACE(REPLACE(Cedula, '-', ''), ' ', '') = @Cedula",
                    new { Cedula = cedulaLimpia });

                if (cedulaExiste.HasValue)
                    return BadRequest(new { message = $"Ya existe un empleado con la cédula '{dto.Cedula}'." });
            }

            // Insertar cliente
            var newId = await conn.QuerySingleAsync<int>(@"
                INSERT INTO Clientes (EmpresaId, Codigo, Nombre, Cedula, Grupo, SaldoOriginal, Saldo, Activo)
                OUTPUT INSERTED.Id
                VALUES (@EmpresaId, @Codigo, @Nombre, @Cedula, @Grupo, @SaldoOriginal, @Saldo, @Activo)",
                new
                {
                    EmpresaId = empresaId,
                    Codigo = dto.Codigo.Trim(),
                    Nombre = dto.Nombre.Trim(),
                    Cedula = dto.Cedula?.Trim(),
                    Grupo = dto.Grupo.Trim(),
                    SaldoOriginal = dto.SaldoOriginal,
                    Saldo = dto.SaldoOriginal,
                    Activo = dto.Activo
                });

            return Created($"/api/empresas/{empresaId}/clientes/{newId}", new
            {
                id = newId,
                mensaje = "Empleado creado exitosamente.",
                limiteAsignado = dto.SaldoOriginal,
                saldoDisponible = dto.SaldoOriginal
            });
        } */

        // PUT /api/empresas/{empresaId}/clientes/{id}
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int empresaId, int id, [FromBody] ClienteUpdateDto dto)
        {
            using var conn = _db.Create();

            var c = await conn.QueryFirstOrDefaultAsync<ClienteEntity>(
                "SELECT * FROM Clientes WHERE EmpresaId = @EmpresaId AND Id = @Id",
                new { EmpresaId = empresaId, Id = id });

            if (c == null) return NotFound();

            // Si se está actualizando el límite de crédito
            decimal nuevoSaldo = c.Saldo;
            decimal nuevoSaldoOriginal = c.SaldoOriginal;

            if (dto.SaldoOriginal.HasValue && dto.SaldoOriginal.Value != c.SaldoOriginal)
            {
                var empresa = await conn.QueryFirstOrDefaultAsync<decimal>(
                    "SELECT LimiteCredito FROM Empresas WHERE Id = @Id",
                    new { Id = empresaId });

                var validacion = await ValidarLimiteCreditoEmpresa(conn, empresaId, dto.SaldoOriginal.Value, c.Id, empresa);
                if (!validacion.EsValido)
                {
                    return BadRequest(new
                    {
                        message = validacion.Mensaje,
                        limiteEmpresa = validacion.LimiteEmpresa,
                        limiteAsignado = validacion.LimiteAsignado,
                        disponible = validacion.Disponible,
                        montoSolicitado = dto.SaldoOriginal.Value,
                        excedente = validacion.Excedente
                    });
                }

                var diferencia = dto.SaldoOriginal.Value - c.SaldoOriginal;
                nuevoSaldoOriginal = dto.SaldoOriginal.Value;
                nuevoSaldo = Math.Max(0, c.Saldo + diferencia);
                if (nuevoSaldo > nuevoSaldoOriginal)
                    nuevoSaldo = nuevoSaldoOriginal;
            }

            // Actualizar
            await conn.ExecuteAsync(@"
                UPDATE Clientes SET
                    Codigo = @Codigo,
                    Nombre = @Nombre,
                    Cedula = @Cedula,
                    Grupo = @Grupo,
                    SaldoOriginal = @SaldoOriginal,
                    Saldo = @Saldo,
                    Activo = @Activo
                WHERE Id = @Id",
                new
                {
                    Id = id,
                    Codigo = !string.IsNullOrWhiteSpace(dto.Codigo) ? dto.Codigo.Trim() : c.Codigo,
                    Nombre = !string.IsNullOrWhiteSpace(dto.Nombre) ? dto.Nombre.Trim() : c.Nombre,
                    Cedula = dto.Cedula != null ? dto.Cedula.Trim() : c.Cedula,
                    Grupo = !string.IsNullOrWhiteSpace(dto.Grupo) ? dto.Grupo.Trim() : c.Grupo,
                    SaldoOriginal = nuevoSaldoOriginal,
                    Saldo = nuevoSaldo,
                    Activo = dto.Activo ?? c.Activo
                });

            return Ok(new
            {
                mensaje = "Empleado actualizado exitosamente.",
                id = c.Id,
                limiteCredito = nuevoSaldoOriginal,
                saldoDisponible = nuevoSaldo
            });
        }

        // PATCH /api/empresas/{empresaId}/clientes/{id}/toggle
        [HttpPatch("{id:int}/toggle")]
        public async Task<IActionResult> Toggle(int empresaId, int id)
        {
            using var conn = _db.Create();

            var activo = await conn.QueryFirstOrDefaultAsync<bool?>(
                "SELECT Activo FROM Clientes WHERE EmpresaId = @EmpresaId AND Id = @Id",
                new { EmpresaId = empresaId, Id = id });

            if (activo == null) return NotFound();

            var nuevoActivo = !activo.Value;

            await conn.ExecuteAsync(
                "UPDATE Clientes SET Activo = @Activo WHERE Id = @Id",
                new { Id = id, Activo = nuevoActivo });

            return Ok(new { Id = id, Activo = nuevoActivo });
        }

        // POST /api/empresas/{empresaId}/clientes/bulk
        [HttpPost("bulk")]
        [RequestSizeLimit(25_000_000)]
        public async Task<IActionResult> Bulk(int empresaId, IFormFile file, [FromQuery] bool upsert = true)
        {
            if (file == null || file.Length == 0)
                return BadRequest("Archivo CSV requerido.");

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != ".csv")
                return BadRequest("Use archivo .csv");

            using var conn = _db.Create();

            var empresa = await conn.QueryFirstOrDefaultAsync<EmpresaBasicDto>(
                "SELECT Id, Nombre, LimiteCredito FROM Empresas WHERE Id = @Id",
                new { Id = empresaId });

            if (empresa == null)
                return NotFound("Empresa no encontrada.");

            var limiteAsignadoActual = await conn.QueryFirstOrDefaultAsync<decimal>(
                "SELECT ISNULL(SUM(SaldoOriginal), 0) FROM Clientes WHERE EmpresaId = @EmpresaId",
                new { EmpresaId = empresaId });

            var errores = new List<string>();
            var advertencias = new List<string>();
            int insertados = 0, actualizados = 0;
            decimal totalNuevoLimite = 0;

            // Leer líneas
            // Formato esperado: nombre,cedula,saldoOriginal,activo
            var lineas = new List<(int row, string[] parts)>();
            using (var sr = new StreamReader(file.OpenReadStream(), Encoding.UTF8))
            {
                await sr.ReadLineAsync(); // header
                int row = 1;
                string? line;
                while ((line = await sr.ReadLineAsync()) != null)
                {
                    row++;
                    if (!string.IsNullOrWhiteSpace(line))
                        lineas.Add((row, line.Split(',', StringSplitOptions.None)));
                }
            }

            // Primera pasada: calcular totales y validar
            var cedulasEnArchivo = new HashSet<string>();
            foreach (var (row, p) in lineas)
            {
                var cedula = p.ElementAtOrDefault(1)?.Trim();
                var saldoStr = p.ElementAtOrDefault(2)?.Trim();

                if (!decimal.TryParse(saldoStr, out var saldoOriginal))
                    saldoOriginal = 0;

                // Si tiene cédula, verificar si ya existe para determinar si es insert o update
                if (!string.IsNullOrWhiteSpace(cedula))
                {
                    var cedulaLimpia = new string(cedula.Where(char.IsDigit).ToArray());

                    var existente = await conn.QueryFirstOrDefaultAsync<decimal?>(
                        @"SELECT SaldoOriginal FROM Clientes 
                  WHERE EmpresaId = @EmpresaId 
                  AND REPLACE(REPLACE(Cedula, '-', ''), ' ', '') = @Cedula",
                        new { EmpresaId = empresaId, Cedula = cedulaLimpia });

                    if (existente == null)
                        totalNuevoLimite += saldoOriginal;
                    else if (upsert)
                        totalNuevoLimite += (saldoOriginal - existente.Value);

                    cedulasEnArchivo.Add(cedulaLimpia);
                }
                else
                {
                    // Sin cédula, siempre es insert nuevo
                    totalNuevoLimite += saldoOriginal;
                }
            }

            // Validar límite
            var nuevoTotalAsignado = limiteAsignadoActual + totalNuevoLimite;
            if (empresa.LimiteCredito > 0 && nuevoTotalAsignado > empresa.LimiteCredito)
            {
                return BadRequest(new
                {
                    message = "La carga masiva excede el límite de crédito de la empresa.",
                    limiteEmpresa = empresa.LimiteCredito,
                    limiteAsignadoActual,
                    limiteEnArchivo = totalNuevoLimite,
                    totalResultante = nuevoTotalAsignado,
                    excedente = nuevoTotalAsignado - empresa.LimiteCredito
                });
            }

            // Segunda pasada: procesar
            // Formato: nombre,cedula,saldoOriginal,activo
            foreach (var (row, p) in lineas)
            {
                var nombre = p.ElementAtOrDefault(0)?.Trim();
                var cedula = p.ElementAtOrDefault(1)?.Trim();
                var saldoStr = p.ElementAtOrDefault(2)?.Trim();
                var activoStr = p.ElementAtOrDefault(3)?.Trim();

                // Validar nombre requerido
                if (string.IsNullOrWhiteSpace(nombre))
                {
                    errores.Add($"Línea {row}: nombre es requerido");
                    continue;
                }

                if (!decimal.TryParse(saldoStr, out var saldoOriginal))
                    saldoOriginal = 0;

                // Parsear activo (default true)
                var activo = true;
                if (!string.IsNullOrWhiteSpace(activoStr))
                {
                    activo = activoStr.ToLower() == "true" || activoStr == "1";
                }

                // Generar código automático
                var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                var random = new Random().Next(1000, 9999);
                var codigo = $"EMP-{empresaId}-{timestamp}-{random}";

                // Grupo por defecto
                var grupo = "General";

                // Buscar existente por cédula si tiene
                ClienteEntity? existente = null;
                if (!string.IsNullOrWhiteSpace(cedula))
                {
                    var cedulaLimpia = new string(cedula.Where(char.IsDigit).ToArray());
                    existente = await conn.QueryFirstOrDefaultAsync<ClienteEntity>(
                        @"SELECT * FROM Clientes 
                  WHERE EmpresaId = @EmpresaId 
                  AND REPLACE(REPLACE(Cedula, '-', ''), ' ', '') = @Cedula",
                        new { EmpresaId = empresaId, Cedula = cedulaLimpia });
                }

                if (existente == null)
                {
                    // Insertar nuevo
                    await conn.ExecuteAsync(@"
                INSERT INTO Clientes (EmpresaId, Codigo, Nombre, Cedula, Grupo, SaldoOriginal, Saldo, Activo)
                VALUES (@EmpresaId, @Codigo, @Nombre, @Cedula, @Grupo, @SaldoOriginal, @Saldo, @Activo)",
                        new
                        {
                            EmpresaId = empresaId,
                            Codigo = codigo,
                            Nombre = nombre,
                            Cedula = cedula,
                            Grupo = grupo,
                            SaldoOriginal = saldoOriginal,
                            Saldo = saldoOriginal,
                            Activo = activo
                        });
                    insertados++;
                }
                else if (upsert)
                {
                    // Actualizar existente
                    var diferencia = saldoOriginal - existente.SaldoOriginal;
                    var nuevoSaldo = Math.Max(0, existente.Saldo + diferencia);
                    if (nuevoSaldo > saldoOriginal) nuevoSaldo = saldoOriginal;

                    await conn.ExecuteAsync(@"
                UPDATE Clientes SET
                    Nombre = @Nombre,
                    Cedula = @Cedula,
                    SaldoOriginal = @SaldoOriginal,
                    Saldo = @Saldo,
                    Activo = @Activo
                WHERE Id = @Id",
                        new
                        {
                            Id = existente.Id,
                            Nombre = nombre,
                            Cedula = cedula,
                            SaldoOriginal = saldoOriginal,
                            Saldo = nuevoSaldo,
                            Activo = activo
                        });
                    actualizados++;
                }
                else
                {
                    advertencias.Add($"Línea {row}: empleado con cédula '{cedula}' ya existe (no actualizado)");
                }
            }

            return Ok(new
            {
                insertados,
                actualizados,
                errores,
                advertencias,
                resumen = new
                {
                    limiteEmpresa = empresa.LimiteCredito,
                    limiteAsignadoAntes = limiteAsignadoActual,
                    limiteAsignadoDespues = nuevoTotalAsignado
                }
            });
        }

        #region Helpers

        private async Task<ValidacionLimiteResult> ValidarLimiteCreditoEmpresa(
            System.Data.IDbConnection conn,
            int empresaId,
            decimal nuevoLimite,
            int? clienteIdExcluir,
            decimal limiteEmpresa)
        {
            if (limiteEmpresa <= 0)
                return new ValidacionLimiteResult { EsValido = true };

            var sql = "SELECT ISNULL(SUM(SaldoOriginal), 0) FROM Clientes WHERE EmpresaId = @EmpresaId";
            if (clienteIdExcluir.HasValue)
                sql += " AND Id != @ClienteId";

            var limiteAsignadoActual = await conn.QueryFirstOrDefaultAsync<decimal>(sql,
                new { EmpresaId = empresaId, ClienteId = clienteIdExcluir });

            var nuevoTotalAsignado = limiteAsignadoActual + nuevoLimite;
            var disponible = limiteEmpresa - limiteAsignadoActual;

            if (nuevoTotalAsignado > limiteEmpresa)
            {
                return new ValidacionLimiteResult
                {
                    EsValido = false,
                    Mensaje = $"El límite de crédito excede el disponible de la empresa. Disponible: RD${disponible:N2}, Solicitado: RD${nuevoLimite:N2}",
                    LimiteEmpresa = limiteEmpresa,
                    LimiteAsignado = limiteAsignadoActual,
                    Disponible = disponible,
                    Excedente = nuevoTotalAsignado - limiteEmpresa
                };
            }

            return new ValidacionLimiteResult
            {
                EsValido = true,
                LimiteEmpresa = limiteEmpresa,
                LimiteAsignado = limiteAsignadoActual,
                Disponible = disponible
            };
        }

        private class ValidacionLimiteResult
        {
            public bool EsValido { get; set; }
            public string? Mensaje { get; set; }
            public decimal LimiteEmpresa { get; set; }
            public decimal LimiteAsignado { get; set; }
            public decimal Disponible { get; set; }
            public decimal Excedente { get; set; }
        }

        #endregion
    }

    #region DTOs internos

    public class EmpresaBasicDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
        public decimal LimiteCredito { get; set; }
    }

    public class EmpleadoResumenDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
        public string Codigo { get; set; } = "";
        public decimal LimiteAsignado { get; set; }
        public decimal SaldoDisponible { get; set; }
        public decimal SaldoUtilizado { get; set; }
        public bool Activo { get; set; }
    }

    public class ClienteEntity
    {
        public int Id { get; set; }
        public int EmpresaId { get; set; }
        public string Codigo { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string? Cedula { get; set; }
        public string Grupo { get; set; } = "";
        public decimal Saldo { get; set; }
        public decimal SaldoOriginal { get; set; }
        public bool Activo { get; set; }
    }

    #endregion
}