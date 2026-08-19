using System.Security.Claims;
using AiDataGateway.Api.Contracts;
using AiDataGateway.Infrastructure.Identity;
using AiDataGateway.Application.Abstractions;
using AiDataGateway.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace AiDataGateway.Api.Endpoints;

internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/login", async (LoginRequest request, SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager, IAuditWriter auditWriter) =>
        {
            var user = await userManager.FindByNameAsync(request.UserName);
            if (user is null || !user.IsEnabled)
            {
                await auditWriter.WriteAsync(request.UserName, "auth.login", "failure", detail: user is null ? "user-not-found" : "user-disabled");
                return Results.Unauthorized();
            }

            var result = await signInManager.PasswordSignInAsync(user, request.Password, request.RememberMe, lockoutOnFailure: true);
            if (!result.Succeeded)
            {
                await auditWriter.WriteAsync(request.UserName, "auth.login", "failure", detail: result.IsLockedOut ? "locked-out" : "invalid-credentials");
                return result.IsLockedOut
                    ? Results.Problem("The user is temporarily locked.", statusCode: StatusCodes.Status423Locked)
                    : Results.Unauthorized();
            }

            user.LastLoginAtUtc = DateTimeOffset.UtcNow;
            await userManager.UpdateAsync(user);
            await auditWriter.WriteAsync(user.UserName ?? user.Id.ToString(), "auth.login", "success");
            return Results.Ok(await ToUserView(user, userManager));
        });

        endpoints.MapPost("/api/auth/logout", [Authorize] async (HttpContext context, SignInManager<ApplicationUser> signInManager, IAuditWriter auditWriter) =>
        {
            await auditWriter.WriteAsync(GatewayPrincipal.Actor(context.User), "auth.logout", "success");
            await signInManager.SignOutAsync();
            return Results.NoContent();
        });

        endpoints.MapGet("/api/auth/me", [Authorize] async (ClaimsPrincipal principal, UserManager<ApplicationUser> userManager) =>
        {
            var user = await userManager.GetUserAsync(principal);
            return user is null ? Results.Unauthorized() : Results.Ok(await ToUserView(user, userManager));
        });

        return endpoints;
    }

    private static async Task<object> ToUserView(ApplicationUser user, UserManager<ApplicationUser> userManager) => new
    {
        user.Id,
        user.UserName,
        user.Email,
        user.DisplayName,
        user.IsEnabled,
        roles = await userManager.GetRolesAsync(user)
    };
}
