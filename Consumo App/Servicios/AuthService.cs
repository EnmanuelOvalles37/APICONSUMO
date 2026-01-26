using Dapper;
using Consumo_App.Data.Sql;
using Consumo_App.Models;

namespace Consumo_App.Servicios
{
    public interface IAuthService
    {
        Task<Usuario?> AuthenticateAsync(string usuario, string contrasena);
        Task<bool> HasPermissionAsync(int usuarioId, string permisoCodigo);
    }

    public class AuthService : IAuthService
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly IPasswordHasher _hasher;

        public AuthService(SqlConnectionFactory connectionFactory, IPasswordHasher hasher)
        {
            _connectionFactory = connectionFactory;
            _hasher = hasher;
        }

        public async Task<Usuario?> AuthenticateAsync(string usuario, string contrasena)
        {
            using var connection = _connectionFactory.Create();

            // Obtener usuario con su rol
            const string sqlUsuario = @"
                SELECT 
                    u.Id, u.Nombre, u.Contrasena, u.Activo, 
                    u.RolId, u.AccessFailedCount, u.LockoutEnd,
                    u.EmpresaId, u.ProveedorId,
                    r.Id, r.Nombre, r.Descripcion
                FROM Usuarios u
                INNER JOIN Roles r ON u.RolId = r.Id
                WHERE u.Nombre = @Nombre AND u.Activo = 1";

            var usuarioResult = await connection.QueryAsync<Usuario, Rol, Usuario>(
                sqlUsuario,
                (u, r) =>
                {
                    u.Rol = r;
                    return u;
                },
                new { Nombre = usuario },
                splitOn: "Id"
            );

            var u = usuarioResult.FirstOrDefault();
            if (u is null) return null;

            // ¿Está bloqueado?
            if (u.LockoutEnd.HasValue && u.LockoutEnd.Value > DateTime.UtcNow)
                return null;

            // Verificar contraseña
            if (!_hasher.Verify(contrasena, u.Contrasena))
            {
                u.AccessFailedCount += 1;

                if (u.AccessFailedCount >= 5)
                {
                    // Bloquear por 15 minutos
                    const string sqlBloquear = @"
                        UPDATE Usuarios 
                        SET AccessFailedCount = 0, 
                            LockoutEnd = @LockoutEnd 
                        WHERE Id = @Id";

                    await connection.ExecuteAsync(sqlBloquear, new
                    {
                        Id = u.Id,
                        LockoutEnd = DateTime.UtcNow.AddMinutes(15)
                    });
                }
                else
                {
                    // Incrementar contador de fallos
                    const string sqlFallo = @"
                        UPDATE Usuarios 
                        SET AccessFailedCount = @AccessFailedCount 
                        WHERE Id = @Id";

                    await connection.ExecuteAsync(sqlFallo, new
                    {
                        Id = u.Id,
                        AccessFailedCount = u.AccessFailedCount
                    });
                }

                return null;
            }

            // Éxito → reset conteo y cargar permisos
            const string sqlReset = @"
                UPDATE Usuarios 
                SET AccessFailedCount = 0, LockoutEnd = NULL 
                WHERE Id = @Id";

            await connection.ExecuteAsync(sqlReset, new { Id = u.Id });

            // Cargar permisos del rol
            u.Rol.RolPermisos = await CargarPermisosDelRolAsync(connection, u.RolId);

            return u;
        }

        public async Task<bool> HasPermissionAsync(int usuarioId, string permisoCodigo)
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM Usuarios u
                INNER JOIN Roles r ON u.RolId = r.Id
                INNER JOIN RolesPermisos rp ON r.Id = rp.RolId
                INNER JOIN Permisos p ON rp.PermisoId = p.Id
                WHERE u.Id = @UsuarioId 
                  AND u.Activo = 1 
                  AND p.Codigo = @PermisoCodigo";

            using var connection = _connectionFactory.Create();
            var count = await connection.ExecuteScalarAsync<int>(sql, new
            {
                UsuarioId = usuarioId,
                PermisoCodigo = permisoCodigo
            });

            return count > 0;
        }

        private async Task<List<RolPermiso>> CargarPermisosDelRolAsync(
            System.Data.IDbConnection connection,
            int rolId)
        {
            const string sql = @"
                SELECT 
                    rp.RolId, rp.PermisoId,
                    p.Id, p.Codigo, p.Nombre, p.Descripcion, p.Modulo
                FROM RolesPermisos rp
                INNER JOIN Permisos p ON rp.PermisoId = p.Id
                WHERE rp.RolId = @RolId";

            var result = await connection.QueryAsync<RolPermiso, Permiso, RolPermiso>(
                sql,
                (rp, p) =>
                {
                    rp.Permiso = p;
                    return rp;
                },
                new { RolId = rolId },
                splitOn: "Id"
            );

            return result.ToList();
        }
    }
}