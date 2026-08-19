using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AiDataGateway.Application.Abstractions;

namespace AiDataGateway.Api.Realtime;

public sealed record GatewayEvent(string Topic, string? Action, DateTimeOffset OccurredAtUtc);

public sealed class GatewayEventHub : IGatewayEventPublisher
{
    private readonly ConcurrentDictionary<Guid, Channel<GatewayEvent>> _subscribers = new();

    public void Publish(string topic, string? action = null)
    {
        var gatewayEvent = new GatewayEvent(topic, action, DateTimeOffset.UtcNow);
        foreach (var subscriber in _subscribers.Values)
        {
            subscriber.Writer.TryWrite(gatewayEvent);
        }
    }

    public async IAsyncEnumerable<GatewayEvent> Subscribe([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<GatewayEvent>(new BoundedChannelOptions(100)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        _subscribers[id] = channel;

        try
        {
            await foreach (var gatewayEvent in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return gatewayEvent;
            }
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
        }
    }
}
