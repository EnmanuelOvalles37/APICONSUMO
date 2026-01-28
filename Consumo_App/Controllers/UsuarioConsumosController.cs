// Controllers/UsuarioConsumosController.cs
using Consumo_App.Data.Sql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/usuarios/{usuarioId}/consumos-registrados")]
    [Authorize]
    public class UsuarioConsumosController : ControllerBase
    {
        private readonly SqlConnectionFactory _db;

        public UsuarioConsumosController(SqlConnectionFactory db)
        {
            _db = db;
        }

        // GET /api/usuarios/{usuarioId}/consumos-registrados?tiendaId=&desde=&hasta=
        [HttpGet]
        public async Task<IActionResult> GetConsumosRegistrados(
            int usuarioId,
            [FromQuery] int? tiendaId,
            [FromQuery] DateTime? desde,
            [FromQuery] DateTime? hasta)
        {
            using var conn = _db.Create();

            var sql = @"
                SELECT 
                    c.Id,
                    c.Fecha,
                    cl.Nombre AS ClienteNombre,
                    ISNULL(cl.Cedula, '') AS ClienteCedula,
                    ISNULL(ca.Nombre, '') AS CajaNombre,
                    c.Monto,
                    c.Concepto,
                    c.Reversado
                FROM Consumos c
                LEFT JOIN Clientes cl ON c.ClienteId = cl.Id
                LEFT JOIN ProveedorCajas ca ON c.CajaId = ca.Id
                WHERE c.UsuarioRegistradorId = @UsuarioId";

            if (tiendaId.HasValue)
                sql += " AND c.TiendaId = @TiendaId";

            if (desde.HasValue)
                sql += " AND c.Fecha >= @Desde";

            if (hasta.HasValue)
                sql += " AND c.Fecha <= @Hasta";

            sql += " ORDER BY c.Fecha DESC";

            var data = await conn.QueryAsync<ConsumoRegistradoDto>(sql, new
            {
                UsuarioId = usuarioId,
                TiendaId = tiendaId,
                Desde = desde,
                Hasta = hasta
            });

            var dataList = data.ToList();
            var total = dataList.Where(c => !c.Reversado).Sum(c => c.Monto);

            return Ok(new
            {
                Data = dataList,
                MontoTotal = total,
                TotalConsumos = dataList.Count(c => !c.Reversado)
            });
        }
    }

    public class ConsumoRegistradoDto
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public string ClienteNombre { get; set; } = "";
        public string ClienteCedula { get; set; } = "";
        public string CajaNombre { get; set; } = "";
        public decimal Monto { get; set; }
        public string? Concepto { get; set; }
        public bool Reversado { get; set; }
    }
}