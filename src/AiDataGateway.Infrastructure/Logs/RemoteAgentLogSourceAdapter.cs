using System.Net.Http.Json;
using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.Logs;

namespace AiDataGateway.Infrastructure.Logs;

internal sealed class RemoteAgentLogSourceAdapter(IHttpClientFactory httpClientFactory) : ILogSourceAdapter
{
    public LogSourceType Type => LogSourceType.RemoteAgent;

    public async Task<LogSourceTestResult> TestAsync(LogSourceConnection connection, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = CreateRequest(connection, HttpMethod.Get, "api/agent/logs/status");
            using var response = await httpClientFactory.CreateClient("AiDataGateway.Seq").SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? new LogSourceTestResult(true, "远程 Agent 连接成功，可以读取该服务器的本地日志。")
                : new LogSourceTestResult(false, $"远程 Agent 返回 HTTP {(int)response.StatusCode}。");
        }
        catch (Exception exception) { return new LogSourceTestResult(false, exception.Message); }
    }

    public async Task<LogQueryResult> QueryAsync(LogSourceConnection connection, LogQueryOptions options, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(connection, HttpMethod.Post, "api/agent/logs/query");
        request.Content = JsonContent.Create(options);
        using var response = await httpClientFactory.CreateClient("AiDataGateway.Seq").SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"远程 Agent 日志查询失败（HTTP {(int)response.StatusCode}）。");
        return await response.Content.ReadFromJsonAsync<LogQueryResult>(cancellationToken: cancellationToken)
               ?? throw new InvalidDataException("远程 Agent 返回了空响应。");
    }

    private static HttpRequestMessage CreateRequest(LogSourceConnection connection, HttpMethod method, string path)
    {
        if (!Uri.TryCreate(connection.Endpoint.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
            throw new ArgumentException("远程 Agent 地址无效。");
        var request = new HttpRequestMessage(method, new Uri(baseUri, path));
        request.Headers.TryAddWithoutValidation("X-Agent-Key", connection.ApiKey);
        return request;
    }
}
