using AiDataGateway.Infrastructure.Logs;
using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.Logs;
using System.Text;

namespace AiDataGateway.Tests;

public sealed class NLogParserTests
{
    private const string Layout = "${longdate}|${level}|${logger}|${message}|${exception}";

    [Fact]
    public void Text_parser_preserves_multiline_empty_and_unterminated_last_record()
    {
        var input =
            "2026-08-29 10:00:00.0000|Info|Order.Api|started|\n" +
            "2026-08-29 10:00:01.0000|Error|Order.Api|request failed\ncontinued message|System.InvalidOperationException: broken\n   at Demo.Run()\n" +
            "2026-08-29 10:00:02.0000|Warning|Order.Api||";

        var items = LocalNLogSourceAdapter.ParseForTest(input, Layout);

        Assert.Equal(3, items.Count);
        Assert.Equal("started", items[0].Message);
        Assert.Contains("continued message", items[1].Message);
        Assert.Contains("InvalidOperationException", items[1].Exception);
        Assert.Null(items[2].Message);
        Assert.Null(items[2].Exception);
        Assert.False(items[2].Incomplete);
    }

    [Fact]
    public void Nlog_variables_are_resolved_before_path_and_layout_tokens()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"AiDataGateway-NLog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var configPath = Path.Combine(directory, "NLog.config");
            File.WriteAllText(configPath,
                """
                <nlog xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
                  <variable name="Layout" value="${longdate}|${level:uppercase=true}[${threadid}]${message}|${exception:format=ToString, StackTrace}" />
                  <variable name="LogTxtLocation" value="${basedir}/Logs/${level:uppercase=true}_${date:format=yyyy-MM-dd-HH}" />
                  <targets><target xsi:type="File" name="File" fileName="${LogTxtLocation}.log" layout="${Layout}" /></targets>
                </nlog>
                """);

            var result = NLogConfigurationResolver.Resolve(new LogSourceConnection(
                LogSourceType.LocalNLog, string.Empty, configPath, "File", string.Empty, string.Empty));

