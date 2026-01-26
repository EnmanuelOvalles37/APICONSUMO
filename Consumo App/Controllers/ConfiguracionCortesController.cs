using Consumo_App.Data;
using Consumo_App.Data.Sql;
using Consumo_App.Servicios;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/configuracion-cortes")]
    [Authorize]
    public class ConfiguracionCortesController : ControllerBase
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly IUserContext _user;

        public ConfiguracionCortesController(SqlConnectionFactory connectionFactory, IUserContext user)
        {
            _connectionFactory = connectionFactory;
            _user = user;
        }

        /// <summary>
        /// GET /api/configuracion-cortes
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Listar()
        {
            using var connection = _connectionFactory.Create();

            var configuraciones = await connection.QueryAsync<ConfiguracionCorteListaDto>(
                @"SELECT 
                    c.Id,
                    c.EmpresaId,
                    e.Nombre AS EmpresaNombre,
                    e.Rnc AS EmpresaRnc,
                    c.DiaCorte,
                    c.DiasGracia,
                    c.CorteAutomatico,
                    c.CreadoUtc
                FROM ConfiguracionCortes c
                INNER JOIN Empresas e ON c.EmpresaId = e.Id
                ORDER BY e.Nombre");

            var configList = configuraciones.ToList();
            var empresasConConfig = configList.Select(c => c.EmpresaId).ToList();

            var empresasSinConfig = await connection.QueryAsync<EmpresaSimpleDto>(
                @"SELECT Id, Nombre, Rnc
                FROM Empresas
                WHERE Activo = 1 AND Id NOT IN @ids",
                new { ids = empresasConConfig.Count > 0 ? empresasConConfig : new List<int> { -1 } });

            var empresasSinConfigList = empresasSinConfig.ToList();

            return Ok(new
            {
                configuraciones = configList,
                empresasSinConfiguracion = empresasSinConfigList,
                resumen = new
                {
                    TotalConfiguradas = configList.Count,
                    ConCorteAutomatico = configList.Count(c => c.CorteAutomatico),
                    SinConfigurar = empresasSinConfigList.Count
                }
            });
        }

        /// <summary>
        /// GET /api/configuracion-cortes/empresa/{empresaId}
        /// </summary>
        [HttpGet("empresa/{empresaId:int}")]
        public async Task<IActionResult> ObtenerPorEmpresa(int empresaId)
        {
            using var connection = _connectionFactory.Create();

            var empresa = await connection.QueryFirstOrDefaultAsync<EmpresaSimpleDto>(
                "SELECT Id, Nombre, Rnc FROM Empresas WHERE Id = @empresaId",
                new { empresaId });

            if (empresa == null)
                return NotFound(new { message = "Empresa no encontrada." });

            var config = await connection.QueryFirstOrDefaultAsync<ConfiguracionCorteDetalleDto>(
                @"SELECT Id, DiaCorte, DiasGracia, CorteAutomatico, CreadoUtc
                FROM ConfiguracionCortes
                WHERE EmpresaId = @empresaId",
                new { empresaId });

            if (config == null)
            {
                return Ok(new
                {
                    existe = false,
                    empresa,
                    configuracion = (object?)null,
                    mensaje = "Esta empresa no tiene configuración de corte. Se usarán valores por defecto."
                });
            }

            return Ok(new
            {
                existe = true,
                empresa,
                configuracion = config
            });
        }

        /// <summary>
        /// POST /api/configuracion-cortes
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CrearOActualizar([FromBody] ConfiguracionCorteDto dto)
        {
            if (dto.DiaCorte < 1 || dto.DiaCorte > 28)
                return BadRequest(new { message = "El día de corte debe estar entre 1 y 28." });

            if (dto.DiasGracia < 0 || dto.DiasGracia > 30)
                return BadRequest(new { message = "Los días de gracia deben estar entre 0 y 30." });

            using var connection = _connectionFactory.Create();

            var empresaExiste = await connection.ExecuteScalarAsync<bool>(
                "SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM Empresas WHERE Id = @empresaId) THEN 1 ELSE 0 END AS BIT)",
                new { empresaId = dto.EmpresaId });

            if (!empresaExiste)
                return BadRequest(new { message = "Empresa no encontrada." });

            var configExistenteId = await connection.QueryFirstOrDefaultAsync<int?>(
                "SELECT Id FROM ConfiguracionCortes WHERE EmpresaId = @empresaId",
                new { empresaId = dto.EmpresaId });

            if (configExistenteId.HasValue)
            {
                await connection.ExecuteAsync(
                    @"UPDATE ConfiguracionCortes 
                    SET DiaCorte = @diaCorte,
                        DiasGracia = @diasGracia,
                        CorteAutomatico = @corteAutomatico
                    WHERE Id = @id",
                    new
                    {
                        diaCorte = dto.DiaCorte,
                        diasGracia = dto.DiasGracia,
                        corteAutomatico = dto.CorteAutomatico,
                        id = configExistenteId.Value
                    });

                return Ok(new
                {
                    Id = configExistenteId.Value,
                    mensaje = "Configuración actualizada exitosamente.",
                    accion = "actualizado"
                });
            }
            else
            {
                var nuevoId = await connection.QuerySingleAsync<int>(
                    @"INSERT INTO ConfiguracionCortes (EmpresaId, DiaCorte, DiasGracia, CorteAutomatico, CreadoUtc)
                    OUTPUT INSERTED.Id
                    VALUES (@empresaId, @diaCorte, @diasGracia, @corteAutomatico, @creadoUtc)",
                    new
                    {
                        empresaId = dto.EmpresaId,
                        diaCorte = dto.DiaCorte,
                        diasGracia = dto.DiasGracia,
                        corteAutomatico = dto.CorteAutomatico,
                        creadoUtc = DateTime.UtcNow
                    });

                return Ok(new
                {
                    Id = nuevoId,
                    mensaje = "Configuración creada exitosamente.",
                    accion = "creado"
                });
            }
        }

        /// <summary>
        /// PATCH /api/configuracion-cortes/{id}/toggle-automatico
        /// </summary>
        [HttpPatch("{id:int}/toggle-automatico")]
        public async Task<IActionResult> ToggleAutomatico(int id)
        {
            using var connection = _connectionFactory.Create();

            var config = await connection.QueryFirstOrDefaultAsync<ConfiguracionToggleDto>(
                @"SELECT c.Id, c.CorteAutomatico, e.Nombre AS EmpresaNombre
                FROM ConfiguracionCortes c
                INNER JOIN Empresas e ON c.EmpresaId = e.Id
                WHERE c.Id = @id",
                new { id });

            if (config == null)
                return NotFound(new { message = "Configuración no encontrada." });

            var nuevoValor = !config.CorteAutomatico;

            await connection.ExecuteAsync(
                "UPDATE ConfiguracionCortes SET CorteAutomatico = @corteAutomatico WHERE Id = @id",
                new { corteAutomatico = nuevoValor, id });

            return Ok(new
            {
                Id = id,
                CorteAutomatico = nuevoValor,
                mensaje = nuevoValor
                    ? $"Corte automático ACTIVADO para {config.EmpresaNombre}"
                    : $"Corte automático DESACTIVADO para {config.EmpresaNombre}"
            });
        }

        /// <summary>
        /// DELETE /api/configuracion-cortes/{id}
        /// </summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Eliminar(int id)
        {
            using var connection = _connectionFactory.Create();

            var filas = await connection.ExecuteAsync(
                "DELETE FROM ConfiguracionCortes WHERE Id = @id",
                new { id });

            if (filas == 0)
                return NotFound(new { message = "Configuración no encontrada." });

            return Ok(new { mensaje = "Configuración eliminada. La empresa usará valores por defecto." });
        }

        /// <summary>
        /// POST /api/configuracion-cortes/masivo
        /// </summary>
        [HttpPost("masivo")]
        public async Task<IActionResult> ConfiguracionMasiva([FromBody] ConfiguracionMasivaDto dto)
        {
            if (dto.EmpresaIds == null || !dto.EmpresaIds.Any())
                return BadRequest(new { message = "Debe seleccionar al menos una empresa." });

            if (dto.DiaCorte < 1 || dto.DiaCorte > 28)
                return BadRequest(new { message = "El día de corte debe estar entre 1 y 28." });

            using var connection = _connectionFactory.Create();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                int creados = 0, actualizados = 0;

                foreach (var empresaId in dto.EmpresaIds)
                {
                    var configId = await connection.QueryFirstOrDefaultAsync<int?>(
                        "SELECT Id FROM ConfiguracionCortes WHERE EmpresaId = @empresaId",
                        new { empresaId },
                        transaction);

                    if (configId.HasValue)
                    {
                        await connection.ExecuteAsync(
                            @"UPDATE ConfiguracionCortes 
                            SET DiaCorte = @diaCorte,
                                DiasGracia = @diasGracia,
                                CorteAutomatico = @corteAutomatico
                            WHERE Id = @id",
                            new
                            {
                                diaCorte = dto.DiaCorte,
                                diasGracia = dto.DiasGracia,
                                corteAutomatico = dto.CorteAutomatico,
                                id = configId.Value
                            },
                            transaction);
                        actualizados++;
                    }
                    else
                    {
                        await connection.ExecuteAsync(
                            @"INSERT INTO ConfiguracionCortes (EmpresaId, DiaCorte, DiasGracia, CorteAutomatico, CreadoUtc)
                            VALUES (@empresaId, @diaCorte, @diasGracia, @corteAutomatico, @creadoUtc)",
                            new
                            {
                                empresaId,
                                diaCorte = dto.DiaCorte,
                                diasGracia = dto.DiasGracia,
                                corteAutomatico = dto.CorteAutomatico,
                                creadoUtc = DateTime.UtcNow
                            },
                            transaction);
                        creados++;
                    }
                }

                transaction.Commit();

                return Ok(new
                {
                    mensaje = $"Configuración aplicada a {dto.EmpresaIds.Count} empresas.",
                    creados,
                    actualizados
                });
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        /// <summary>
        /// GET /api/configuracion-cortes/proximos
        /// </summary>
        [HttpGet("proximos")]
        public async Task<IActionResult> ProximosCortes()
        {
            using var connection = _connectionFactory.Create();

            var configuraciones = await connection.QueryAsync<ConfiguracionProximoCorteDto>(
                @"SELECT 
                    c.Id,
                    c.EmpresaId,
                    e.Nombre AS EmpresaNombre,
                    c.DiaCorte,
                    c.DiasGracia
                FROM ConfiguracionCortes c
                INNER JOIN Empresas e ON c.EmpresaId = e.Id
                WHERE c.CorteAutomatico = 1
                ORDER BY c.DiaCorte");

            var hoy = DateTime.Now;
            var diaActual = hoy.Day;

            var proximos = configuraciones.Select(c =>
            {
                DateTime proximoCorte;
                if (c.DiaCorte >= diaActual)
                {
                    proximoCorte = new DateTime(hoy.Year, hoy.Month, c.DiaCorte);
                }
                else
                {
                    var proximoMes = hoy.AddMonths(1);
                    proximoCorte = new DateTime(proximoMes.Year, proximoMes.Month, c.DiaCorte);
                }

                return new
                {
                    c.Id,
                    c.EmpresaId,
                    c.EmpresaNombre,
                    c.DiaCorte,
                    c.DiasGracia,
                    ProximoCorte = proximoCorte,
                    DiasRestantes = (proximoCorte - hoy.Date).Days
                };
            })
            .OrderBy(x => x.DiasRestantes)
            .ToList();

            return Ok(new
            {
                fecha_actual = hoy.ToString("yyyy-MM-dd"),
                proximos_cortes = proximos
            });
        }
    }

    #region DTOs

    public class ConfiguracionCorteDto
    {
        public int EmpresaId { get; set; }
        public int DiaCorte { get; set; } = 1;
        public int DiasGracia { get; set; } = 5;
        public bool CorteAutomatico { get; set; } = true;
    }

    public class ConfiguracionMasivaDto
    {
        public List<int> EmpresaIds { get; set; } = new();
        public int DiaCorte { get; set; } = 1;
        public int DiasGracia { get; set; } = 5;
        public bool CorteAutomatico { get; set; } = true;
    }

    internal class ConfiguracionCorteListaDto
    {
        public int Id { get; set; }
        public int EmpresaId { get; set; }
        public string EmpresaNombre { get; set; } = null!;
        public string? EmpresaRnc { get; set; }
        public int DiaCorte { get; set; }
        public int DiasGracia { get; set; }
        public bool CorteAutomatico { get; set; }
        public DateTime CreadoUtc { get; set; }
    }

    internal class ConfiguracionCorteDetalleDto
    {
        public int Id { get; set; }
        public int DiaCorte { get; set; }
        public int DiasGracia { get; set; }
        public bool CorteAutomatico { get; set; }
        public DateTime CreadoUtc { get; set; }
    }

    internal class EmpresaSimpleDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = null!;
        public string? Rnc { get; set; }
    }

    internal class ConfiguracionToggleDto
    {
        public int Id { get; set; }
        public bool CorteAutomatico { get; set; }
        public string EmpresaNombre { get; set; } = null!;
    }

    internal class ConfiguracionProximoCorteDto
    {
        public int Id { get; set; }
        public int EmpresaId { get; set; }
        public string EmpresaNombre { get; set; } = null!;
        public int DiaCorte { get; set; }
        public int DiasGracia { get; set; }
    }

    #endregion
}