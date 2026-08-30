namespace AiDataGateway.Application.Security;

public static class GatewayScopes
{
    public const string DataSourceRead = "gateway.datasource.read";
    public const string QueryExecute = "gateway.query.execute";
    public const string ChangeSubmit = "gateway.change.submit";
    public const string ChangeApprove = "gateway.change.approve";
    public const string AuditRead = "gateway.audit.read";
    public const string LogRead = "gateway.logs.read";
    public const string MetricsRead = "gateway.metrics.read";
    public const string Admin = "gateway.admin";

    public static readonly string[] AiClientDefaults = [DataSourceRead, QueryExecute, ChangeSubmit, LogRead, MetricsRead];
}
