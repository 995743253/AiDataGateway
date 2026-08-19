namespace AiDataGateway.Application.Abstractions;

public interface IUserHistoryChecker
{
    Task<bool> HasHistoryAsync(string userName, CancellationToken cancellationToken = default);
}
