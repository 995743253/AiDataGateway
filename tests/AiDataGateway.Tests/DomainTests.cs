using AiDataGateway.Domain.Approvals;
using AiDataGateway.Domain.DataSources;
using AiDataGateway.Domain.Sql;

namespace AiDataGateway.Tests;

public sealed class DomainTests
{
    [Fact]
    public void Data_source_normalizes_key_and_limits_resource_settings()
    {
        var source = new DataSourceDefinition(" Project-A ", "Project A", DatabaseProvider.SqlServer, "127.0.0.1", 1433, "App", "sa", DataSourceAccessMode.ReadOnly);
        source.Update(" Project-A ", "Project A", DatabaseProvider.SqlServer, "127.0.0.1", 1433, "App", "sa", DataSourceAccessMode.ReadOnly, 99_999, 9_999, true);

        Assert.Equal("project-a", source.Key);
        Assert.Equal(10_000, source.MaxRows);
        Assert.Equal(300, source.CommandTimeoutSeconds);
    }

    [Fact]
    public void Change_request_can_only_be_executed_after_approval()
    {
        var request = new ChangeRequest(Guid.NewGuid(), "update t set value = 1 where id = 1", "ai-client", SqlRiskLevel.High);

        Assert.Throws<InvalidOperationException>(() => request.MarkExecuted(true, null));
        request.Approve("admin", "approved locally");
        request.MarkExecuted(true, null);

        Assert.Equal(ChangeStatus.Succeeded, request.Status);
    }

    [Fact]
    public void Change_request_uses_configured_expiration()
    {
        var request = new ChangeRequest(Guid.NewGuid(), "update t set value = 1 where id = 1", "ai-client", SqlRiskLevel.High, 90);

        Assert.InRange(request.ExpiresAtUtc - request.CreatedAtUtc, TimeSpan.FromMinutes(89.9), TimeSpan.FromMinutes(90.1));
    }
}
