public interface ISeguridadService
{
    Task<List<string>> GetPermisosCodigoPorUsuarioAsync(int usuarioId);
    Task<List<int>> GetPermisoIdsPorRolAsync(int rolId);
    Task SetPermisosDeRolAsync(int rolId, IEnumerable<int> permisoIds);
}