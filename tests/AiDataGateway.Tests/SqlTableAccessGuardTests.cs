using AiDataGateway.Application.Sql;

namespace AiDataGateway.Tests;

public sealed class SqlTableAccessGuardTests
{
    private readonly SqlTableAccessGuard _guard = new();

    [Theory]
    [InlineData("select * from GatewayAuditEntries", "gatewayauditentries")]
    [InlineData("select a.id from main.Allowed a join [GatewayAuditEntries] g on g.Id = a.Id", "main.GatewayAuditEntries")]
    [InlineData("select * from allowed, `GatewayAuditEntries` where allowed.id = GatewayAuditEntries.id", "GatewayAuditEntries")]
    [InlineData("select * from (select * from \"GatewayAuditEntries\") audit_rows", "GatewayAuditEntries")]
    public void Detects_blocked_tables_in_common_query_shapes(string sql, string blockedTable)
    {
        var result = _guard.FindBlockedTables(sql, [blockedTable]);

        Assert.Single(result);
    }

    [Fact]
    public void Does_not_match_a_column_name_outside_from_or_join()
    {
        var result = _guard.FindBlockedTables("select GatewayAuditEntries from allowed_table", ["GatewayAuditEntries"]);

        Assert.Empty(result);
    }
}
