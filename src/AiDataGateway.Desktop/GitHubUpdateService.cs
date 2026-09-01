using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiDataGateway.Desktop;

internal sealed record GatewayUpdate(Version Version, string VersionText, string AssetName, Uri DownloadUrl, string Sha256, Uri ReleasePage);

internal sealed class GitHubUpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/995743253/AiDataGateway/releases/latest";
    private static readonly HttpClient Client = CreateClient();

    public static string CurrentVersionText => (Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    public async Task<GatewayUpdate?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(LatestReleaseUrl, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<ReleaseDto>(content, cancellationToken: cancellationToken)
                      ?? throw new InvalidDataException("GitHub Release 返回为空。");

        var versionText = release.TagName.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(versionText.Split('-', 2)[0], out var remoteVersion))
            throw new InvalidDataException($"无法识别发布版本：{release.TagName}");
        var current = Version.TryParse(CurrentVersionText.Split('-', 2)[0], out var parsed) ? parsed : new Version(0, 0);
        if (remoteVersion <= current) return null;

        var installer = release.Assets.FirstOrDefault(asset =>
            asset.Name.StartsWith("AiDataGateway-Setup-", StringComparison.OrdinalIgnoreCase) &&
            asset.Name.EndsWith("-win-x64.exe", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("最新 Release 没有 Windows x64 安装器。");

        var sha256 = ParseDigest(installer.Digest);
        if (sha256 is null)
        {
            var checksumAsset = release.Assets.FirstOrDefault(asset => asset.Name.Equals(installer.Name + ".sha256", StringComparison.OrdinalIgnoreCase));
            if (checksumAsset is null) throw new InvalidDataException("安装器缺少 SHA-256 校验文件。");
            sha256 = ParseChecksum(await Client.GetStringAsync(checksumAsset.BrowserDownloadUrl, cancellationToken));
        }

        return new GatewayUpdate(remoteVersion, versionText, installer.Name, new Uri(installer.BrowserDownloadUrl), sha256, new Uri(release.HtmlUrl));
    }

    public async Task<string> DownloadAsync(GatewayUpdate update, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AiDataGateway", "Updates", update.VersionText);
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, update.AssetName);
        if (File.Exists(target) && await HashMatchesAsync(target, update.Sha256, cancellationToken))
        {
            progress?.Report(100);
            return target;
        }

        using var response = await Client.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var length = response.Content.Headers.ContentLength;
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var destination = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, true))
        {
            var buffer = new byte[128 * 1024];
            long total = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                total += read;
                if (length > 0) progress?.Report((int)Math.Min(99, total * 100 / length.Value));
            }
        }

        if (!await HashMatchesAsync(target, update.Sha256, cancellationToken))
        {
            File.Delete(target);
            throw new InvalidDataException("安装器 SHA-256 校验失败，文件已删除。");
        }
        progress?.Report(100);
        return target;
    }

    public static void StartInstaller(string installerPath)
    {
        var start = new ProcessStartInfo(installerPath) { UseShellExecute = true };
        start.ArgumentList.Add("--update");
        start.ArgumentList.Add("--silent");
        start.ArgumentList.Add("--wait-pid");
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        start.ArgumentList.Add("--launch");
        Process.Start(start);
    }

    private static async Task<bool> HashMatchesAsync(string path, string expected, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ParseDigest(string? value) => value?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true ? ParseChecksum(value[7..]) : null;

    private static string ParseChecksum(string value)
    {
        var checksum = value.Trim().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0];
        return checksum.Length == 64 && checksum.All(Uri.IsHexDigit) ? checksum.ToUpperInvariant() : throw new InvalidDataException("SHA-256 校验值格式不正确。");
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AiDataGateway", CurrentVersionText));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private sealed class ReleaseDto
    {
        [JsonPropertyName("tag_name")] public string TagName { get; init; } = string.Empty;
        [JsonPropertyName("html_url")] public string HtmlUrl { get; init; } = string.Empty;
        [JsonPropertyName("assets")] public List<AssetDto> Assets { get; init; } = [];
    }

    private sealed class AssetDto
    {
        [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; init; } = string.Empty;
        [JsonPropertyName("digest")] public string? Digest { get; init; }
    }
}
