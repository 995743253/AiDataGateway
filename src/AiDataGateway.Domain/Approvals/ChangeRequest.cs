using AiDataGateway.Domain.Sql;

namespace AiDataGateway.Domain.Approvals;

public sealed class ChangeRequest
{
    private ChangeRequest()
    {
    }

    public ChangeRequest(Guid dataSourceId, string sql, string requestedBy, SqlRiskLevel riskLevel, int expirationMinutes = 15)
    {
        if (expirationMinutes is < 1 or > 10_080)
        {
            throw new ArgumentOutOfRangeException(nameof(expirationMinutes));
        }
        Id = Guid.NewGuid();
        DataSourceId = dataSourceId;
        Sql = sql;
        RequestedBy = requestedBy;
        RiskLevel = riskLevel;
        Status = ChangeStatus.Pending;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        ExpiresAtUtc = CreatedAtUtc.AddMinutes(expirationMinutes);
    }

    public Guid Id { get; private set; }
    public Guid DataSourceId { get; private set; }
    public string Sql { get; private set; } = string.Empty;
    public string RequestedBy { get; private set; } = string.Empty;
    public string? ReviewedBy { get; private set; }
    public string? ReviewComment { get; private set; }
    public SqlRiskLevel RiskLevel { get; private set; }
    public ChangeStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ReviewedAtUtc { get; private set; }
    public DateTimeOffset? ExecutedAtUtc { get; private set; }
    public string? ExecutionError { get; private set; }

    public void Approve(string reviewedBy, string? comment)
    {
        EnsurePending();
        ReviewedBy = reviewedBy;
        ReviewComment = comment;
        ReviewedAtUtc = DateTimeOffset.UtcNow;
        Status = ChangeStatus.Approved;
    }

    public void Reject(string reviewedBy, string? comment)
    {
        EnsurePending();
        ReviewedBy = reviewedBy;
        ReviewComment = comment;
        ReviewedAtUtc = DateTimeOffset.UtcNow;
        Status = ChangeStatus.Rejected;
    }

    public void MarkExecuted(bool succeeded, string? error)
    {
        if (Status != ChangeStatus.Approved && Status != ChangeStatus.Executing)
        {
            throw new InvalidOperationException("Only an approved change can be executed.");
        }

        ExecutedAtUtc = DateTimeOffset.UtcNow;
        ExecutionError = error;
        Status = succeeded ? ChangeStatus.Succeeded : ChangeStatus.Failed;
    }

    private void EnsurePending()
    {
        if (Status != ChangeStatus.Pending || ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("The change request is no longer pending.");
        }
    }
}
