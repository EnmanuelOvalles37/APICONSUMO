using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace Consumo_App.Seguridad
{
    public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
    {
        
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PermissionRequirement requirement)
        {
            // Busca el claim "permiso" con el código exacto
            if (context.User.HasClaim("permiso", requirement.Code))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
