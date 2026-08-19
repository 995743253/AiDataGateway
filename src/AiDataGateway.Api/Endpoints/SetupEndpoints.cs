using System.Security.Cryptography;
using AiDataGateway.Api.Contracts;
using AiDataGateway.Api.Security;
using AiDataGateway.Application.Security;
using AiDataGateway.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace AiDataGateway.Api.Endpoints;

internal static class SetupEndpoints
{
    public static IEndpointRouteBuilder MapSetupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/setup/status", async (UserManager<ApplicationUser> userManager) =>
            Results.Ok(new { needsSetup = !await userManager.Users.AnyAsync() }));

        endpoints.MapPost("/api/setup", async (
            SetupRequest request,
            UserManager<ApplicationUser> userManager,
            IOpenIddictApplicationManager applicationManager) =>
        {
            if (await userManager.Users.AnyAsync())
            {
                return Results.Conflict(new { message = "Initial setup has already been completed." });
            }

            var requiredErrors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(request.UserName))
            {
                requiredErrors[nameof(request.UserName)] = ["请输入用户名。"];
            }
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                requiredErrors[nameof(request.Email)] = ["请输入邮箱地址。"];
            }
            if (string.IsNullOrWhiteSpace(request.DisplayName))
            {
                requiredErrors[nameof(request.DisplayName)] = ["请输入显示名称。"];
            }
            if (string.IsNullOrWhiteSpace(request.Password))
            {
                requiredErrors[nameof(request.Password)] = ["请输入管理员密码。"];
            }
            if (string.IsNullOrWhiteSpace(request.AiClientName))
            {
                requiredErrors[nameof(request.AiClientName)] = ["请输入 AI 客户端名称。"];
            }
            if (requiredErrors.Count > 0)
            {
                return Results.BadRequest(new
                {
                    message = string.Join("；", requiredErrors.Values.SelectMany(item => item)),
                    errors = requiredErrors
                });
            }

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = request.UserName.Trim(),
                Email = request.Email.Trim(),
                DisplayName = request.DisplayName.Trim(),
                EmailConfirmed = true,
                IsEnabled = true
            };

            var createResult = await userManager.CreateAsync(user, request.Password);
            if (!createResult.Succeeded)
            {
                return IdentityErrorResponse.BadRequest(createResult);
            }

            await userManager.AddToRoleAsync(user, GatewayRoles.Administrator);

            var clientId = $"local-ai-{Guid.NewGuid():N}";
            var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            await applicationManager.CreateAsync(OAuthDescriptorFactory.CreateAiClient(clientId, request.AiClientName, secret));

            return Results.Ok(new
            {
                message = "Setup completed. Save the client secret now; it will not be shown again.",
                clientId,
                clientSecret = secret,
                tokenEndpoint = "/connect/token",
                scopes = GatewayScopes.AiClientDefaults
            });
        });

        return endpoints;
    }
}
