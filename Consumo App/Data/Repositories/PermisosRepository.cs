using Dapper;
using Consumo_App.Data.Sql;

namespace Consumo_App.Data.Repositories
{
    public interface IPermisosRepository
    {
        Task<IReadOnlyList<string>> GetPermisosEfectivosAsync(int usuarioId);
        Task<IReadOnlyList<int>> GetPermisoIdsPorRolAsync(int rolId);
        Task<bool> UsuarioTienePermisoAsync(int usuarioId, string permisoCodigo);
    }

    public class PermisosRepository : IPermisosRepository
    {
        private readonly SqlConnectionFactory _connectionFactory;

        public PermisosRepository(SqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<IReadOnlyList<string>> GetPermisosEfectivosAsync(int usuarioId)
        {
            const string sql = @"
                SELECT DISTINCT p.Codigo
                FROM Usuarios u
                INNER JOIN RolesPermisos rp ON u.RolId = rp.RolId
                INNER JOIN Permisos p ON rp.PermisoId = p.Id
                WHERE u.Id = @UsuarioId AND u.Activo = 1";

            using var connection = _connectionFactory.Create();
            var result = await connection.QueryAsync<string>(sql, new { UsuarioId = usuarioId });
            return result.ToList();
        }

        public async Task<IReadOnlyList<int>> GetPermisoIdsPorRolAsync(int rolId)
        {
            const string sql = @"
                SELECT PermisoId 
                FROM RolesPermisos 
                WHERE RolId = @RolId";

            using var connection = _connectionFactory.Create();
            var result = await connection.QueryAsync<int>(sql, new { RolId = rolId });
            return result.ToList();
        }

        public async Task<bool> UsuarioTienePermisoAsync(int usuarioId, string permisoCodigo)
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM Usuarios u
                INNER JOIN RolesPermisos rp ON u.RolId = rp.RolId
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
    }
}