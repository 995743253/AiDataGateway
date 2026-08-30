using System.Security.Cryptography;
using System.Text;
using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.Logs;
using AiDataGateway.Infrastructure.Logs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

internal sealed class AgentLogServer : IAsyncDisposable
{
    private readonly WebApplication? _application;

    private AgentLogServer(WebApplication? application) => _application = application;

    public static async Task<AgentLogServer> StartAsync(AgentOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.LogPath) && string.IsNullOrWhiteSpace(options.NLogConfiguration))
        {
            Console.WriteLine("远程本地日志服务未启用；如需启用，请配置 --log-path 或 --nlog-config。");
            return new AgentLogServer(null);
        }

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls(options.ListenUrl);
        builder.Services.ConfigureHttpJsonOptions(value => value.SerializerOptions.PropertyNameCaseInsensitive = true);
        var app = builder.Build();
        var adapter = new LocalNLogSourceAdapter();
        var connection = new LogSourceConnection(LogSourceType.LocalNLog, options.LogPath, options.NLogConfiguration,
            options.NLogTargetName, options.NLogLayout, string.Empty);

        app.Use(async (context, next) =>
        {
            if (!SecureEquals(context.Request.Headers["X-Agent-Key"].ToString(), options.Secret))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            await next(context);
        });
        app.MapGet("/api/agent/logs/status", async (CancellationToken token) => Results.Ok(await adapter.TestAsync(connection, token)));
        app.MapPost("/api/agent/logs/query", async (LogQueryOptions query, CancellationToken token) => Results.Ok(await adapter.QueryAsync(connection, query, token)));
        await app.StartAsync(cancellationToken);
        Console.WriteLine($"远程本地日志服务已启动：{options.ListenUrl}");
        return new AgentLogServer(app);
    }

    public async ValueTask DisposeAsync()
    {
        if (_application is null) return;
        await _application.StopAsync();
        await _application.DisposeAsync();
    }

    private static bool SecureEquals(string actual, string expected)
    {
        var left = SHA256.HashData(Encoding.UTF8.GetBytes(actual));
        var right = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(left, right);
    }
}
