namespace AiDataGateway.Domain.Approvals;

public enum ChangeStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Executing = 4,
    Succeeded = 5,
    Failed = 6,
    Expired = 7
}
