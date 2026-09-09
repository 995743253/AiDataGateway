using AiDataGateway.Application.Abstractions;
using AiDataGateway.Application.Logs;
using AiDataGateway.Application.Sql;

namespace AiDataGateway.Tests;

public sealed class LogSqlTraceTests
{
    private static StructuredLogEvent Event(
        string message,
        IReadOnlyDictionary<string, object?>? properties = null,
        string rawText = "") => new(
        "evt-1", DateTimeOffset.UtcNow, "Info", message, null,
        properties ?? new Dictionary<string, object?>(), rawText);

    [Fact]
    public void Extract_prefers_well_known_property()
    {
        var item = Event("executed", new Dictionary<string, object?> { ["CommandText"] = "SELECT Id FROM Users WHERE Id = 1" });
        Assert.Equal("SELECT Id FROM Users WHERE Id = 1", LogSqlTextExtractor.Extract(item));
    }

    [Fact]
    public void Extract_cuts_sql_out_of_message_text()
    {
        var item = Event("查询用户失败: SELECT Id, Name FROM Users WHERE Id = 1 执行超时");
        var sql = LogSqlTextExtractor.Extract(item);
        Assert.NotNull(sql);
        Assert.StartsWith("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM Users", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Extract_ignores_plain_text_without_support_word()
    {
        Assert.Null(LogSqlTextExtractor.Extract(Event("select 附近的咖啡厅今天营业")));
        Assert.Null(LogSqlTextExtractor.Extract(Event("普通运行日志，没有任何语句")));
    }

    [Fact]
    public void Placeholder_count_ignores_question_marks_inside_strings()
    {
        var count = LogSqlParameterSubstitutor.CountPlaceholders("SELECT * FROM t WHERE a = ? AND b = 'what?' AND c = ?");
        Assert.Equal(2, count);
    }

    [Fact]
    public void Substitute_replaces_parameters_in_order()
    {
        var sql = LogSqlParameterSubstitutor.Substitute(
            "SELECT * FROM t WHERE a = ? AND b = ? AND c = 'x?'",
            ["42", "it's ok"]);
        Assert.Equal("SELECT * FROM t WHERE a = 42 AND b = 'it''s ok' AND c = 'x?'", sql);
    }

    [Fact]
    public void Substitute_maps_null_true_false_and_numbers()
    {
        var sql = LogSqlParameterSubstitutor.Substitute("VALUES (?, ?, ?, ?, ?)", [null, "true", "false", "-3.5", "abc"]);
        Assert.Equal("VALUES (NULL, 1, 0, -3.5, 'abc')", sql);
    }

    [Fact]
    public void Substitute_rejects_count_mismatch()
    {
        Assert.Throws<ArgumentException>(() => LogSqlParameterSubstitutor.Substitute("a = ? AND b = ?", ["1"]));
    }
}
