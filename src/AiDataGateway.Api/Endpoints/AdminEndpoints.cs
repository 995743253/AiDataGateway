using System.Security.Cryptography;
using AiDataGateway.Api.Contracts;
using AiDataGateway.Api.Security;
using AiDataGateway.Application.DataSources;
using AiDataGateway.Application.Abstractions;
using AiDataGateway.Application.Security;
using AiDataGateway.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace AiDataGateway.Api.Endpoints;

internal static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin").RequireAuthorization(new AuthorizeAttribute { Roles = GatewayRoles.Administrator });

        admin.MapGet("/users", async (UserManager<ApplicationUser> userManager) =>
        {
            var users = await userManager.Users.OrderBy(item => item.UserName).ToListAsync();
            var result = new List<object>();
            foreach (var user in users)
            {
                result.Add(new
                {
                    user.Id,
                    user.UserName,
                    user.Email,
                    user.DisplayName,
                    user.IsEnabled,
                    user.LockoutEnd,
                    roles = await userManager.GetRolesAsync(user)
                });
            }

            return Results.Ok(result);
        });

        admin.MapPost("/users", async (CreateUserRequest request, HttpContext context, UserManager<ApplicationUser> userManager, IAuditWriter auditWriter) =>
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = request.UserName.Trim(),
                Email = request.Email.Trim(),
                DisplayName = request.DisplayName.Trim(),
                EmailConfirmed = true,
                IsEnabled = true
            };
            var result = await userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                return IdentityErrorResponse.BadRequest(result);
            }

            var roles = request.Roles.Intersect(GatewayRoles.All, StringComparer.OrdinalIgnoreCase).ToArray();
            if (roles.Length > 0)
            {
                await userManager.AddToRolesAsync(user, roles);
            }
            await auditWriter.WriteAsync(GatewayPrincipal.Actor(context.User), "user.create", "success", detail: user.UserName);
            return Results.Created($"/api/admin/users/{user.Id}", new { user.Id });
        });

        admin.MapPut("/users/{id:guid}", async (Guid id, UpdateUserRequest request, HttpContext context, UserManager<ApplicationUser> userManager, IAuditWriter auditWriter) =>
        {
            var user = await userManager.FindByIdAsync(id.ToString());
            if (user is null)
            {
                return Results.NotFound();
            }

            var currentRoles = await userManager.GetRolesAsync(user);
            var requestedRoles = request.Roles.Intersect(GatewayRoles.All, StringComparer.OrdinalIgnoreCase).ToArray();
            var rolesChanged = !currentRoles.ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(requestedRoles);
            var isAdministrator = currentRoles.Contains(GatewayRoles.Administrator, StringComparer.OrdinalIgnoreCase);
            var remainsAdministrator = requestedRoles.Contains(GatewayRoles.Administrator, StringComparer.OrdinalIgnoreCase);
            var currentUserId = userManager.GetUserId(context.User);
            if (string.Equals(currentUserId, id.ToString(), StringComparison.OrdinalIgnoreCase) && !request.Enabled)
            {
                return Results.BadRequest(new { message = "不能禁用当前登录账号。" });
            }
            if (isAdministrator && (!request.Enabled || !remainsAdministrator) && !await HasOtherEnabledAdministratorAsync(id, userManager))
            {
                return Results.BadRequest(new { message = "不能禁用最后一个管理员或移除其管理员角色。" });
            }

            user.DisplayName = request.DisplayName.Trim();
            user.IsEnabled = request.Enabled;
            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded) return IdentityErrorResponse.BadRequest(updateResult);
            var removeResult = await userManager.RemoveFromRolesAsync(user, currentRoles);
            if (!removeResult.Succeeded) return IdentityErrorResponse.BadRequest(removeResult);
            if (requestedRoles.Length > 0)
            {
                var addResult = await userManager.AddToRolesAsync(user, requestedRoles);
                if (!addResult.Succeeded) return IdentityErrorResponse.BadRequest(addResult);
            }
            if (!request.Enabled || rolesChanged)
            {
                await userManager.UpdateSecurityStampAsync(user);
            }
            await auditWriter.WriteAsync(GatewayPrincipal.Actor(context.User), "user.update", "success", detail: user.UserName);
            return Results.NoContent();
        });

        admin.MapDelete("/users/{id:guid}", async (
            Guid id,
            HttpContext context,
            UserManager<ApplicationUser> userManager,
            IUserHistoryChecker historyChecker,
            IAuditWriter auditWriter,
            CancellationToken cancellationToken) =>
        {
            var user = await userManager.FindByIdAsync(id.ToString());
            if (user is null) return Results.NotFound();
            if (string.Equals(userManager.GetUserId(context.User), id.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { message = "不能删除当前登录账号。" });
            }

            var roles = await userManager.GetRolesAsync(user);
            if (roles.Contains(GatewayRoles.Administrator, StringComparer.OrdinalIgnoreCase) && !await HasOtherEnabledAdministratorAsync(id, userManager))
            {
                return Results.BadRequest(new { message = "不能删除最后一个管理员。" });
            }
            if (await historyChecker.HasHistoryAsync(user.UserName ?? id.ToString(), cancellationToken))
            {
                return Results.Conflict(new { message = "该用户已有审批或审计历史，不能永久删除；请改为禁用账号。" });
            }

            var result = await userManager.DeleteAsync(user);
            if (!result.Succeeded) return IdentityErrorResponse.BadRequest(result);
            await auditWriter.WriteAsync(GatewayPrincipal.Actor(context.User), "user.delete", "success", detail: user.UserName, cancellationToken: cancellationToken);
            return Results.NoContent();
        });

        admin.MapGet("/roles", () => Results.Ok(GatewayRoles.All));

        admin.MapGet("/oauth-clients", async (IOpenIddictApplicationManager manager) =>
        {
            var clients = new List<object>();
            await foreach (var application in manager.ListAsync())
            {
                clients.Add(new
                {
                    clientId = await manager.GetClientIdAsync(application),
                    displayName = await manager.GetDisplayNameAsync(application),
                    permissions = await manager.GetPermissionsAsync(application)
                });
            }
            return Results.Ok(clients);
        });

        admin.MapPost("/oauth-clients", async (CreateOAuthClientRequest request, HttpContext context, IOpenIddictApplicationManager manager, IAuditWriter auditWriter) =>
        {
            var clientId = $"local-ai-{Guid.NewGuid():N}";
            var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var allowedScopes = (request.Scopes ?? GatewayScopes.AiClientDefaults)
                .Intersect(GatewayScopes.AiClientDefaults, StringComparer.Ordinal)
                .ToArray();
            await manager.CreateAsync(OAuthDescriptorFactory.CreateAiClient(clientId, request.DisplayName, secret, allowedScopes));
            await auditWriter.WriteAsync(GatewayPrincipal.Actor(context.User), "oauth-client.create", "success", detail: clientId);
            return Results.Ok(new { clientId, clientSecret = secret, scopes = allowedScopes });
        });

        admin.MapDelete("/oauth-clients/{clientId}", async (
            string clientId,
            HttpContext context,
            IOpenIddictApplicationManager applicationManager,
            IOpenIddictAuthorizationManager authorizationManager,
            IOpenIddictTokenManager tokenManager,
            IAuditWriter auditWriter,
            CancellationToken cancellationToken) =>
        {
            var application = await applicationManager.FindByClientIdAsync(clientId, cancellationToken);
            if (application is null) return Results.NotFound();
            var applicationId = await applicationManager.GetIdAsync(application, cancellationToken);
            if (string.IsNullOrWhiteSpace(applicationId)) return Results.Problem("OAuth2 client has no application identifier.");

            await tokenManager.RevokeByApplicationIdAsync(applicationId, cancellationToken);
            await authorizationManager.RevokeByApplicationIdAsync(applicationId, cancellationToken);

            var tokens = new List<object>();
            await foreach (var token in tokenManager.FindByApplicationIdAsync(applicationId, cancellationToken)) tokens.Add(token);
            foreach (var token in tokens) await tokenManager.DeleteAsync(token, cancellationToken);

            var authorizations = new List<object>();
            await foreach (var authorization in authorizationManager.FindByApplicationIdAsync(applicationId, cancellationToken)) authorizations.Add(authorization);
            foreach (var authorization in authorizations) await authorizationManager.DeleteAsync(authorization, cancellationToken);

            await applicationManager.DeleteAsync(application, cancellationToken);
            await auditWriter.WriteAsync(GatewayPrincipal.Actor(context.User), "oauth-client.delete", "success", detail: clientId, cancellationToken: cancellationToken);
            return Results.NoContent();
        });

        var dataSources = endpoints.MapGroup("/api/admin/datasources")
            .RequireAuthorization(new AuthorizeAttribute { Roles = $"{GatewayRoles.Administrator},{GatewayRoles.Operator}" });
        dataSources.MapGet("/", async (DataSourceService service, CancellationToken cancellationToken) => Results.Ok(await service.ListAsync(cancellationToken)));
        dataSources.MapPost("/", async (DataSourceUpsertRequest request, HttpContext context, DataSourceService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.CreateAsync(request, GatewayPrincipal.Actor(context.User), cancellationToken)));
        dataSources.MapPut("/{id:guid}", async (Guid id, DataSourceUpsertRequest request, HttpContext context, DataSourceService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateAsync(id, request, GatewayPrincipal.Actor(context.User), cancellationToken)));
        dataSources.MapDelete("/{id:guid}", async (Guid id, HttpContext context, DataSourceService service, CancellationToken cancellationToken) =>
        {
            await service.DeleteAsync(id, GatewayPrincipal.Actor(context.User), cancellationToken);
            return Results.NoContent();
        });
        dataSources.MapPost("/{id:guid}/test", async (Guid id, HttpContext context, DataSourceService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.TestAsync(id, GatewayPrincipal.Actor(context.User), cancellationToken)));

        return endpoints;
    }

    private static async Task<bool> HasOtherEnabledAdministratorAsync(Guid userId, UserManager<ApplicationUser> userManager)
    {
        var administrators = await userManager.GetUsersInRoleAsync(GatewayRoles.Administrator);
        return administrators.Any(item => item.Id != userId && item.IsEnabled);
    }
}
