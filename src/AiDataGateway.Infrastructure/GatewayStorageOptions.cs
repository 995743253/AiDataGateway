namespace AiDataGateway.Infrastructure;

public sealed class GatewayStorageOptions
{
    public string BasePath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiDataGateway");
    public bool ProtectKeysWithDpapi { get; set; } = true;
}
