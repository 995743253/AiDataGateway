using System.Net.Http.Json;
using AiDataGateway.Monitoring;

var options = AgentOptions.Read(args);
if (!options.IsValid)
{
    Console.Error.WriteLine("缺少配置。请使用 --gateway http://网关IP:端口 --target 节点标识 --secret 上报密钥 [--interval 10]");
    Console.Error.WriteLine("远程日志：追加 --log-path D:\\Logs\\App [--nlog-config D:\\App\\NLog.config] [--listen http://0.0.0.0:5188]");
    Console.Error.WriteLine("也可设置 GATEWAY_URL、MONITOR_TARGET、MONITOR_SECRET、MONITOR_INTERVAL_SECONDS 环境变量。");
    return 2;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

using var client = new HttpClient { BaseAddress = new Uri(options.GatewayUrl), Timeout = TimeSpan.FromSeconds(15) };
await using var logServer = await AgentLogServer.StartAsync(options, cancellation.Token);
var collector = new SystemMetricsCollector();
var metricKeys = await LoadMetricKeysAsync(client, options, cancellation.Token);
collector.Collect(metricKeys);
Console.WriteLine($"AiDataGateway Monitor Agent 已启动：{Environment.MachineName} -> {client.BaseAddress}（{options.TargetKey}，每 {options.IntervalSeconds} 秒）");

try
{
    await Task.Delay(TimeSpan.FromSeconds(1), cancellation.Token);
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.IntervalSeconds));
    var samplesSinceConfigurationRefresh = 0;
    do
    {
        if (samplesSinceConfigurationRefresh++ >= Math.Max(1, 300 / options.IntervalSeconds))
        {
            metricKeys = await LoadMetricKeysAsync(client, options, cancellation.Token) ?? metricKeys;
            samplesSinceConfigurationRefresh = 0;
        }
        var sample = collector.Collect(metricKeys);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/monitoring/ingest/{Uri.EscapeDataString(options.TargetKey)}")
        {
            Content = JsonContent.Create(sample)
        };
        request.Headers.Add("X-Monitor-Key", options.Secret);
        try
        {
            using var response = await client.SendAsync(request, cancellation.Token);
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} 上报失败：HTTP {(int)response.StatusCode}");
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            break;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} 上报异常：{exception.Message}");
        }
    } while (await timer.WaitForNextTickAsync(cancellation.Token));
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    // Normal Ctrl+C shutdown.
}

return 0;

static async Task<IReadOnlyList<string>?> LoadMetricKeysAsync(HttpClient client, AgentOptions options, CancellationToken cancellationToken)
{
    try
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/monitoring/ingest/{Uri.EscapeDataString(options.TargetKey)}/configuration");
        request.Headers.Add("X-Monitor-Key", options.Secret);
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        var configuration = await response.Content.ReadFromJsonAsync<AgentMetricConfiguration>(cancellationToken: cancellationToken);
        return configuration?.MetricKeys;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
    catch { return null; }
}

internal sealed record AgentMetricConfiguration(string TargetKey, string[] MetricKeys);

internal sealed record AgentOptions(
    string GatewayUrl,
    string TargetKey,
    string Secret,
    int IntervalSeconds,
    string ListenUrl,
    string LogPath,
    string NLogConfiguration,
    string NLogTargetName,
    string NLogLayout)
{
    public bool IsValid => Uri.TryCreate(GatewayUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" &&
                           !string.IsNullOrWhiteSpace(TargetKey) && !string.IsNullOrWhiteSpace(Secret);

    public static AgentOptions Read(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal)) values[args[i][2..]] = args[++i];
        }

        var gateway = Get(values, "gateway", "GATEWAY_URL").TrimEnd('/') + "/";
        var target = Get(values, "target", "MONITOR_TARGET");
        var secret = Get(values, "secret", "MONITOR_SECRET");
        var intervalText = Get(values, "interval", "MONITOR_INTERVAL_SECONDS");
        var interval = int.TryParse(intervalText, out var parsed) ? Math.Clamp(parsed, 5, 300) : 10;
        return new AgentOptions(gateway, target.Trim().ToLowerInvariant(), secret, interval,
            Get(values, "listen", "MONITOR_LISTEN_URL") is { Length: > 0 } listen ? listen : "http://0.0.0.0:5188",
            Get(values, "log-path", "MONITOR_LOG_PATH"),
            Get(values, "nlog-config", "MONITOR_NLOG_CONFIG"),
            Get(values, "nlog-target", "MONITOR_NLOG_TARGET"),
            Get(values, "log-layout", "MONITOR_LOG_LAYOUT"));
    }

    private static string Get(IReadOnlyDictionary<string, string> values, string argument, string environment) =>
        values.GetValueOrDefault(argument) ?? Environment.GetEnvironmentVariable(environment) ?? string.Empty;
}
