// Seguridad/PermissionRequirement.cs
using Microsoft.AspNetCore.Authorization;

namespace Consumo_App.Seguridad
{
    public sealed record PermissionRequirement(string Code) : IAuthorizationRequirement;
}

