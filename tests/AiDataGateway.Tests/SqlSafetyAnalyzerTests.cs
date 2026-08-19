using AiDataGateway.Application.Sql;
using AiDataGateway.Domain.Sql;

namespace AiDataGateway.Tests;

public sealed class SqlSafetyAnalyzerTests
{
    private readonly SqlSafetyAnalyzer _analyzer = new();

    [Fact]
    public void Select_is_allowed_as_read_only()
    {
        var result = _analyzer.Analyze("select id, name from users where id = 1");

        Assert.True(result.Allowed);
        Assert.True(result.IsReadOnly);
        Assert.Equal(SqlRiskLevel.Low, result.RiskLevel);
    }

    [Fact]
    public void Delete_without_where_is_blocked()
    {
        var result = _analyzer.Analyze("delete from users");

        Assert.False(result.Allowed);
        Assert.False(result.IsReadOnly);
        Assert.Equal(SqlRiskLevel.Critical, result.RiskLevel);
    }

    [Fact]
    public void Multiple_statements_are_blocked()
    {
        var result = _analyzer.Analyze("select 1; drop table users");

        Assert.False(result.Allowed);
        Assert.Equal("MULTI_STATEMENT", result.Operation);
    }

    [Fact]
    public void Update_with_where_requires_approval()
    {
        var result = _analyzer.Analyze("update users set enabled = 0 where id = 1");

        Assert.True(result.Allowed);
        Assert.False(result.IsReadOnly);
        Assert.Equal(SqlRiskLevel.High, result.RiskLevel);
    }

    [Fact]
    public void Create_table_requires_approval()
    {
        var result = _analyzer.Analyze("create table api_test (id integer primary key)");

        Assert.True(result.Allowed);
        Assert.False(result.IsReadOnly);
        Assert.Equal(SqlRiskLevel.High, result.RiskLevel);
    }

    [Fact]
    public void Other_create_operations_remain_blocked()
    {
        var result = _analyzer.Analyze("create database unsafe_test");

        Assert.False(result.Allowed);
        Assert.Equal(SqlRiskLevel.Critical, result.RiskLevel);
    }
}
