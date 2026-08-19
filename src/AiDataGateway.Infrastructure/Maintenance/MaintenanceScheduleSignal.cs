using System.Threading.Channels;
using AiDataGateway.Application.Abstractions;

namespace AiDataGateway.Infrastructure.Maintenance;

internal sealed class MaintenanceScheduleSignal : IMaintenanceScheduleNotifier
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });

    public void NotifyScheduleChanged() => _channel.Writer.TryWrite(true);

    public ValueTask<bool> WaitAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAsync(cancellationToken);

    public async Task<bool> WaitUntilAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var signalTask = _channel.Reader.ReadAsync(linkedCancellation.Token).AsTask();
        var delayTask = Task.Delay(delay, linkedCancellation.Token);
        var completed = await Task.WhenAny(signalTask, delayTask);
        linkedCancellation.Cancel();
        try { await Task.WhenAll(signalTask, delayTask); } catch (OperationCanceledException) { }
        return completed == signalTask;
    }
}
