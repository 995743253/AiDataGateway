using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.Logs;
using AiDataGateway.Infrastructure.Logs;

namespace AiDataGateway.Tests;

public class LocalAdapterDiagnostics
{
    [Fact]
    public async Task Diagnose_folder_parse_produces_events()
    {
        var endpoint = "D:\\Logs";
        if (!Directory.Exists(endpoint))
        {
            // CI runners and other machines may not have this folder; the test
            // only validates parsing when the sample logs are available.
            return;
        }

        var adapter = new LocalNLogSourceAdapter();
        var result = await adapter.QueryAsync(new LogSourceConnection(
                LogSourceType.LocalNLog, endpoint, string.Empty, string.Empty, string.Empty, string.Empty),
            new LogQueryOptions(FromUtc: null, ToUtc: null, Page: 1, PageSize: 2000));

        Assert.True(result.Items.Count > 0, "Expected at least one parsed log entry.");

        var errorEntries = result.Items.Where(item => item.Level == "Error").ToList();
        Assert.True(errorEntries.Count > 0, "Expected at least one Error-level entry in the test log folder.");

        var debugEntries = result.Items.Where(item => item.Level == "Debug").ToList();
        Assert.True(debugEntries.Count > 0, "Expected at least one Debug-level entry.");
    }
}
