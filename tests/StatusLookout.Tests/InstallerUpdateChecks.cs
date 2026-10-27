using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using StatusLookout.Core;

internal static class InstallerUpdateChecks
{
    private static readonly byte[] Payload = "verified installer bytes"u8.ToArray();
    private static byte[] Release(bool includePortable = true, bool badInstaller = false) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        tag_name = "v90.0.0", draft = false, prerelease = false,
        assets = new[] { "win-x64", "win-x86", "win-arm64" }.SelectMany(runtime => (includePortable ? new[] { "StatusLookout-", "StatusLookout-Setup-" } : new[] { "StatusLookout-Setup-" }).Select(prefix => new
        {
            name = prefix + runtime + ".exe",
            browser_download_url = "https://github.com/Bouneh/statuslookout/releases/download/v90.0.0/" + prefix + runtime + ".exe",
            digest = prefix.Contains("Setup") && badInstaller ? "" : "sha256:" + Convert.ToHexString(SHA256.HashData(Payload)),
            size = Payload.Length
        }))
    });
    public static async Task Run(Action<string, Action> check, Func<string, Func<Task>, Task> checkAsync)
    {
        void Assert(bool condition) { if (!condition) throw new Exception("Installer update assertion failed."); }
        foreach (var runtime in new[] { "win-x64", "win-x86", "win-arm64" })
            check("Updater prefers the installer for " + runtime, () =>
            {
                var update = UpdateService.ParseRelease(Release(), new(0, 7, 1), runtime)!;
                Assert(update.IsInstaller && update.AssetName == "StatusLookout-Setup-" + runtime + ".exe");
                Assert(UpdateService.ParseRelease(Release(false), new(0, 7, 1), runtime)!.IsInstaller);
            });
        check("An invalid installer checksum cannot fall back to a portable download", () =>
        {
            try { UpdateService.ParseRelease(Release(badInstaller: true), new(0, 7, 1), "win-x64"); }
            catch (InvalidDataException) { return; }
            throw new Exception("Unverified installer was accepted.");
        });
        var directory = Path.Combine(Path.GetTempPath(), "StatusLookout-installer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var update = UpdateService.ParseRelease(Release(), new(0, 7, 1), "win-x64")!;
            var destination = Path.Combine(directory, "setup.exe");
            using var client = new HttpClient(new Handler());
            await checkAsync("An official installer downloads and passes verification before launch", async () =>
            {
                await new UpdateService(client).DownloadAsync(update, destination);
                await UpdateService.VerifyInstallerAsync(update, destination);
                Assert(File.ReadAllBytes(destination).SequenceEqual(Payload));
            });
            await checkAsync("An installer changed after download is rejected before execution", async () =>
            {
                var changed = Payload.ToArray(); changed[0] ^= 1; File.WriteAllBytes(destination, changed);
                try { await UpdateService.VerifyInstallerAsync(update, destination); }
                catch (InvalidDataException) { return; }
                throw new Exception("Modified installer was accepted.");
            });
            await checkAsync("A truncated installer is rejected before execution", async () =>
            {
                File.WriteAllBytes(destination, [1]);
                try { await UpdateService.VerifyInstallerAsync(update, destination); }
                catch (InvalidDataException) { return; }
                throw new Exception("Truncated installer was accepted.");
            });
            await checkAsync("Portable updates cannot be executed as installers", async () =>
            {
                File.WriteAllBytes(destination, Payload);
                var portable = update with { AssetName = "StatusLookout-win-x64.exe", DownloadUrl = update.DownloadUrl.Replace("Setup-", "") };
                try { await UpdateService.VerifyInstallerAsync(portable, destination); }
                catch (InvalidDataException) { return; }
                throw new Exception("Portable update was accepted as an installer.");
            });
        }
        finally { foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file); Directory.Delete(directory); }
    }
    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) });
    }
}
