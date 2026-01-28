using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Consumo_App.Seguridad
{
    public sealed class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
    {
        public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : base(options) { }

        public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            // Crea políticas al vuelo con el prefijo "perm:"
            if (policyName.StartsWith("perm:", StringComparison.OrdinalIgnoreCase))
            {
                var code = policyName.Substring("perm:".Length);
                var policy = new AuthorizationPolicyBuilder()
                    .AddRequirements(new PermissionRequirement(code))
                    .Build();

                return Task.FromResult<AuthorizationPolicy?>(policy);
            }
            return base.GetPolicyAsync(policyName);
        }
    }
}
