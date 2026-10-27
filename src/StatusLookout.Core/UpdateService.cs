using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StatusLookout.Core;

public sealed record AvailableUpdate(Version Version, string Tag, string ReleaseUrl, string AssetName,
    string DownloadUrl, string Sha256, long Size)
{
    public bool IsInstaller => AssetName.StartsWith("StatusLookout-Setup-", StringComparison.Ordinal);
}

/// <summary>Release discovery and requested downloads from this project's stable GitHub releases only.</summary>
public sealed class UpdateService(HttpClient http)
{
    public const string LatestEndpoint = "https://api.github.com/repos/Bouneh/statuslookout/releases/latest";
    private const long MaxExecutableBytes = 250 * 1024 * 1024;
    public static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
    public async Task<AvailableUpdate?> CheckAsync(Version installed, string runtime, CancellationToken token = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestEndpoint);
        request.Headers.UserAgent.ParseAdd("StatusLookout/" + Normalize(installed).ToString(3));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 1024 * 1024) throw new InvalidDataException("Release metadata is too large.");
        using var source = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var bytes = new byte[8192]; int read;
        while ((read = await source.ReadAsync(bytes, token)) != 0)
        {
            if (buffer.Length + read > 1024 * 1024) throw new InvalidDataException("Release metadata is too large.");
            await buffer.WriteAsync(bytes.AsMemory(0, read), token);
        }
        return ParseRelease(buffer.ToArray(), installed, runtime);
    }
    public static AvailableUpdate? ParseRelease(byte[] json, Version installed, string runtime)
    {
        try { return ReadRelease(json, installed, runtime); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new InvalidDataException("The release metadata is invalid.", ex); }
    }
    private static AvailableUpdate? ReadRelease(byte[] json, Version installed, string runtime)
    {
        if (runtime is not ("win-x64" or "win-x86" or "win-arm64")) throw new ArgumentException("Unsupported Windows architecture.");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+$") || !Version.TryParse(tag[1..], out var version))
            throw new InvalidDataException("The release version is invalid.");
        if (Normalize(version) <= Normalize(installed)) return null;
        var assets = root.GetProperty("assets").EnumerateArray().ToArray();
        var name = "StatusLookout-Setup-" + runtime + ".exe";
        var asset = assets.SingleOrDefault(a => a.GetProperty("name").GetString() == name);
        if (asset.ValueKind == JsonValueKind.Undefined)
        {
            name = "StatusLookout-" + runtime + ".exe";
            asset = assets.SingleOrDefault(a => a.GetProperty("name").GetString() == name);
        }
        if (asset.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("This release has no executable for your Windows architecture.");
        var url = asset.GetProperty("browser_download_url").GetString() ?? "";
        var releaseUrl = "https://github.com/Bouneh/statuslookout/releases/tag/" + tag;
        var expectedUrl = "https://github.com/Bouneh/statuslookout/releases/download/" + tag + "/" + name;
        if (url != expectedUrl) throw new InvalidDataException("The update is not hosted in the official repository.");
        var digest = asset.GetProperty("digest").GetString() ?? "";
        if (!Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$")) throw new InvalidDataException("The release has no valid SHA-256 checksum.");
        var size = asset.GetProperty("size").GetInt64();
        if (size <= 0 || size > MaxExecutableBytes) throw new InvalidDataException("The executable size is invalid.");
        return new(version, tag, releaseUrl, name, url, digest[7..].ToLowerInvariant(), size);
    }
    public async Task DownloadAsync(AvailableUpdate update, string destination, IProgress<double>? progress = null, CancellationToken token = default)
    {
        ValidateDownload(update);
        var fullPath = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var partial = fullPath + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, update.DownloadUrl);
            request.Headers.UserAgent.ParseAdd("StatusLookout-update-download");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long size && size != update.Size)
                throw new InvalidDataException("The download size does not match the release.");
            await using var input = await response.Content.ReadAsStreamAsync(token);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920]; long total = 0; int read;
                while ((read = await input.ReadAsync(buffer, token)) != 0)
                {
                    total += read;
                    if (total > update.Size) throw new InvalidDataException("The download is larger than expected.");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    progress?.Report(100d * total / update.Size);
                }
                if (total != update.Size) throw new InvalidDataException("The download is incomplete.");
                await output.FlushAsync(token);
            }
            if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(update.Sha256)))
                throw new InvalidDataException("The download checksum does not match the release.");
            token.ThrowIfCancellationRequested();
            File.Move(partial, fullPath, overwrite: true);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
    private static void ValidateDownload(AvailableUpdate update)
    {
        // Public records can be constructed by callers. Validate again before downloading or launching.
        var expectedUrl = "https://github.com/Bouneh/statuslookout/releases/download/" + update.Tag + "/" + update.AssetName;
        if (!Regex.IsMatch(update.Tag, @"^v\d+\.\d+\.\d+$")
            || !Regex.IsMatch(update.AssetName, @"^StatusLookout-(Setup-)?win-(x64|x86|arm64)\.exe$")
            || update.DownloadUrl != expectedUrl || !Regex.IsMatch(update.Sha256, "^[a-fA-F0-9]{64}$")
            || update.Size <= 0 || update.Size > MaxExecutableBytes) throw new InvalidDataException("Invalid update metadata.");
    }
    public static async Task VerifyInstallerAsync(AvailableUpdate update, string path, CancellationToken token = default)
    {
        ValidateDownload(update);
        if (!update.IsInstaller) throw new InvalidDataException("This update is a portable executable, not an installer.");
        await using var input = new FileStream(Path.GetFullPath(path), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        if (input.Length != update.Size) throw new InvalidDataException("The installer size no longer matches the release.");
        var hash = await SHA256.HashDataAsync(input, token);
        if (!CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(update.Sha256)))
            throw new InvalidDataException("The installer checksum no longer matches the release. Download it again.");
    }
}
