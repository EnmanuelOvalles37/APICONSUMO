using Dapper;
using Consumo_App.Data.Sql;
using Consumo_App.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/proveedores/{proveedorId:int}")]
    [Authorize]
    public class ProveedorUsuariosController : ControllerBase
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly IPasswordHasher _hasher;

        public ProveedorUsuariosController(SqlConnectionFactory connectionFactory, IPasswordHasher hasher)
        {
            _connectionFactory = connectionFactory;
            _hasher = hasher;
        }

        #region ========== USUARIOS ==========

        /// <summary>
        /// GET /api/proveedores/{proveedorId}/usuarios
        /// </summary>
        [HttpGet("usuarios")]
        public async Task<IActionResult> ListarUsuarios(int proveedorId)
        {
            using var connection = _connectionFactory.Create();

            // Verificar que el proveedor existe
            var proveedorExiste = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM Proveedores WHERE Id = @Id",
                new { Id = proveedorId }) > 0;

            if (!proveedorExiste)
                return NotFound(new { message = "Proveedor no encontrado." });

            // Obtener todas las asignaciones con datos relacionados
            const string sql = @"
                SELECT 
                    a.Id AS AsignacionId,
                    a.UsuarioId,
                    a.TiendaId,
                    a.CajaId,
                    a.Rol,
                    a.Activo AS AsignacionActiva,
                    u.Nombre AS UsuarioNombre,
                    u.Activo AS UsuarioActivo,
                    t.Nombre AS TiendaNombre,
                    c.Nombre AS CajaNombre
                FROM ProveedorAsignaciones a
                INNER JOIN Usuarios u ON a.UsuarioId = u.Id
                LEFT JOIN ProveedorTiendas t ON a.TiendaId = t.Id
                LEFT JOIN ProveedorCajas c ON a.CajaId = c.Id
                WHERE a.ProveedorId = @ProveedorId
                ORDER BY u.Nombre, a.Id";

            var asignaciones = (await connection.QueryAsync<dynamic>(sql, new { ProveedorId = proveedorId })).ToList();

            // Agrupar por usuario en memoria
            var usuariosAgrupados = asignaciones
                .GroupBy(a => (int)a.UsuarioId)
                .Select(g => new
                {
                    UsuarioId = g.Key,
                    Nombre = (string)(g.First().UsuarioNombre ?? "Sin nombre"),
                    Activo = (bool)g.First().UsuarioActivo,
                    TotalAsignaciones = g.Count(),
                    Asignaciones = g.Select(a => new
                    {
                        AsignacionId = (int)a.AsignacionId,
                        TiendaId = (int?)a.TiendaId,
                        TiendaNombre = (string?)a.TiendaNombre,
                        CajaId = (int?)a.CajaId,
                        CajaNombre = (string?)a.CajaNombre,
                        Rol = (string)a.Rol,
                        Nivel = GetNivelAcceso((int?)a.TiendaId, (int?)a.CajaId, (string)a.Rol),
                        Activo = (bool)a.AsignacionActiva
                    }).ToList()
                })
                .OrderBy(u => u.Nombre)
                .ToList();

            return Ok(usuariosAgrupados);
        }

        /// <summary>
        /// POST /api/proveedores/{proveedorId}/usuarios
        /// </summary>
        [HttpPost("usuarios")]
        public async Task<IActionResult> CrearUsuario(int proveedorId, [FromBody] CrearUsuarioCajeroDto dto)
        {
            using var connection = _connectionFactory.Create();
            await connection.OpenAsync();

            // Validar proveedor
            var proveedorExiste = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM Proveedores WHERE Id = @Id",
                new { Id = proveedorId }) > 0;

            if (!proveedorExiste)
                return NotFound(new { message = "Proveedor no encontrado." });

            // Validar campos requeridos
            if (string.IsNullOrWhiteSpace(dto.Nombre))
                return BadRequest(new { message = "El nombre de usuario es requerido." });

            if (string.IsNullOrWhiteSpace(dto.Contrasena) || dto.Contrasena.Length < 6)
                return BadRequest(new { message = "La contraseña debe tener al menos 6 caracteres." });

            // Validar nombre de usuario único
            var nombreExiste = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM Usuarios WHERE Nombre = @Nombre",
                new { Nombre = dto.Nombre.Trim() }) > 0;

            if (nombreExiste)
                return BadRequest(new { message = $"Ya existe un usuario con el nombre '{dto.Nombre}'." });

            // Validar tienda y caja según el rol
            string? tiendaNombre = null;
            string? cajaNombre = null;

            if (dto.Rol != "admin")
            {
                if (!dto.TiendaId.HasValue)
                    return BadRequest(new { message = "Debe seleccionar una tienda." });

                var tienda = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT Id, Nombre FROM ProveedorTiendas 
                    WHERE Id = @TiendaId AND ProveedorId = @ProveedorId",
                    new { dto.TiendaId, ProveedorId = proveedorId });

                if (tienda == null)
                    return BadRequest(new { message = "La tienda seleccionada no existe o no pertenece a este proveedor." });

                tiendaNombre = tienda.Nombre;

                if (dto.Rol == "cajero" || dto.Rol == null)
                {
                    if (!dto.CajaId.HasValue)
                        return BadRequest(new { message = "Debe seleccionar una caja." });

                    var caja = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                        SELECT Id, Nombre FROM ProveedorCajas 
                        WHERE Id = @CajaId AND TiendaId = @TiendaId",
                        new { dto.CajaId, dto.TiendaId });

                    if (caja == null)
                        return BadRequest(new { message = "La caja seleccionada no existe o no pertenece a la tienda." });

                    cajaNombre = caja.Nombre;
                }
            }

            using var transaction = connection.BeginTransaction();

            try
            {
                // Obtener o crear rol cajero
                var rolCajeroId = await connection.ExecuteScalarAsync<int?>(
                    "SELECT Id FROM Roles WHERE Nombre = 'cajero'",
                    transaction: transaction);

                if (!rolCajeroId.HasValue)
                {
                    rolCajeroId = await connection.ExecuteScalarAsync<int>(@"
                        INSERT INTO Roles (Nombre, Descripcion) 
                        OUTPUT INSERTED.Id 
                        VALUES ('cajero', 'Usuario cajero de proveedor')",
                        transaction: transaction);
                }

                // Crear el usuario
                const string sqlUsuario = @"
                    INSERT INTO Usuarios (Nombre, Contrasena, RolId, Activo, CreadoUtc)
                    OUTPUT INSERTED.Id
                    VALUES (@Nombre, @Contrasena, @RolId, 1, @CreadoUtc)";

                var usuarioId = await connection.ExecuteScalarAsync<int>(sqlUsuario, new
                {
                    Nombre = dto.Nombre.Trim(),
                    Contrasena = _hasher.Hash(dto.Contrasena),
                    RolId = rolCajeroId.Value,
                    CreadoUtc = DateTime.UtcNow
                }, transaction);

                // Crear la asignación inicial
                const string sqlAsignacion = @"
                    INSERT INTO ProveedorAsignaciones (ProveedorId, UsuarioId, TiendaId, CajaId, Rol, Activo)
                    OUTPUT INSERTED.Id
                    VALUES (@ProveedorId, @UsuarioId, @TiendaId, @CajaId, @Rol, 1)";

                var asignacionId = await connection.ExecuteScalarAsync<int>(sqlAsignacion, new
                {
                    ProveedorId = proveedorId,
                    UsuarioId = usuarioId,
                    dto.TiendaId,
                    dto.CajaId,
                    Rol = dto.Rol ?? "cajero"
                }, transaction);

                transaction.Commit();

                return Ok(new
                {
                    mensaje = "Usuario creado exitosamente.",
                    usuarioId,
                    asignacionId,
                    nombre = dto.Nombre.Trim(),
                    rol = dto.Rol ?? "cajero",
                    tienda = tiendaNombre,
                    caja = cajaNombre
                });
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        /// <summary>
        /// PATCH /api/proveedores/{proveedorId}/usuarios/{usuarioId}/desactivar
        /// </summary>
        [HttpPatch("usuarios/{usuarioId:int}/desactivar")]
        public async Task<IActionResult> DesactivarUsuario(int proveedorId, int usuarioId)
        {
            using var connection = _connectionFactory.Create();
            await connection.OpenAsync();

            // Verificar que el usuario tiene asignación a este proveedor
            var tieneAsignacion = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM ProveedorAsignaciones 
                WHERE ProveedorId = @ProveedorId AND UsuarioId = @UsuarioId",
                new { ProveedorId = proveedorId, UsuarioId = usuarioId }) > 0;

            if (!tieneAsignacion)
                return NotFound(new { message = "Usuario no encontrado en este proveedor." });

            var usuarioExiste = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM Usuarios WHERE Id = @Id",
                new { Id = usuarioId }) > 0;

            if (!usuarioExiste)
                return NotFound(new { message = "Usuario no encontrado." });

            using var transaction = connection.BeginTransaction();

            try
            {
                // Desactivar usuario
                await connection.ExecuteAsync(
                    "UPDATE Usuarios SET Activo = 0 WHERE Id = @Id",
                    new { Id = usuarioId }, transaction);

                // Desactivar todas sus asignaciones en este proveedor
                await connection.ExecuteAsync(@"
                    UPDATE ProveedorAsignaciones SET Activo = 0 
                    WHERE ProveedorId = @ProveedorId AND UsuarioId = @UsuarioId",
                    new { ProveedorId = proveedorId, UsuarioId = usuarioId }, transaction);

                transaction.Commit();

                return Ok(new { mensaje = "Usuario desactivado exitosamente." });
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        /// <summary>
        /// PATCH /api/proveedores/{proveedorId}/usuarios/{usuarioId}/activar
        /// </summary>
        [HttpPatch("usuarios/{usuarioId:int}/activar")]
        public async Task<IActionResult> ActivarUsuario(int proveedorId, int usuarioId)
        {
            using var connection = _connectionFactory.Create();
            await connection.OpenAsync();

            var tieneAsignacion = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM ProveedorAsignaciones 
                WHERE ProveedorId = @ProveedorId AND UsuarioId = @UsuarioId",
                new { ProveedorId = proveedorId, UsuarioId = usuarioId }) > 0;

            if (!tieneAsignacion)
                return NotFound(new { message = "Usuario no encontrado en este proveedor." });

            var usuarioExiste = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM Usuarios WHERE Id = @Id",
                new { Id = usuarioId }) > 0;

            if (!usuarioExiste)
                return NotFound(new { message = "Usuario no encontrado." });

            using var transaction = connection.BeginTransaction();

            try
            {
                // Activar usuario
                await connection.ExecuteAsync(
                    "UPDATE Usuarios SET Activo = 1 WHERE Id = @Id",
                    new { Id = usuarioId }, transaction);

                // Activar sus asignaciones
                await connection.ExecuteAsync(@"
                    UPDATE ProveedorAsignaciones SET Activo = 1 
                    WHERE ProveedorId = @ProveedorId AND UsuarioId = @UsuarioId",
                    new { ProveedorId = proveedorId, UsuarioId = usuarioId }, transaction);

                transaction.Commit();

                return Ok(new { mensaje = "Usuario activado exitosamente." });
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        #endregion

        #region ========== ASIGNACIONES ==========

        /// <summary>
        /// POST /api/proveedores/{proveedorId}/usuarios/{usuarioId}/asignaciones
        /// </summary>
        [HttpPost("usuarios/{usuarioId:int}/asignaciones")]
        public async Task<IActionResult> AgregarAsignacion(
            int proveedorId,
            int usuarioId,
            [FromBody] CrearAsignacionDto dto)
        {
            using var connection = _connectionFactory.Create();

            // Verificar usuario
            var usuarioExiste = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM Usuarios WHERE Id = @Id",
                new { Id = usuarioId }) > 0;

            if (!usuarioExiste)
                return NotFound(new { message = "Usuario no encontrado." });

            // Verificar que el usuario ya tiene al menos una asignación en este proveedor
            var tieneAsignacion = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM ProveedorAsignaciones 
                WHERE ProveedorId = @ProveedorId AND UsuarioId = @UsuarioId",
                new { ProveedorId = proveedorId, UsuarioId = usuarioId }) > 0;

            if (!tieneAsignacion)
                return BadRequest(new { message = "El usuario no pertenece a este proveedor." });

            string? tiendaNombre = null;
            string? cajaNombre = null;

            // Validar tienda
            if (dto.TiendaId.HasValue)
            {
                var tienda = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT Id, Nombre FROM ProveedorTiendas 
                    WHERE Id = @TiendaId AND ProveedorId = @ProveedorId",
                    new { dto.TiendaId, ProveedorId = proveedorId });

                if (tienda == null)
                    return BadRequest(new { message = "La tienda no existe o no pertenece a este proveedor." });

                tiendaNombre = tienda.Nombre;
            }

            // Validar caja
            if (dto.CajaId.HasValue)
            {
                if (!dto.TiendaId.HasValue)
                    return BadRequest(new { message = "Debe seleccionar una tienda para asignar una caja." });

                var caja = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT Id, Nombre FROM ProveedorCajas 
                    WHERE Id = @CajaId AND TiendaId = @TiendaId",
                    new { dto.CajaId, dto.TiendaId });

                if (caja == null)
                    return BadRequest(new { message = "La caja no existe o no pertenece a la tienda." });

                cajaNombre = caja.Nombre;
            }

            // Verificar que no exista una asignación duplicada
            var existeAsignacion = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1) FROM ProveedorAsignaciones 
                WHERE ProveedorId = @ProveedorId 
                  AND UsuarioId = @UsuarioId 
                  AND (TiendaId = @TiendaId OR (TiendaId IS NULL AND @TiendaId IS NULL))
                  AND (CajaId = @CajaId OR (CajaId IS NULL AND @CajaId IS NULL))",
                new { ProveedorId = proveedorId, UsuarioId = usuarioId, dto.TiendaId, dto.CajaId }) > 0;

            if (existeAsignacion)
                return BadRequest(new { message = "Ya existe esta asignación para el usuario." });

            // Determinar el rol basado en la asignación
            var rol = dto.Rol ?? DeterminarRol(dto.TiendaId, dto.CajaId);

            const string sql = @"
                INSERT INTO ProveedorAsignaciones (ProveedorId, UsuarioId, TiendaId, CajaId, Rol, Activo)
                OUTPUT INSERTED.Id
                VALUES (@ProveedorId, @UsuarioId, @TiendaId, @CajaId, @Rol, 1)";

            var asignacionId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                ProveedorId = proveedorId,
                UsuarioId = usuarioId,
                dto.TiendaId,
                dto.CajaId,
                Rol = rol
            });

            return Ok(new
            {
                mensaje = "Asignación creada exitosamente.",
                asignacionId,
                tienda = tiendaNombre,
                caja = cajaNombre,
                rol
            });
        }

        /// <summary>
        /// DELETE /api/proveedores/{proveedorId}/asignaciones/{asignacionId}
        /// </summary>
        [HttpDelete("asignaciones/{asignacionId:int}")]
        public async Task<IActionResult> EliminarAsignacion(int proveedorId, int asignacionId)
        {
            using var connection = _connectionFactory.Create();

            var asignacion = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT Id, UsuarioId FROM ProveedorAsignaciones 
                WHERE Id = @Id AND ProveedorId = @ProveedorId",
                new { Id = asignacionId, ProveedorId = proveedorId });

            if (asignacion == null)
                return NotFound(new { message = "Asignación no encontrada." });

            // Verificar que el usuario tenga al menos otra asignación
            var totalAsignaciones = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(*) FROM ProveedorAsignaciones 
                WHERE UsuarioId = @UsuarioId AND ProveedorId = @ProveedorId",
                new { UsuarioId = (int)asignacion.UsuarioId, ProveedorId = proveedorId });

            if (totalAsignaciones <= 1)
                return BadRequest(new { message = "No se puede eliminar la última asignación del usuario. Desactive el usuario en su lugar." });

            await connection.ExecuteAsync(
                "DELETE FROM ProveedorAsignaciones WHERE Id = @Id",
                new { Id = asignacionId });

            return Ok(new { mensaje = "Asignación eliminada." });
        }

        #endregion

        #region ========== HELPERS ==========

        private static string GetNivelAcceso(int? tiendaId, int? cajaId, string rol)
        {
            if (rol == "admin" || (tiendaId == null && cajaId == null))
                return "Acceso total al proveedor";

            if (tiendaId != null && cajaId == null)
                return "Acceso a toda la tienda";

            return "Acceso solo a la caja";
        }

        private static string DeterminarRol(int? tiendaId, int? cajaId)
        {
            if (tiendaId == null && cajaId == null)
                return "admin";

            if (tiendaId != null && cajaId == null)
                return "supervisor";

            return "cajero";
        }

        #endregion
    }

    #region ========== DTOs ==========

    public class CrearUsuarioCajeroDto
    {
        public string Nombre { get; set; } = null!;
        public string Contrasena { get; set; } = null!;
        public int? TiendaId { get; set; }
        public int? CajaId { get; set; }
        public string? Rol { get; set; } // "cajero" | "supervisor" | "admin"
    }

    public class CrearAsignacionDto
    {
        public int? TiendaId { get; set; }
        public int? CajaId { get; set; }
        public string? Rol { get; set; }
    }

    #endregion
}