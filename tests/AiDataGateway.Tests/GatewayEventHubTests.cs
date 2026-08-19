using AiDataGateway.Api.Realtime;

namespace AiDataGateway.Tests;

public sealed class GatewayEventHubTests
{
    [Fact]
    public async Task Event_hub_broadcasts_change_without_polling()
    {
        var hub = new GatewayEventHub();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var subscription = hub.Subscribe(timeout.Token).GetAsyncEnumerator(timeout.Token);

        var moveNext = subscription.MoveNextAsync().AsTask();
        hub.Publish("audit", "change.submit");

        Assert.True(await moveNext);
        Assert.Equal("audit", subscription.Current.Topic);
        Assert.Equal("change.submit", subscription.Current.Action);
    }
}
