using Consumo_App.Models;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Consumo_App.Servicios
{
    public interface IJwtService
    {
        string GenerateToken(Usuario usuarios, IEnumerable<string> permisos);
        Task<bool> ValidateTokenAsync(string token);

        string GenerateTokenFromClaims(List<Claim> claims);
    }
}
