using System.Text;
using System.Text.Json;
using AiDataGateway.Extensions;

namespace LocalMonitorReportExtension;

public sealed class LocalMonitorReportModule : IGatewayExtension
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static readonly JsonElement EmptySchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { },
        additionalProperties = false
    });

    private static readonly JsonElement ReportSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            targetKey = new { type = "string", description = "监控节点标识，默认 local。" },
            hours = new { type = "integer", minimum = 1, maximum = 168, @default = 24, description = "分析最近多少小时。" }
        },
        additionalProperties = false
    });

    public GatewayExtensionDefinition Definition { get; } = new(
        "local-monitor-report",
        "本机监控分析报告",
        "1.0.0",
        "根据网关保存的服务器指标生成健康评分、趋势图、异常提示和 Markdown 摘要。",
        "监控分析报告",
        "wwwroot/index.html",
        [
            new GatewayExtensionToolDefinition("list_targets", "列出可用于生成报告的监控节点。", EmptySchema,
                GatewayExtensionCapability.MetricsRead),
            new GatewayExtensionToolDefinition("generate_report", "分析指定监控节点近期的 CPU、内存和磁盘指标并生成报告。", ReportSchema,
                GatewayExtensionCapability.MetricsRead)
        ]);

    public async Task<JsonElement> InvokeAsync(
        string operation,
        JsonElement arguments,
        IGatewayExtensionContext context,
        CancellationToken cancellationToken) => operation switch
    {
        "list_targets" => JsonSerializer.SerializeToElement(new { items = await context.Monitoring.ListTargetsAsync(cancellationToken) }, WebJson),
        "generate_report" => await GenerateReportAsync(arguments, context, cancellationToken),
        _ => throw new ArgumentException($"Unknown operation '{operation}'.")
    };

    private static async Task<JsonElement> GenerateReportAsync(
        JsonElement arguments,
        IGatewayExtensionContext context,
        CancellationToken cancellationToken)
    {
        var targetKey = GetString(arguments, "targetKey") ?? "local";
        var hours = Math.Clamp(GetInt32(arguments, "hours") ?? 24, 1, 168);
        var to = DateTimeOffset.UtcNow;
        var from = to.AddHours(-hours);
        var metrics = await context.Monitoring.QueryAsync(targetKey, from, to, 360, cancellationToken);
        var definitions = new[]
        {
            new Metric("cpu.percent", "CPU", "%"),
            new Metric("memory.percent", "内存", "%"),
            new Metric("disk.percent", "磁盘", "%")
        };
        var summaries = definitions.Select(definition => Summarize(definition, metrics.Items)).ToArray();
        var pressure = summaries.Where(item => item.Count > 0).Select(item => item.Average).DefaultIfEmpty(0).Max();
        var peak = summaries.Where(item => item.Count > 0).Select(item => item.Maximum).DefaultIfEmpty(0).Max();
        var score = Math.Clamp((int)Math.Round(100 - pressure * .45 - Math.Max(0, peak - 80) * .8), 0, 100);
        var findings = BuildFindings(summaries, metrics.SourceCount, hours);
        var status = score >= 85 ? "健康" : score >= 65 ? "需关注" : "高风险";
        var markdown = BuildMarkdown(metrics.TargetName, hours, score, status, summaries, findings);
        return JsonSerializer.SerializeToElement(new
        {
            title = $"{metrics.TargetName} · 最近 {hours} 小时监控分析",
            generatedAtUtc = DateTimeOffset.UtcNow,
            metrics.TargetKey,
            metrics.TargetName,
            metrics.SourceCount,
            score,
            status,
            findings,
            summaries,
            series = definitions.Select(definition => new
            {
                definition.Key,
                definition.Name,
                definition.Unit,
                points = metrics.Items.Select(item => new
                {
                    timestampUtc = item.CollectedAtUtc,
                    value = item.Metrics.TryGetValue(definition.Key, out var value) ? Math.Round(value, 2) : (double?)null
                }).Where(item => item.value.HasValue)
            }),
            markdown
        }, WebJson);
    }

    private static MetricSummary Summarize(Metric definition, IReadOnlyList<GatewayExtensionMetricSample> samples)
    {
        var values = samples.Select(item => item.Metrics.TryGetValue(definition.Key, out var value) ? value : (double?)null)
            .Where(item => item.HasValue).Select(item => item!.Value).ToArray();
        if (values.Length == 0) return new MetricSummary(definition.Key, definition.Name, definition.Unit, 0, 0, 0, 0, 0, "无数据");
        var average = values.Average();
        var latest = values[^1];
        var firstWindow = values.Take(Math.Max(1, values.Length / 4)).Average();
        var lastWindow = values.TakeLast(Math.Max(1, values.Length / 4)).Average();
        var trend = lastWindow > firstWindow + 5 ? "上升" : lastWindow < firstWindow - 5 ? "下降" : "平稳";
        return new MetricSummary(definition.Key, definition.Name, definition.Unit, values.Length,
            Math.Round(latest, 2), Math.Round(average, 2), Math.Round(values.Min(), 2), Math.Round(values.Max(), 2), trend);
    }

    private static string[] BuildFindings(IEnumerable<MetricSummary> summaries, int sourceCount, int hours)
    {
        var result = new List<string>();
        if (sourceCount == 0) result.Add("所选时间范围内没有采样数据，请确认监控节点正在上报。");
        foreach (var metric in summaries.Where(item => item.Count > 0))
        {
            if (metric.Maximum >= 90) result.Add($"{metric.Name}峰值达到 {metric.Maximum:0.0}{metric.Unit}，建议检查高负载时段。");
            else if (metric.Average >= 75) result.Add($"{metric.Name}平均值为 {metric.Average:0.0}{metric.Unit}，长期处于偏高水平。");
            if (metric.Trend == "上升") result.Add($"{metric.Name}在最近 {hours} 小时呈上升趋势。");
        }
        if (result.Count == 0) result.Add("主要资源指标处于平稳范围，未发现明显容量风险。");
        return result.ToArray();
    }

    private static string BuildMarkdown(string target, int hours, int score, string status, IEnumerable<MetricSummary> summaries, IEnumerable<string> findings)
    {
        var text = new StringBuilder().AppendLine($"# {target} 监控分析报告").AppendLine()
            .AppendLine($"- 时间范围：最近 {hours} 小时").AppendLine($"- 健康评分：{score}/100（{status}）").AppendLine()
            .AppendLine("## 指标摘要").AppendLine();
        foreach (var item in summaries) text.AppendLine($"- {item.Name}：最新 {item.Latest:0.0}{item.Unit}，平均 {item.Average:0.0}{item.Unit}，峰值 {item.Maximum:0.0}{item.Unit}，趋势 {item.Trend}");
        text.AppendLine().AppendLine("## 分析结论").AppendLine();
        foreach (var finding in findings) text.AppendLine($"- {finding}");
        return text.ToString();
    }

    private static string? GetString(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static int? GetInt32(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number : null;

    private sealed record Metric(string Key, string Name, string Unit);
    private sealed record MetricSummary(string Key, string Name, string Unit, int Count, double Latest, double Average, double Minimum, double Maximum, string Trend);
}
