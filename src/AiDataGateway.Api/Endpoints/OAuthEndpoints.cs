using System.Security.Claims;
using AiDataGateway.Infrastructure.Identity;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace AiDataGateway.Api.Endpoints;

internal static class OAuthEndpoints
{
    public static IEndpointRouteBuilder MapOAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods("/connect/authorize", [HttpMethods.Get, HttpMethods.Post], HandleAuthorizationAsync);
        endpoints.MapPost("/connect/token", HandleTokenAsync);
        endpoints.MapMethods("/connect/logout", [HttpMethods.Get, HttpMethods.Post], HandleLogoutAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleAuthorizationAsync(HttpContext context, UserManager<ApplicationUser> userManager)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be resolved.");
        var cookie = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!cookie.Succeeded)
        {
            return Results.Challenge(new AuthenticationProperties
            {
                RedirectUri = context.Request.PathBase + context.Request.Path + context.Request.QueryString
            }, [IdentityConstants.ApplicationScheme]);
        }

        var user = await userManager.GetUserAsync(cookie.Principal!);
        if (user is null || !user.IsEnabled)
        {
            return Results.Forbid();
        }

        var principal = await CreateUserPrincipalAsync(user, userManager, request.GetScopes());
        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> HandleTokenAsync(HttpContext context, UserManager<ApplicationUser> userManager)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be resolved.");

        if (request.IsClientCredentialsGrantType())
        {
            var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, OpenIddictConstants.Claims.Name, OpenIddictConstants.Claims.Role);
            identity.SetClaim(OpenIddictConstants.Claims.Subject, request.ClientId!);
            identity.SetClaim(OpenIddictConstants.Claims.Name, request.ClientId!);
            var principal = new ClaimsPrincipal(identity);
            principal.SetScopes(request.GetScopes());
            principal.SetDestinations(static claim => [OpenIddictConstants.Destinations.AccessToken]);
            return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var subject = result.Principal?.GetClaim(OpenIddictConstants.Claims.Subject);
            var user = subject is null ? null : await userManager.FindByIdAsync(subject);
            if (user is null || !user.IsEnabled)
            {
                return Results.Forbid(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
            }

            var principal = await CreateUserPrincipalAsync(user, userManager, request.GetScopes());
            return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        return Results.BadRequest(new { error = OpenIddictConstants.Errors.UnsupportedGrantType });
    }

    private static async Task<IResult> HandleLogoutAsync(HttpContext context, SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return Results.SignOut(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    private static async Task<ClaimsPrincipal> CreateUserPrincipalAsync(ApplicationUser user, UserManager<ApplicationUser> userManager, IEnumerable<string> scopes)
    {
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, OpenIddictConstants.Claims.Name, OpenIddictConstants.Claims.Role);
        identity.SetClaim(OpenIddictConstants.Claims.Subject, user.Id.ToString());
        identity.SetClaim(OpenIddictConstants.Claims.Name, user.UserName ?? user.DisplayName);
        identity.SetClaim(OpenIddictConstants.Claims.Email, user.Email);
        foreach (var role in await userManager.GetRolesAsync(user))
        {
            identity.AddClaim(new Claim(OpenIddictConstants.Claims.Role, role));
        }

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(scopes);
        principal.SetDestinations(static claim => claim.Type switch
        {
            OpenIddictConstants.Claims.Name or OpenIddictConstants.Claims.Email or OpenIddictConstants.Claims.Role =>
                [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken],
            _ => [OpenIddictConstants.Destinations.AccessToken]
        });
        return principal;
    }
}
