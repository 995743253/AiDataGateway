using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiDataGateway.Extensions;
using Microsoft.AspNetCore.DataProtection;

namespace AiDataGateway.Infrastructure.Extensions;

internal sealed class GatewayExtensionFileStorage : IGatewayExtensionStorage
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex SafeKey = new("^[a-z][a-z0-9_-]{0,79}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly string _directory;
    private readonly IDataProtector _protector;

    public GatewayExtensionFileStorage(string extensionsRoot, string moduleId, IDataProtectionProvider protectionProvider)
    {
        _directory = Path.Combine(extensionsRoot, ".data", moduleId);
        _protector = protectionProvider.CreateProtector("AiDataGateway.ExtensionStorage.v1", moduleId);
        Directory.CreateDirectory(_directory);
    }

    public async Task<JsonElement?> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);
        var gate = Gates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path)) return null;
            var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
            using var document = JsonDocument.Parse(_protector.Unprotect(protectedBytes));
            return document.RootElement.Clone();
        }
        finally { gate.Release(); }
    }

    public async Task WriteAsync(string key, JsonElement value, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);
        var gate = Gates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(value));
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            gate.Release();
        }
    }

    private string PathFor(string key)
    {
        if (!SafeKey.IsMatch(key ?? string.Empty)) throw new ArgumentException("Invalid extension storage key.", nameof(key));
        return Path.Combine(_directory, key + ".json.protected");
    }
}
