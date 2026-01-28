// Servicios/UserContext.cs
using System.Security.Claims;

namespace Consumo_App.Servicios
{
    public interface IUserContext
    {
        int Id { get; }
        string? Nombre { get; }
        int RolId { get; }
        string? Rol { get; }
        IEnumerable<string> Roles { get; }
        IEnumerable<string> Permisos { get; }
    }

    public class UserContext : IUserContext
    {
        private readonly IHttpContextAccessor _http;

        public UserContext(IHttpContextAccessor http) => _http = http;

        private ClaimsPrincipal? Principal => _http.HttpContext?.User;

        public int Id
        {
            get
            {
                var uid = Principal?.FindFirst("uid")?.Value
                       ?? Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                return int.TryParse(uid, out var id) ? id : 0;
            }
        }

        public string? Nombre => Principal?.FindFirst(ClaimTypes.Name)?.Value;

        public int RolId
        {
            get
            {
                var rolId = Principal?.FindFirst("rolId")?.Value;
                return int.TryParse(rolId, out var id) ? id : 0;
            }
        }

        public string? Rol => Principal?.FindFirst(ClaimTypes.Role)?.Value
                           ?? Roles.FirstOrDefault();

        public IEnumerable<string> Roles =>
            Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value) ?? Enumerable.Empty<string>();

        public IEnumerable<string> Permisos =>
            Principal?.FindAll("perm").Select(c => c.Value) ?? Enumerable.Empty<string>();
    }
}