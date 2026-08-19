using AiDataGateway.Api.Endpoints;
using AiDataGateway.Application;
using AiDataGateway.Application.Security;
using AiDataGateway.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.FileProviders;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;

namespace AiDataGateway.Api;

public sealed class GatewayWebHost : IAsyncDisposable
{
    private readonly WebApplication _application;

    private GatewayWebHost(WebApplication application, Uri baseAddress)
    {
        _application = application;
        BaseAddress = baseAddress;
    }

    public Uri BaseAddress { get; }

    public static async Task<GatewayWebHost> StartAsync(GatewayHostOptions options, CancellationToken cancellationToken = default)
    {
        var webRoot = options.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
        Directory.CreateDirectory(webRoot);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(GatewayWebHost).Assembly.FullName,
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = webRoot
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddDebug();

        var baseAddress = new Uri($"http://127.0.0.1:{options.Port}/");
        builder.WebHost.UseUrls(baseAddress.ToString());
        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(storage =>
        {
            storage.BasePath = options.StoragePath;
            storage.ProtectKeysWithDpapi = !options.UseEphemeralCertificates;
        });
        builder.Services.AddAuthorization();
        builder.Services.AddAuthentication(authentication =>
            {
                authentication.DefaultAuthenticateScheme = "Gateway";
                authentication.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
            })
            .AddPolicyScheme("Gateway", "Cookie or OAuth bearer", policy =>
            {
                policy.ForwardDefaultSelector = context =>
                    context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                        ? OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme
                        : IdentityConstants.ApplicationScheme;
            });

        builder.Services.AddOpenIddict()
            .AddServer(server =>
            {
                server.SetIssuer(baseAddress);
                server.SetAuthorizationEndpointUris("/connect/authorize");
                server.SetTokenEndpointUris("/connect/token");
                server.SetEndSessionEndpointUris("/connect/logout");
                server.SetRevocationEndpointUris("/connect/revocation");
                server.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange();
                server.AllowClientCredentialsFlow();
                server.AllowRefreshTokenFlow();
                server.RegisterScopes(
                    OpenIddictConstants.Scopes.OpenId,
                    OpenIddictConstants.Scopes.Profile,
                    OpenIddictConstants.Scopes.Email,
                    OpenIddictConstants.Scopes.OfflineAccess,
                    GatewayScopes.DataSourceRead,
                    GatewayScopes.QueryExecute,
                    GatewayScopes.ChangeSubmit,
                    GatewayScopes.ChangeApprove,
                    GatewayScopes.AuditRead,
                    GatewayScopes.Admin);
                if (options.UseEphemeralCertificates)
                {
                    server.AddEphemeralEncryptionKey();
                    server.AddEphemeralSigningKey();
                }
                else
                {
                    server.AddDevelopmentEncryptionCertificate();
                    server.AddDevelopmentSigningCertificate();
                }
                server.DisableAccessTokenEncryption();
                server.UseAspNetCore()
                    .DisableTransportSecurityRequirement()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableStatusCodePagesIntegration();
            })
            .AddValidation(validation =>
            {
                validation.UseLocalServer();
                validation.UseAspNetCore();
            });

        var app = builder.Build();
        app.UseExceptionHandler(exceptionHandler => exceptionHandler.Run(async context =>
        {
            var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
            context.Response.StatusCode = feature?.Error is KeyNotFoundException ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { message = feature?.Error.Message ?? "Unexpected error." });
        }));
        app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(webRoot) });
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/api/health", () => Results.Ok(new { status = "ok", version = typeof(GatewayWebHost).Assembly.GetName().Version?.ToString() }));
        app.MapSetupEndpoints();
        app.MapAuthEndpoints();
        app.MapOAuthEndpoints();
        app.MapAdminEndpoints();
        app.MapGatewayEndpoints();
        app.MapFallback(async context =>
        {
            var index = Path.Combine(webRoot, "index.html");
            if (File.Exists(index))
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.SendFileAsync(index);
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync("<html><body><h2>AiDataGateway is running</h2><p>Build the Vue application to enable the management UI.</p></body></html>");
        });

        await app.StartAsync(cancellationToken);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<GatewayDatabaseInitializer>().InitializeAsync(cancellationToken);
        }

        return new GatewayWebHost(app, baseAddress);
    }

    public async ValueTask DisposeAsync()
    {
        await _application.StopAsync();
        await _application.DisposeAsync();
    }
}
