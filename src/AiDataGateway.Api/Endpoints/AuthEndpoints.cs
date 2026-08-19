using System.Security.Claims;
using AiDataGateway.Api.Contracts;
using AiDataGateway.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace AiDataGateway.Api.Endpoints;

internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/login", async (LoginRequest request, SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager) =>
        {
            var user = await userManager.FindByNameAsync(request.UserName);
            if (user is null || !user.IsEnabled)
            {
                return Results.Unauthorized();
            }

            var result = await signInManager.PasswordSignInAsync(user, request.Password, request.RememberMe, lockoutOnFailure: true);
            if (!result.Succeeded)
            {
                return result.IsLockedOut
                    ? Results.Problem("The user is temporarily locked.", statusCode: StatusCodes.Status423Locked)
                    : Results.Unauthorized();
            }

            user.LastLoginAtUtc = DateTimeOffset.UtcNow;
            await userManager.UpdateAsync(user);
            return Results.Ok(await ToUserView(user, userManager));
        });

        endpoints.MapPost("/api/auth/logout", [Authorize] async (SignInManager<ApplicationUser> signInManager) =>
        {
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