            Assert.Contains("Logs", result.FilePattern);
            Assert.EndsWith("*_*.log", result.FilePattern);
            Assert.StartsWith("${longdate}|${level:uppercase=true}[${threadid}]", result.Layout);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Local_source_auto_detects_gb18030_and_parses_real_layout()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var directory = Path.Combine(Path.GetTempPath(), $"AiDataGateway-NLog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var now = DateTimeOffset.Now;
            var file = Path.Combine(directory, $"DEBUG_{now:yyyy-MM-dd-HH}.log");
            var text = $"{now:yyyy-MM-dd HH:mm:ss.ffff}|DEBUG[197][ServicesSTD.Module_WIP] [2.1.0] 查询人员和仓库信息 requestId=abc-123 Service Response Content:{{\"success\":true,\"count\":2,\"data\":{{\"name\":\"人员\"}}}}|\r\n";
            await File.WriteAllBytesAsync(file, Encoding.GetEncoding("GB18030").GetBytes(text));
            var adapter = new LocalNLogSourceAdapter();
            var result = await adapter.QueryAsync(new LogSourceConnection(LogSourceType.LocalNLog, directory,
                    string.Empty, string.Empty, "${longdate}|${level:uppercase=true}[${threadid}]${message}|${exception}", string.Empty),
                new LogQueryOptions(FromUtc: now.AddHours(-1), ToUtc: now.AddHours(1)));

            var item = Assert.Single(result.Items);
            Assert.Equal("DEBUG", item.Level);
            Assert.Equal("197", item.Properties["threadid"]);
            Assert.Contains("人员和仓库", item.Message);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Incorrect_layout_falls_back_to_common_nlog_envelope()
    {
        const string input = "2026-03-06 17:00:33.5606|DEBUG[197][ServicesSTD.Module_WIP.warehouse_fail_eaiqueue_info_get] [2.1.0.00000000] Service Request Content:{ \"StockIn_Type\": 0 }|";
        const string staleUiDefault = "${longdate}|${level}|${logger}|${message}|${exception}";

        var item = Assert.Single(LocalNLogSourceAdapter.ParseForTest(input, staleUiDefault));

        Assert.NotNull(item.TimestampUtc);
        Assert.Equal("DEBUG", item.Level);
        Assert.Equal("197", item.Properties["threadid"]);
        Assert.StartsWith("[ServicesSTD.Module_WIP.warehouse_fail_eaiqueue_info_get]", item.Message);
    }

    [Fact]
    public void Message_with_enough_pipes_does_not_pollute_level_fields()
    {
        const string input = "2026-03-06 17:00:33.5606|DEBUG[197][ServicesSTD.Module_WIP] a|b|c|";
        const string staleUiDefault = "${longdate}|${level}|${logger}|${message}|${exception}";

        var item = Assert.Single(LocalNLogSourceAdapter.ParseForTest(input, staleUiDefault));

        Assert.Equal("DEBUG", item.Level);
        Assert.Equal("[ServicesSTD.Module_WIP] a|b|c", item.Message);
    }

    [Fact]
    public void Seq_simple_filter_escapes_values_and_pushes_date_range_to_server()
    {
        var from = DateTimeOffset.Parse("2026-08-29T00:00:00Z");
        var to = DateTimeOffset.Parse("2026-08-30T00:00:00Z");

        var filter = SeqLogSourceAdapter.BuildFilter(new LogQueryOptions(
            Level: "Error", FromUtc: from, ToUtc: to, SearchText: "order \"failed\"",
            PropertyName: "Application.Name", PropertyValue: "Order.Api"));

        Assert.Contains("\"order \\\"failed\\\"\"", filter);
        Assert.Contains("Application.Name = 'Order.Api'", filter);
        Assert.Contains("@Level = 'Error'", filter);
        Assert.Contains("@Timestamp >= DateTime('2026-08-29T00:00:00.000Z')", filter);
        Assert.Contains("@Timestamp <= DateTime('2026-08-30T00:00:00.000Z')", filter);
    }

    [Fact]
    public async Task Local_source_includes_exactly_ten_megabytes_and_truncates_above_it()
    {
        var path = Path.Combine(Path.GetTempPath(), $"AiDataGateway-NLog-{Guid.NewGuid():N}.log");
        try
        {
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                stream.SetLength(10L * 1024 * 1024);
            var adapter = new LocalNLogSourceAdapter();
            var connection = new LogSourceConnection(LogSourceType.LocalNLog, path, string.Empty, string.Empty, string.Empty, string.Empty);
            var range = new LogQueryOptions(FromUtc: DateTimeOffset.UtcNow.AddMinutes(-1), ToUtc: DateTimeOffset.UtcNow.AddMinutes(1));

            var exact = await adapter.QueryAsync(connection, range);
            Assert.False(exact.IsPartial);

            await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read))
                stream.SetLength(10L * 1024 * 1024 + 1);
            var over = await adapter.QueryAsync(connection, range);
            Assert.True(over.IsPartial);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Json_parser_handles_multiline_null_and_incomplete_tail()
    {
        var input =
            """
            {
              "@t": "2026-08-29T10:00:00Z",
              "@l": "Information",
              "@m": "complete",
              "nullable": null
            }
            {"@t":"2026-08-29T10:00:01Z","@l":"Error","@m":"unfinished"
            """;

        var items = LocalNLogSourceAdapter.ParseForTest(input, string.Empty, json: true);

        Assert.Equal(2, items.Count);
        Assert.Equal("complete", items[0].Message);
        Assert.True(items[0].Properties.ContainsKey("nullable"));
        Assert.Null(items[0].Properties["nullable"]);
        Assert.True(items[1].Incomplete);
        Assert.Contains("未闭合", items[1].ParseWarning);
        Assert.Contains("unfinished", items[1].RawText);
    }

    [Fact]
    public void Folder_source_uses_log_files_and_orders_newest_first()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"AiDataGateway-NLog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var older = Path.Combine(directory, "older.log");
            var newer = Path.Combine(directory, "newer.log");
            File.WriteAllText(older, "old");
            File.WriteAllText(newer, "new");
            File.WriteAllText(Path.Combine(directory, "ignored.txt"), "ignored");
            File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddMinutes(-2));
            File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddMinutes(-1));

            var files = NLogConfigurationResolver.FindFiles(directory);

            Assert.Equal([newer, older], files);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
