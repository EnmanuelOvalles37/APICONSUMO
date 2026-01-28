using Dapper;
using Consumo_App.Data.Sql;
using Consumo_App.DTOs;
using Consumo_App.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class EmpresasController : ControllerBase
    {
        private readonly SqlConnectionFactory _connectionFactory;

        public EmpresasController(SqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        /// <summary>
        /// GET /api/empresas?search=&amp;page=1&amp;pageSize=10
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<PagedResult<EmpresaListDto>>> GetAll(
            [FromQuery] string? search,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            using var connection = _connectionFactory.Create();

            var whereClause = "WHERE 1=1";
            var parameters = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(search))
            {
                whereClause += " AND (e.Rnc LIKE @Search OR e.Nombre LIKE @Search)";
                parameters.Add("Search", $"%{search.Trim()}%");
            }

            // Contar total
            var countSql = $"SELECT COUNT(*) FROM Empresas e {whereClause}";
            var total = await connection.ExecuteScalarAsync<int>(countSql, parameters);

            // Obtener datos paginados con conteo de empleados
            parameters.Add("Offset", (page - 1) * pageSize);
            parameters.Add("PageSize", pageSize);

            var dataSql = $@"
                SELECT 
                    e.Id,
                    e.Rnc,
                    e.Nombre,
                    (SELECT COUNT(*) FROM Clientes c WHERE c.EmpresaId = e.Id) AS Empleados,
                    e.CreatedAt,
                    e.Activo
                FROM Empresas e
                {whereClause}
                ORDER BY e.Nombre
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var data = (await connection.QueryAsync<EmpresaListDto>(dataSql, parameters)).ToList();

            return Ok(new PagedResult<EmpresaListDto>
            {
                Data = data,
                Total = total,
                Page = page,
                PageSize = pageSize
            });
        }

        /// <summary>
        /// POST /api/empresas
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<CreateEmpresaDto>> Create([FromBody] CreateEmpresaDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Rnc) || string.IsNullOrWhiteSpace(dto.Nombre))
                return BadRequest("RNC y Nombre son requeridos.");

            using var connection = _connectionFactory.Create();

            // Verificar RNC único
            var exists = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM Empresas WHERE Rnc = @Rnc",
                new { Rnc = dto.Rnc.Trim() }) > 0;

            if (exists)
                return Conflict("Ya existe una empresa con ese RNC.");

            // Insertar empresa
            const string sql = @"
                INSERT INTO Empresas (Rnc, Nombre, Direccion, Email, Telefono, Activo, CreatedAt, Limite_Credito)
                OUTPUT INSERTED.Id
                VALUES (@Rnc, @Nombre, @Direccion, @Email, @Telefono, 1, @CreatedAt, @Limite_Credito)";

            var empresaId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                Rnc = dto.Rnc.Trim(),
                Nombre = dto.Nombre.Trim(),
                Direccion = dto.Direccion?.Trim(),
                Email = dto.Email?.Trim(),
                Telefono = dto.Telefono?.Trim(),
                CreatedAt = DateTime.Now,
                dto.LimiteCredito
            });

            var result = new CreateEmpresaDto
            {
                Id = empresaId,
                Rnc = dto.Rnc.Trim(),
                Nombre = dto.Nombre.Trim(),
                Direccion = dto.Direccion?.Trim(),
                Email = dto.Email?.Trim(),
                Telefono = dto.Telefono?.Trim(),
                LimiteCredito = dto.LimiteCredito
            };

            return Created($"/api/empresas/{empresaId}", result);
        }

        /// <summary>
        /// GET /api/empresas/{id}
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<ActionResult<EmpresaDetailDto>> GetById(int id)
        {
            using var connection = _connectionFactory.Create();

            // Obtener empresa
            const string sqlEmpresa = @"
                SELECT 
                    e.Id, e.Rnc, e.Nombre, e.Telefono, e.Email, e.Direccion,
                    e.Activo, e.Limite_Credito As LimiteCredito, e.DiaCorte
                FROM Empresas e
                WHERE e.Id = @Id";

            var empresa = await connection.QueryFirstOrDefaultAsync<dynamic>(sqlEmpresa, new { Id = id });

            if (empresa == null)
                return NotFound();

            // Obtener empleados de la empresa
            const string sqlEmpleados = @"
                SELECT 
                    c.Id, c.Codigo, c.Nombre, c.Cedula, c.Grupo, c.Saldo, c.Activo
                FROM Clientes c
                WHERE c.EmpresaId = @EmpresaId
                ORDER BY c.Nombre";

            var empleados = (await connection.QueryAsync<EmpresaEmpleadoDto>(sqlEmpleados, new { EmpresaId = id })).ToList();

            return Ok(new EmpresaDetailDto
            {
                Id = empresa.Id,
                Rnc = empresa.Rnc,
                Nombre = empresa.Nombre,
                Telefono = empresa.Telefono,
                Email = empresa.Email,
                Direccion = empresa.Direccion,
                Activo = empresa.Activo,
                LimiteCredito = empresa.LimiteCredito,
                DiaCorte = empresa.DiaCorte,
                Empleados = empleados
            });
        }

        /// <summary>
        /// GET /api/empresas/{id}/empleados?search=&amp;page=1&amp;pageSize=10
        /// </summary>
        [HttpGet("{id:int}/empleados")]
        public async Task<ActionResult<object>> GetEmpleados(
            int id,
            [FromQuery] string? search,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            using var connection = _connectionFactory.Create();

            var whereClause = "WHERE 1=1";
            var parameters = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(search))
            {
                whereClause += " AND (e.Rnc LIKE @Search OR e.Nombre LIKE @Search)";
                parameters.Add("Search", $"%{search.Trim()}%");
            }

            // Contar total
            var countSql = $"SELECT COUNT(*) FROM Empresas e {whereClause}";
            var total = await connection.ExecuteScalarAsync<int>(countSql, parameters);

            // Obtener datos paginados
            parameters.Add("Offset", (page - 1) * pageSize);
            parameters.Add("PageSize", pageSize);

            var dataSql = $@"
                SELECT 
                    e.Id,
                    e.Rnc,
                    e.Nombre,
                    (SELECT COUNT(*) FROM Clientes c WHERE c.EmpresaId = e.Id) AS Empleados,
                    e.Activo
                FROM Empresas e
                {whereClause}
                ORDER BY e.Nombre
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var data = await connection.QueryAsync<EmpresaListDto>(dataSql, parameters);

            return Ok(new { data, total, page, pageSize });
        }

        /// <summary>
        /// PUT /api/empresas/{id}
        /// </summary>
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] EmpresaUpdateDto dto)
        {
            using var connection = _connectionFactory.Create();

            // Verificar que existe
            var exists = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM Empresas WHERE Id = @Id",
                new { Id = id }) > 0;

            if (!exists)
                return NotFound();

            // Construir UPDATE dinámico solo con campos que tienen valor
            var updates = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("Id", id);

            if (!string.IsNullOrWhiteSpace(dto.Nombre))
            {
                updates.Add("Nombre = @Nombre");
                parameters.Add("Nombre", dto.Nombre.Trim());
            }
            if (dto.Rnc != null)
            {
                updates.Add("Rnc = @Rnc");
                parameters.Add("Rnc", dto.Rnc.Trim());
            }
            if (dto.Limite_Credito.HasValue)
            {
                updates.Add("Limite_Credito = @Limite_Credito");
                parameters.Add("Limite_Credito", dto.Limite_Credito.Value);
            }
            if (dto.Activo.HasValue)
            {
                updates.Add("Activo = @Activo");
                parameters.Add("Activo", dto.Activo.Value);
            }
            if (dto.Telefono != null)
            {
                updates.Add("Telefono = @Telefono");
                parameters.Add("Telefono", dto.Telefono.Trim());
            }
            if (dto.Email != null)
            {
                updates.Add("Email = @Email");
                parameters.Add("Email", dto.Email.Trim());
            }
            if (dto.Direccion != null)
            {
                updates.Add("Direccion = @Direccion");
                parameters.Add("Direccion", dto.Direccion.Trim());
            }

            if (updates.Count > 0)
            {
                var sql = $"UPDATE Empresas SET {string.Join(", ", updates)} WHERE Id = @Id";
                await connection.ExecuteAsync(sql, parameters);
            }

            return NoContent();
        }

        /// <summary>
        /// PUT /api/empresas/{id}/dia-corte
        /// </summary>
        [HttpPut("{id:int}/dia-corte")]
        [Authorize(Roles = "administrador,contabilidad")]
        public async Task<IActionResult> ActualizarDiaCorte(int id, [FromBody] ActualizarDiaCorteDto dto)
        {
            // Validar que el día sea válido (1-28)
            if (dto.DiaCorte < 1 || dto.DiaCorte > 28)
                return BadRequest(new { message = "El día de corte debe estar entre 1 y 28." });

            using var connection = _connectionFactory.Create();

            var affected = await connection.ExecuteAsync(
                "UPDATE Empresas SET DiaCorte = @DiaCorte WHERE Id = @Id",
                new { Id = id, dto.DiaCorte });

            if (affected == 0)
                return NotFound(new { message = "Empresa no encontrada." });

            return Ok(new
            {
                message = "Día de corte actualizado correctamente.",
                empresaId = id,
                diaCorte = dto.DiaCorte,
                proximoCorte = CalcularProximoCorte(dto.DiaCorte)
            });
        }

        // Método auxiliar para calcular próximo corte
        private static DateTime CalcularProximoCorte(int diaCorte)
        {
            var hoy = DateTime.Today;
            var fechaCorte = new DateTime(hoy.Year, hoy.Month, Math.Min(diaCorte, DateTime.DaysInMonth(hoy.Year, hoy.Month)));

            if (hoy.Day > diaCorte)
            {
                fechaCorte = fechaCorte.AddMonths(1);
                fechaCorte = new DateTime(fechaCorte.Year, fechaCorte.Month, Math.Min(diaCorte, DateTime.DaysInMonth(fechaCorte.Year, fechaCorte.Month)));
            }

            return fechaCorte;
        }
    }

    #region DTOs

    public class EmpresaListDto
    {
        public int Id { get; set; }
        public string Rnc { get; set; } = "";
        public string Nombre { get; set; } = "";
        public int Empleados { get; set; }
        public DateTime? CreatedAt { get; set; }
        public bool Activo { get; set; }
    }

    public class EmpresaDetailDto
    {
        public int Id { get; set; }
        public string Rnc { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string? Telefono { get; set; }
        public string? Email { get; set; }
        public string? Direccion { get; set; }
        public bool Activo { get; set; }
        public decimal? LimiteCredito { get; set; }
        public int? DiaCorte { get; set; }
        public List<EmpresaEmpleadoDto> Empleados { get; set; } = new();
    }

    public class EmpresaEmpleadoDto
    {
        public int Id { get; set; }
        public string? Codigo { get; set; }
        public string Nombre { get; set; } = "";
        public string? Cedula { get; set; }
        public string? Grupo { get; set; }
        public decimal Saldo { get; set; }
        public bool Activo { get; set; }
    }

    public class CreateEmpresaDto
    {
        public int Id { get; set; }
        public string Rnc { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string? Direccion { get; set; }
        public string? Email { get; set; }
        public string? Telefono { get; set; }
        public decimal LimiteCredito { get; set; }
    }

    public class EmpresaUpdateDto
    {
        public string? Nombre { get; set; }
        public string? Rnc { get; set; }
        public decimal? Limite_Credito { get; set; }
        public bool? Activo { get; set; }
        public string? Telefono { get; set; }
        public string? Email { get; set; }
        public string? Direccion { get; set; }
    }

    public class ActualizarDiaCorteDto
    {
        public int DiaCorte { get; set; }
    }

    #endregion
}