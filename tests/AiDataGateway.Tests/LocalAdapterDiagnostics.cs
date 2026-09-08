using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.Logs;
using AiDataGateway.Infrastructure.Logs;

namespace AiDataGateway.Tests;

public class LocalAdapterDiagnostics
{
    [Fact]
    public async Task Diagnose_folder_parse_writes_report()
    {
        var adapter = new LocalNLogSourceAdapter();
        var result = await adapter.QueryAsync(new LogSourceConnection(
                LogSourceType.LocalNLog, "D:\\Logs", string.Empty, string.Empty, string.Empty, string.Empty),
            new LogQueryOptions(FromUtc: null, ToUtc: null, Page: 1, PageSize: 2000));

        var report = new System.Text.StringBuilder();
        report.AppendLine("events: " + result.Items.Count);

        var byFile = result.Items
            .GroupBy(item => item.Properties.GetValueOrDefault("_file") as string ?? "?")
            .Select(group => System.IO.Path.GetFileName(group.Key) + "=" + group.Count()
                + " levels[" + string.Join(",", group.Select(item => item.Level ?? "null").Distinct()) + "]")
            .OrderBy(item => item);
        foreach (var entry in byFile) report.AppendLine("  file " + entry);

        foreach (var item in result.Items.Take(3))
        {
            report.AppendLine("== level=" + (item.Level ?? "<null>")
                + " time=" + item.TimestampUtc?.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            report.AppendLine("   message: " + (item.Message ?? "<null>")[..Math.Min(110, (item.Message ?? "<null>").Length)]);
            report.AppendLine("   exception: " + (item.Exception ?? "<null>")[..Math.Min(80, (item.Exception ?? "<null>").Length)]);
        }

        System.IO.File.WriteAllText("D:\\WorkStation\\DataGateway\\diag-report.txt", report.ToString());
    }
}
