using Dapper;
using Consumo_App.Data.Sql;
using Consumo_App.Models;

namespace Consumo_App.Servicios
{
    /*public interface ISeguridadService
    {
        Task<List<string>> GetPermisosCodigoPorUsuarioAsync(int usuarioId);
        Task<List<int>> GetPermisoIdsPorRolAsync(int rolId);
        Task SetPermisosDeRolAsync(int rolId, IEnumerable<int> permisoIds);
    }*/

    public class SeguridadService : ISeguridadService
    {
        private readonly SqlConnectionFactory _connectionFactory;

        public SeguridadService(SqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<List<string>> GetPermisosCodigoPorUsuarioAsync(int usuarioId)
        {
            const string sql = @"
                SELECT DISTINCT p.Codigo
                FROM Usuarios u
                INNER JOIN Roles r ON u.RolId = r.Id
                INNER JOIN RolesPermisos rp ON r.Id = rp.RolId
                INNER JOIN Permisos p ON rp.PermisoId = p.Id
                WHERE u.Id = @UsuarioId AND u.Activo = 1";

            using var connection = _connectionFactory.Create();
            var result = await connection.QueryAsync<string>(sql, new { UsuarioId = usuarioId });
            return result.ToList();
        }

        public async Task<List<int>> GetPermisoIdsPorRolAsync(int rolId)
        {
            const string sql = @"
                SELECT PermisoId 
                FROM RolesPermisos 
                WHERE RolId = @RolId";

            using var connection = _connectionFactory.Create();
            var result = await connection.QueryAsync<int>(sql, new { RolId = rolId });
            return result.ToList();
        }

        public async Task SetPermisosDeRolAsync(int rolId, IEnumerable<int> permisoIds)
        {
            using var connection = _connectionFactory.Create();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                // Eliminar permisos actuales del rol
                const string deleteSql = "DELETE FROM RolesPermisos WHERE RolId = @RolId";
                await connection.ExecuteAsync(deleteSql, new { RolId = rolId }, transaction);

                // Insertar nuevos permisos (evitando duplicados con Distinct)
                var permisosUnicos = permisoIds.Distinct().ToList();

                if (permisosUnicos.Count > 0)
                {
                    const string insertSql = @"
                        INSERT INTO RolesPermisos (RolId, PermisoId) 
                        VALUES (@RolId, @PermisoId)";

                    var parametros = permisosUnicos.Select(pid => new
                    {
                        RolId = rolId,
                        PermisoId = pid
                    });

                    await connection.ExecuteAsync(insertSql, parametros, transaction);
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }
}