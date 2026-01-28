using Dapper;
using Consumo_App.Data.Sql;
using Consumo_App.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ProveedoresController : ControllerBase
    {
        private readonly SqlConnectionFactory _connectionFactory;

        public ProveedoresController(SqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        /// <summary>
        /// GET /api/proveedores?q=...
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ProveedorListDto>>> Get([FromQuery] string? q)
        {
            using var connection = _connectionFactory.Create();

            var whereClause = "WHERE 1=1";
            var parameters = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(q))
            {
                whereClause += " AND (p.Nombre LIKE @Search OR p.Rnc LIKE @Search)";
                parameters.Add("Search", $"%{q.Trim()}%");
            }

            var sql = $@"
                SELECT p.Id, p.Rnc, p.Nombre, p.Activo
                FROM Proveedores p
                {whereClause}
                ORDER BY p.Nombre";

            var list = await connection.QueryAsync<ProveedorListDto>(sql, parameters);
            return Ok(list);
        }

        /// <summary>
        /// GET /api/proveedores/{id} -> detalle (incluye tiendas)
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<ActionResult<ProveedorDetailDto>> GetDetail(int id)
        {
            using var connection = _connectionFactory.Create();

            // Obtener proveedor
            const string sqlProveedor = @"
                SELECT Id, Nombre, Rnc, Activo, Direccion, Telefono, Email, 
                       Contacto, DiasCorte, PorcentajeComision, CreadoUtc
                FROM Proveedores
                WHERE Id = @Id";

            var prov = await connection.QueryFirstOrDefaultAsync<dynamic>(sqlProveedor, new { Id = id });

            if (prov == null)
                return NotFound();

            // Obtener tiendas
            const string sqlTiendas = @"
                SELECT Id, Nombre, Activo
                FROM ProveedorTiendas
                WHERE ProveedorId = @ProveedorId
                ORDER BY Nombre";

            var tiendas = (await connection.QueryAsync<ProveedorTiendaDto>(
                sqlTiendas, new { ProveedorId = id })).ToList();

            return Ok(new ProveedorDetailDto
            {
                Id = prov.Id,
                Nombre = prov.Nombre,
                Rnc = prov.Rnc,
                Activo = prov.Activo,
                Direccion = prov.Direccion,
                Telefono = prov.Telefono,
                Email = prov.Email,
                Contacto = prov.Contacto,
                DiasCorte = prov.DiasCorte,
                PorcentajeComision = prov.PorcentajeComision,
                CreadoUtc = prov.CreadoUtc,
                Tiendas = tiendas
            });
        }

        /// <summary>
        /// GET /api/proveedores/{id}/form -> datos para formulario de edición
        /// </summary>
        [HttpGet("{id:int}/form")]
        public async Task<ActionResult<ProveedorFormDto>> GetForm(int id)
        {
            using var connection = _connectionFactory.Create();

            const string sql = @"
                SELECT Rnc, Nombre, Direccion, Telefono, Email, Contacto, 
                       DiasCorte, PorcentajeComision, Activo
                FROM Proveedores
                WHERE Id = @Id";

            var p = await connection.QueryFirstOrDefaultAsync<ProveedorFormDto>(sql, new { Id = id });

            if (p == null)
                return NotFound();

            return Ok(p);
        }

        /// <summary>
        /// POST /api/proveedores
        /// </summary>
        [HttpPost]
        public async Task<ActionResult> Create([FromBody] ProveedorFormDto dto)
        {
            using var connection = _connectionFactory.Create();

            const string sql = @"
                INSERT INTO Proveedores 
                    (Nombre, Rnc, Direccion, Telefono, Email, Contacto, DiasCorte, PorcentajeComision, Activo, CreadoUtc)
                OUTPUT INSERTED.Id, INSERTED.Nombre, INSERTED.Rnc, INSERTED.Activo, 
                       INSERTED.Direccion, INSERTED.Telefono, INSERTED.Email, INSERTED.Contacto,
                       INSERTED.DiasCorte, INSERTED.PorcentajeComision, INSERTED.CreadoUtc
                VALUES 
                    (@Nombre, @Rnc, @Direccion, @Telefono, @Email, @Contacto, @DiasCorte, @PorcentajeComision, @Activo, @CreadoUtc)";

            var result = await connection.QueryFirstAsync<dynamic>(sql, new
            {
                Nombre = dto.Nombre.Trim(),
                Rnc = dto.Rnc?.Trim(),
                Direccion = dto.Direccion?.Trim(),
                Telefono = dto.Telefono?.Trim(),
                Email = dto.Email?.Trim(),
                Contacto = dto.Contacto?.Trim(),
                dto.DiasCorte,
                dto.PorcentajeComision,
                dto.Activo,
                CreadoUtc = DateTime.UtcNow
            });

            return CreatedAtAction(nameof(GetDetail), new { id = result.Id }, new
            {
                result.Id,
                result.Nombre,
                result.Rnc,
                result.Activo,
                result.Direccion,
                result.Telefono,
                result.Email,
                result.Contacto,
                result.DiasCorte,
                result.PorcentajeComision,
                result.CreadoUtc
            });
        }

        /// <summary>
        /// PUT /api/proveedores/{id}
        /// </summary>
        [HttpPut("{id:int}")]
        public async Task<ActionResult> Update(int id, [FromBody] ProveedorFormDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Nombre))
                return BadRequest(new { message = "El nombre es requerido." });

            using var connection = _connectionFactory.Create();

            // Verificar que existe
            var exists = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM Proveedores WHERE Id = @Id",
                new { Id = id }) > 0;

            if (!exists)
                return NotFound(new { message = "Proveedor no encontrado." });

            const string sql = @"
                UPDATE Proveedores SET
                    Nombre = @Nombre,
                    Rnc = @Rnc,
                    Direccion = @Direccion,
                    Telefono = @Telefono,
                    Email = @Email,
                    Contacto = @Contacto,
                    DiasCorte = @DiasCorte,
                    PorcentajeComision = @PorcentajeComision,
                    Activo = CASE WHEN @Activo = 1 THEN @Activo ELSE Activo END
                WHERE Id = @Id;

                SELECT Id, Nombre, Rnc, Direccion, Telefono, Email, Contacto, 
                       DiasCorte, PorcentajeComision, Activo, CreadoUtc
                FROM Proveedores WHERE Id = @Id";

            var p = await connection.QueryFirstAsync<dynamic>(sql, new
            {
                Id = id,
                Nombre = dto.Nombre.Trim(),
                Rnc = dto.Rnc?.Trim(),
                Direccion = dto.Direccion?.Trim(),
                Telefono = dto.Telefono?.Trim(),
                Email = dto.Email?.Trim(),
                Contacto = dto.Contacto?.Trim(),
                dto.DiasCorte,
                dto.PorcentajeComision,
                Activo = dto.Activo ? 1 : 0
            });

            return Ok(new
            {
                p.Id,
                p.Nombre,
                p.Rnc,
                p.Direccion,
                p.Telefono,
                p.Email,
                p.Contacto,
                p.DiasCorte,
                p.PorcentajeComision,
                p.Activo,
                p.CreadoUtc,
                message = "Proveedor actualizado correctamente."
            });
        }

        /// <summary>
        /// GET /api/proveedores/lookup?q=...
        /// </summary>
        [HttpGet("lookup")]
        public async Task<ActionResult<PagedLookupDto>> Lookup(
            [FromQuery] string? q,
            [FromQuery] bool activeOnly = true,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            using var connection = _connectionFactory.Create();

            var whereClause = "WHERE 1=1";
            var parameters = new DynamicParameters();

            if (activeOnly)
            {
                whereClause += " AND p.Activo = 1";
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                whereClause += " AND (p.Nombre LIKE @Search OR p.Rnc LIKE @Search)";
                parameters.Add("Search", $"%{q.Trim()}%");
            }

            // Contar total
            var countSql = $"SELECT COUNT(*) FROM Proveedores p {whereClause}";
            var total = await connection.ExecuteScalarAsync<int>(countSql, parameters);

            // Obtener datos paginados
            parameters.Add("Offset", (page - 1) * pageSize);
            parameters.Add("PageSize", pageSize);

            var dataSql = $@"
                SELECT 
                    CAST(p.Id AS VARCHAR(20)) AS Value,
                    p.Nombre + ' · ' + ISNULL(p.Rnc, '') AS Label
                FROM Proveedores p
                {whereClause}
                ORDER BY p.Nombre
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var items = (await connection.QueryAsync<LookupItemDto>(dataSql, parameters)).ToList();

            return Ok(new PagedLookupDto(items, page, pageSize, total));
        }

        #region Tiendas

        /// <summary>
        /// POST /api/proveedores/{proveedorId}/tiendas
        /// </summary>
        [HttpPost("{proveedorId:int}/tiendas")]
        public async Task<IActionResult> CreateTienda(int proveedorId, [FromBody] ProveedorTiendaDto dto)
        {
            using var connection = _connectionFactory.Create();

            const string sql = @"
                INSERT INTO ProveedorTiendas (ProveedorId, Nombre, Activo)
                OUTPUT INSERTED.Id
                VALUES (@ProveedorId, @Nombre, 1)";

            var tiendaId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                ProveedorId = proveedorId,
                Nombre = dto.Nombre
            });

            dto.Id = tiendaId;

            return CreatedAtAction(nameof(GetDetail), new { id = proveedorId }, dto);
        }

        /// <summary>
        /// PUT /api/proveedores/{proveedorId}/tiendas/{id}
        /// </summary>
        [HttpPut("{proveedorId:int}/tiendas/{id:int}")]
        public async Task<IActionResult> UpdateTienda(int proveedorId, int id, [FromBody] ProveedorTiendaDto dto)
        {
            using var connection = _connectionFactory.Create();

            var affected = await connection.ExecuteAsync(@"
                UPDATE ProveedorTiendas 
                SET Nombre = @Nombre, Activo = @Activo
                WHERE Id = @Id AND ProveedorId = @ProveedorId",
                new { Id = id, ProveedorId = proveedorId, dto.Nombre, dto.Activo });

            if (affected == 0)
                return NotFound();

            return NoContent();
        }

        /// <summary>
        /// DELETE /api/proveedores/{proveedorId}/tiendas/{id}
        /// </summary>
        [HttpDelete("{proveedorId:int}/tiendas/{id:int}")]
        public async Task<IActionResult> DeleteTienda(int proveedorId, int id)
        {
            using var connection = _connectionFactory.Create();

            var affected = await connection.ExecuteAsync(@"
                DELETE FROM ProveedorTiendas 
                WHERE Id = @Id AND ProveedorId = @ProveedorId",
                new { Id = id, ProveedorId = proveedorId });

            if (affected == 0)
                return NotFound();

            return NoContent();
        }

        #endregion
    }

    #region DTOs

    public record ProveedorListDto(int Id, string? Rnc, string Nombre, bool Activo);

    public class ProveedorDetailDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
        public string? Rnc { get; set; }
        public bool Activo { get; set; }
        public string? Direccion { get; set; }
        public string? Telefono { get; set; }
        public string? Email { get; set; }
        public string? Contacto { get; set; }
        public int? DiasCorte { get; set; }
        public decimal PorcentajeComision { get; set; }
        public DateTime? CreadoUtc { get; set; }
        public List<ProveedorTiendaDto> Tiendas { get; set; } = new();
    }

    public class ProveedorFormDto
    {
        public string? Rnc { get; set; }
        public string Nombre { get; set; } = "";
        public string? Direccion { get; set; }
        public string? Telefono { get; set; }
        public string? Email { get; set; }
        public string? Contacto { get; set; }
        public int? DiasCorte { get; set; }
        public decimal PorcentajeComision { get; set; }
        public bool Activo { get; set; }
    }

    public class ProveedorTiendaDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
        public bool Activo { get; set; }
    }

    public record LookupItemDto(string Value, string Label);
    public record PagedLookupDto(List<LookupItemDto> Items, int Page, int PageSize, int Total);

    #endregion
}