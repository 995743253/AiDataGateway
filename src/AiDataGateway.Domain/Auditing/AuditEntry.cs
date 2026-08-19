namespace AiDataGateway.Domain.Auditing;

public sealed class AuditEntry
{
    private AuditEntry()
    {
    }

    public AuditEntry(string actor, string action, string outcome, Guid? dataSourceId = null, string? detail = null)
    {
        Id = Guid.NewGuid();
        Actor = actor;
        Action = action;
        Outcome = outcome;
        DataSourceId = dataSourceId;
        Detail = detail;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public string Actor { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string Outcome { get; private set; } = string.Empty;
    public Guid? DataSourceId { get; private set; }
    public string? Detail { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
