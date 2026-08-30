namespace AiDataGateway.Api;

public sealed class GatewayHostOptions
{
    public int Port { get; set; } = 5127;
    public string ListenAddress { get; set; } = "127.0.0.1";
    public string StoragePath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiDataGateway");
    public string? WebRootPath { get; set; }
    public bool UseEphemeralCertificates { get; set; }
}
