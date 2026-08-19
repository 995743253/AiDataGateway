namespace AiDataGateway.Application.Security;

public static class GatewayRoles
{
    public const string Administrator = "Administrator";
    public const string Operator = "Operator";
    public const string Approver = "Approver";
    public const string Auditor = "Auditor";
    public const string Developer = "Developer";
    public const string Viewer = "Viewer";

    public static readonly string[] All = [Administrator, Operator, Approver, Auditor, Developer, Viewer];
}
