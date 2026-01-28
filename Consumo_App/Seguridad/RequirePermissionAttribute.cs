using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using Consumo_App.Servicios;
using System.Security.Claims;

namespace Consumo_App.Seguridad
{
    // Seguridad/RequirePermissionAttribute.cs
    public class RequirePermissionAttribute : TypeFilterAttribute
    {
        public RequirePermissionAttribute(string permiso) : base(typeof(RequirePermissionFilter))
        { Arguments = new object[] { permiso }; }

        private class RequirePermissionFilter : IAsyncActionFilter
        {
            private readonly IAuthService _auth;
            private readonly ISeguridadService _seguridad;
            private readonly string _permiso;

            public RequirePermissionFilter(IAuthService auth,ISeguridadService seguridad, string permiso)
            { _seguridad = seguridad;
              _permiso = permiso;
                _auth = auth;
            }

            public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
            {
                // Recupera userId desde token/jwt
                var claim = ctx.HttpContext.User.FindFirst("uid")?.Value
                ?? ctx.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (string.IsNullOrWhiteSpace("uid") || !int.TryParse("uid", out var userId))
                {
                    ctx.Result = new UnauthorizedResult();
                    return;
                }

                // 2) Verificar permiso por CÓDIGO
                var ok = await _auth.HasPermissionAsync(userId, _permiso);
                if (!ok)
                {
                    ctx.Result = new ForbidResult();
                    return;
                }

                await next();
            }
        }
    }

}
