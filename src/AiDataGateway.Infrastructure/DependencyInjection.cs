using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.DataSources;
using AiDataGateway.Infrastructure.Databases;
using AiDataGateway.Infrastructure.Identity;
using AiDataGateway.Infrastructure.Persistence;
using AiDataGateway.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AiDataGateway.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, Action<GatewayStorageOptions>? configure = null)
    {
        var storage = new GatewayStorageOptions();
        configure?.Invoke(storage);
        Directory.CreateDirectory(storage.BasePath);
        Directory.CreateDirectory(Path.Combine(storage.BasePath, "keys"));
        Directory.CreateDirectory(Path.Combine(storage.BasePath, "logs"));

        services.AddSingleton(Options.Create(storage));
        var dataProtection = services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(storage.BasePath, "keys")))
            .SetApplicationName("AiDataGateway");
        if (storage.ProtectKeysWithDpapi && OperatingSystem.IsWindows())
        {
            dataProtection.ProtectKeysWithDpapi();
        }

        var databasePath = Path.Combine(storage.BasePath, "gateway.db");
        services.AddDbContext<GatewayDbContext>(options =>
        {
            options.UseSqlite($"Data Source={databasePath}");
            options.UseOpenIddict();
        });

        services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<GatewayDbContext>()
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "AiDataGateway.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.Events.OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
        });

        services.AddOpenIddict()
            .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<GatewayDbContext>());

        services.AddScoped<IDataSourceRepository, DataSourceRepository>();
        services.AddScoped<IChangeRequestRepository, ChangeRequestRepository>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddSingleton<ICredentialProtector, DataProtectionCredentialProtector>();
        services.AddSingleton<IDatabaseAdapterFactory, DatabaseAdapterFactory>();
        services.AddSingleton<IDatabaseAdapter>(new FreeSqlDatabaseAdapter(DatabaseProvider.SqlServer));
        services.AddSingleton<IDatabaseAdapter>(new FreeSqlDatabaseAdapter(DatabaseProvider.MySql));
        services.AddSingleton<IDatabaseAdapter>(new FreeSqlDatabaseAdapter(DatabaseProvider.PostgreSql));
        services.AddSingleton<IDatabaseAdapter>(new FreeSqlDatabaseAdapter(DatabaseProvider.Sqlite));
        services.AddScoped<GatewayDatabaseInitializer>();
        return services;
    }
}
