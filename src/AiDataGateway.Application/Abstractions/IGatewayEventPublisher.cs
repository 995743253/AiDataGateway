namespace AiDataGateway.Application.Abstractions;

public interface IGatewayEventPublisher
{
    void Publish(string topic, string? action = null);
}
