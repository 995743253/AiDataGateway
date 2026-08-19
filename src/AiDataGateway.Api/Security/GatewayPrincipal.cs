using System.Security.Claims;
using AiDataGateway.Application.Security;
using OpenIddict.Abstractions;

namespace AiDataGateway.Api.Security;

internal static class GatewayPrincipal
{
    public static string Actor(ClaimsPrincipal principal) =>
        principal.FindFirstValue(OpenIddictConstants.Claims.Name)
        ?? principal.FindFirstValue(ClaimTypes.Name)
        ?? principal.FindFirstValue(OpenIddictConstants.Claims.ClientId)
        ?? principal.Identity?.Name
        ?? "unknown";

    public static bool Can(ClaimsPrincipal principal, string scope, params string[] roles) =>
        principal.HasScope(scope)
        || roles.Any(principal.IsInRole)
        || principal.IsInRole(GatewayRoles.Administrator);
}
