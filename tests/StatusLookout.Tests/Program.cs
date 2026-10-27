using System.Net;
using System.Text.Json;
using StatusLookout.Core;

var passed = 0;
var failed = 0;
void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}."); }
void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
void Throws(Action action) { try { action(); } catch (Exception ex) when (ex is InvalidDataException or JsonException) { return; } throw new Exception("Expected invalid data to be rejected."); }
void Check(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); } }
async Task CheckAsync(string name, Func<Task> action) { try { await action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); } }
var definition = new ServiceDefinition("test", "Test", "Developer services", "statuspage", "https://example.com", "https://example.com/api/v2/summary.json");
if (!args.Contains("--audit") && !args.Contains("--retry-audit") && !args.Contains("--aws-audit") && !args.Contains("--update-audit")) AdditionalProviderChecks.Run(Check);
if(args.Length==0)ListChecks.Run(Check);
if(args.Length==0)SoundChecks.Run(Check);
if(args.Length==0)await OpenAiComponentChecks.Run(Check,CheckAsync);
if(args.Contains("--openai-audit"))
{
    using var client=new HttpClient();var monitor=new StatusMonitor(client);var openai=ServiceCatalog.Load().Single(s=>s.Id=="extra.OpenAI");
    var choices=await monitor.GetComponentsAsync(openai);var responses=choices.Single(c=>c.Name=="Responses");var codex=choices.Single(c=>c.Name=="Codex Web");
    var results=await monitor.CheckAsync([openai with {Id="responses",Components=[responses.Id]},openai with {Id="codex",Components=[codex.Id]}]);
    foreach(var result in results)Console.WriteLine(result.ServiceId+" | "+result.Status+" | "+result.Message);
    if(results.Any(r=>r.Status==ServiceStatus.Unknown))return 1;Console.WriteLine($"OpenAI: {choices.Count} components; Responses and Codex scoped checks readable.");return 0;
}
var healthy = """{"status":{"indicator":"none","description":"All clear"},"components":[{"id":"api","name":"API","status":"operational"}],"incidents":[]}""";
var outage = """{"status":{"indicator":"major","description":"Major outage"},"components":[{"id":"api","name":"API","status":"major_outage"}],"incidents":[{"name":"API unavailable","status":"investigating","components":[{"id":"api"}]},{"name":"Old incident","status":"resolved"}]}""";

if (args.Contains("--extended-audit")) return await ExtendedServiceChecks.AuditAsync(Path.GetFullPath(".verification/extended-services-audit.json"));
if (args.Length == 0) await ExtendedServiceChecks.Run(Check, CheckAsync);
if (args.Length == 0) await InstallerUpdateChecks.Run(Check, CheckAsync);

if (args.Contains("--update-audit"))
{
    using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    var updates = new UpdateService(client);
    var release = await updates.CheckAsync(new(0, 0, 0), "win-x64") ?? throw new Exception("No public stable release found.");
    var directory = Path.Combine(Environment.CurrentDirectory, ".verification"); Directory.CreateDirectory(directory);
    var destination = Path.Combine(directory, "download-audit-" + release.Tag + ".exe");
    await updates.DownloadAsync(release, destination);
    Console.WriteLine("Verified official download: " + release.Tag + " | " + release.AssetName + " | " + new FileInfo(destination).Length + " bytes");
    return 0;
}
if (args.Contains("--audit"))
{
    using var client = new HttpClient();
    var results = await new StatusMonitor(client).CheckAsync(ServiceCatalog.Load());
    var directory = Path.Combine(Environment.CurrentDirectory, ".verification");
    Directory.CreateDirectory(directory);
    File.WriteAllText(Path.Combine(directory, "catalog-audit.json"), JsonSerializer.Serialize(results, JsonStore.Options));
    var services = ServiceCatalog.Load().ToDictionary(s => s.Id);
    foreach (var result in results.Where(s => s.Status == ServiceStatus.Unknown))
        Console.WriteLine(services[result.ServiceId].Name + " | " + services[result.ServiceId].ApiUrl + " | " + result.Message);
    Console.WriteLine($"Catalog audit: {results.Count(s => s.Status != ServiceStatus.Unknown)}/{results.Count} readable.");
    return 0;
}
if(args.Contains("--retry-audit"))
{
    var path=Path.Combine(Environment.CurrentDirectory,".verification","catalog-audit.json");
    var previous=JsonSerializer.Deserialize<List<ServiceSnapshot>>(File.ReadAllText(path),JsonStore.Options)!;
    var unknown=previous.Where(s=>s.Status==ServiceStatus.Unknown).Select(s=>s.ServiceId).ToHashSet();
    using var client=new HttpClient();var retry=await new StatusMonitor(client).CheckAsync(ServiceCatalog.Load().Where(s=>unknown.Contains(s.Id)));
    var merged=previous.Where(s=>!unknown.Contains(s.ServiceId)).Concat(retry).ToArray();File.WriteAllText(path,JsonSerializer.Serialize(merged,JsonStore.Options));
    foreach(var s in retry.Where(s=>s.Status==ServiceStatus.Unknown))Console.WriteLine(s.ServiceId+" | "+s.Message);
    Console.WriteLine($"Catalog audit: {merged.Count(s=>s.Status!=ServiceStatus.Unknown)}/{merged.Length} readable.");return 0;
}
Check("Catalog has unique IDs and valid HTTPS endpoints", () =>
{
    var services = ServiceCatalog.Load();
    True(services.Count > 350);
    foreach (var service in services) service.Validate();
    Equal(services.Count, services.Select(s => s.Id).Distinct().Count());
    foreach (var id in new Preferences().SelectedServices) True(services.Any(s => s.Id == id));
});
if (args.Contains("--aws-audit"))
{
    using var client = new HttpClient();
    var definitions = ServiceCatalog.Load().Where(s => s.Provider == "aws").ToArray();
    var snapshots = await new StatusMonitor(client).CheckAsync(definitions);
    var selected = definitions.Where(s => s.Id == "extra.AWS" || s.Id == "aws.region.eu-west-3" || s.Name.Contains("(EC2)") || s.Name.Contains("(S3)")).ToDictionary(s => s.Id);
    foreach (var snapshot in snapshots.Where(s => selected.ContainsKey(s.ServiceId))) Console.WriteLine(selected[snapshot.ServiceId].Name + " | " + snapshot.Status + " | " + snapshot.Message);
    Console.WriteLine($"AWS public feed: {snapshots.Count(s => s.Status != ServiceStatus.Unknown)}/{snapshots.Count} readable.");
    return snapshots.Any(s => s.Status == ServiceStatus.Unknown) ? 1 : 0;
}
Check("Appearance defaults to English and dark", () =>
{
    var preferences = new Preferences(); Equal("en", preferences.Language); Equal("dark", preferences.Theme);
    var legacy = JsonSerializer.Deserialize<Preferences>("{\"pollSeconds\":60}", JsonStore.Options)!;
    Equal("en", legacy.Language); Equal("dark", legacy.Theme);
});
Check("Language and theme survive serialization", () =>
{
    var preferences = new Preferences { Language = "fr", Theme = "light" };
    var restored = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(preferences, JsonStore.Options), JsonStore.Options)!;
    restored.Normalize(); Equal("fr", restored.Language); Equal("light", restored.Theme);
});
Check("Unsupported appearance settings fall back safely", () =>
{
    var preferences = new Preferences { Language = null!, Theme = "unknown" };
    preferences.Normalize(); Equal("en", preferences.Language); Equal("dark", preferences.Theme);
});
Check("Statuspage healthy", () => Equal(ServiceStatus.Operational, StatusParser.Parse(definition, healthy).Status));
Check("Statuspage outage includes only active incidents", () =>
{
    var result = StatusParser.Parse(definition, outage);
    Equal(ServiceStatus.Outage, result.Status); Equal("API unavailable", result.Message);
});
Check("Statuspage partial outage is degraded", () => Equal(ServiceStatus.Degraded, StatusParser.Parse(definition, """{"status":{"indicator":"minor","description":"Partial outage"}}""").Status));
Check("Statuspage maintenance", () => Equal(ServiceStatus.Maintenance, StatusParser.Parse(definition, """{"status":{"indicator":"maintenance"}}""").Status));
Check("Unrecognized indicator rejected", () => Throws(() => StatusParser.Parse(definition, """{"status":{"indicator":"surprise"}}""")));
Check("Wrong schema rejected", () => Throws(() => StatusParser.Parse(definition, """{"message":"rate limited"}""")));
Check("HTML challenge rejected", () => Throws(() => StatusParser.Parse(definition, "<html>challenge</html>")));
Check("Selected component overrides page aggregate", () => Equal(ServiceStatus.Operational, StatusParser.Parse(definition with { Components = ["api"] }, healthy.Replace("none", "major")).Status));
Check("Missing component rejected", () => Throws(() => StatusParser.Parse(definition with { Components = ["missing"] }, healthy)));
Check("Component outage", () => Equal(ServiceStatus.Outage, StatusParser.Parse(definition with { Components = ["api"] }, outage).Status));

var statusio = definition with { Provider = "statusio" };
foreach (var (code, expected) in new[] { (100, ServiceStatus.Operational), (200, ServiceStatus.Maintenance), (300, ServiceStatus.Degraded), (400, ServiceStatus.Degraded), (500, ServiceStatus.Outage), (600, ServiceStatus.Outage) })
    Check("Status.io code " + code, () => Equal(expected, StatusParser.Parse(statusio, JsonSerializer.Serialize(new { result = new { status_overall = new { status_code = code, status = "provider text" } } })).Status));
Check("Unknown Status.io code rejected", () => Throws(() => StatusParser.Parse(statusio, """{"result":{"status_overall":{"status_code":999}}}""")));
var instatus = definition with { Provider = "instatus" };
Check("Instatus up", () => Equal(ServiceStatus.Operational, StatusParser.Parse(instatus, """{"page":{"status":"UP"}}""").Status));
Check("Instatus issues", () => Equal(ServiceStatus.Degraded, StatusParser.Parse(instatus, """{"page":{"status":"HASISSUES"}}""").Status));
Check("Instatus component severity", () => Equal(ServiceStatus.Outage, StatusParser.Parse(instatus, """{"page":{"status":"HASISSUES"},"components":[{"status":"MAJOROUTAGE"}]}""").Status));

var google = definition with { Provider = "google", Components = ["Cloud Storage"] };
var googleActive = """[{"begin":"2026-10-10T12:00:00Z","end":null,"affected_products":[{"title":"Cloud Storage","id":"storage"}],"external_desc":"Storage requests failing","most_recent_update":{"status":"SERVICE_OUTAGE"},"status_impact":"SERVICE_OUTAGE"}]""";
Check("Google active product outage", () => Equal(ServiceStatus.Outage, StatusParser.Parse(google, googleActive).Status));
Check("Google unaffected product", () => Equal(ServiceStatus.Operational, StatusParser.Parse(google with { Components = ["Compute Engine"] }, googleActive).Status));
Check("Google aggregate includes all products", () => Equal(ServiceStatus.Outage, StatusParser.Parse(google with { Components = null }, googleActive).Status));
Check("Google ended incident ignored", () => Equal(ServiceStatus.Operational, StatusParser.Parse(google, googleActive.Replace("\"end\":null", "\"end\":\"2026-10-10T13:00:00Z\"")).Status));
Check("Google empty feed healthy", () => Equal(ServiceStatus.Operational, StatusParser.Parse(google, "[]").Status));
Check("Google schema not inferred healthy", () => Throws(() => StatusParser.Parse(google, """[{"unexpected":"schema"}]""")));
Check("Google pending resolution remains degraded", () => Equal(ServiceStatus.Degraded, StatusParser.Parse(google, googleActive.Replace("SERVICE_OUTAGE", "AVAILABLE")).Status));
var slack = definition with { Provider = "slack" };
Check("Slack normal", () => Equal(ServiceStatus.Operational, StatusParser.Parse(slack, """{"status":"ok","active_incidents":[]}""").Status));
Check("Slack outage", () => Equal(ServiceStatus.Outage, StatusParser.Parse(slack, """{"status":"active","active_incidents":[{"type":"outage","title":"Unavailable"}]}""").Status));
Check("Slack degraded", () => Equal(ServiceStatus.Degraded, StatusParser.Parse(slack, """{"status":"active","active_incidents":[{"type":"incident","title":"Slow requests"}]}""").Status));
Check("Slack maintenance", () => Equal(ServiceStatus.Maintenance, StatusParser.Parse(slack, """{"status":"active","active_incidents":[{"type":"maintenance","title":"Maintenance"}]}""").Status));
Check("Slack inconsistent active schema rejected", () => Throws(() => StatusParser.Parse(slack, """{"status":"active","active_incidents":[]}""")));

ServiceSnapshot Snapshot(ServiceStatus state) => new("test", state, state.ToString(), DateTimeOffset.UtcNow);
Check("No alert on first healthy check", () => True(new NotificationTracker().Observe(Snapshot(ServiceStatus.Operational)) is null));
Check("Initial outage alert enabled", () => Equal(AlertKind.Outage, new NotificationTracker().Observe(Snapshot(ServiceStatus.Outage))!.Kind));
Check("Initial outage alert disabled", () => True(new NotificationTracker().Observe(Snapshot(ServiceStatus.Outage), false) is null));
Check("Unknown status cannot create an outage alert", () => True(new NotificationTracker().Observe(Snapshot(ServiceStatus.Unknown)) is null));
Check("State transition and duplicate suppression", () =>
{
    var tracker = new NotificationTracker();
    tracker.Observe(Snapshot(ServiceStatus.Operational));
    Equal(AlertKind.Degraded, tracker.Observe(Snapshot(ServiceStatus.Degraded))!.Kind);
    True(tracker.Observe(Snapshot(ServiceStatus.Degraded)) is null);
    Equal(AlertKind.Outage, tracker.Observe(Snapshot(ServiceStatus.Outage))!.Kind);
});
Check("Recovery survives transport failure", () =>
{
    var tracker = new NotificationTracker();
    tracker.Observe(Snapshot(ServiceStatus.Outage));
    True(tracker.Observe(Snapshot(ServiceStatus.Unknown)) is null);
    Equal(ServiceStatus.Outage, tracker.LastKnown["test"]);
    Equal(AlertKind.Recovery, tracker.Observe(Snapshot(ServiceStatus.Operational))!.Kind);
    True(tracker.Observe(Snapshot(ServiceStatus.Operational)) is null);
});
Check("Network recovery from healthy does not fake recovery alert", () =>
{
    var tracker = new NotificationTracker(new() { ["test"] = ServiceStatus.Operational });
    tracker.Observe(Snapshot(ServiceStatus.Unknown));
    True(tracker.Observe(Snapshot(ServiceStatus.Operational)) is null);
});
Check("Persisted baseline produces recovery after restart", () => Equal(AlertKind.Recovery, new NotificationTracker(new() { ["test"] = ServiceStatus.Outage }).Observe(Snapshot(ServiceStatus.Operational))!.Kind));
Check("Mute overrides alert category", () =>
{
    var settings = new Preferences { MutedServices = ["test"] };
    True(!settings.ShouldNotify(new("test", ServiceStatus.Operational, ServiceStatus.Outage, AlertKind.Outage, "", DateTimeOffset.UtcNow)));
});
Check("Recovery can be independently disabled", () =>
{
    var settings = new Preferences { NotifyRecovery = false };
    True(!settings.ShouldNotify(new("test", ServiceStatus.Outage, ServiceStatus.Operational, AlertKind.Recovery, "", DateTimeOffset.UtcNow)));
});
Check("Polling interval constrained", () => { var p = new Preferences { PollSeconds = 1 }; p.Normalize(); Equal(30, p.PollSeconds); p.PollSeconds = 10000; p.Normalize(); Equal(900, p.PollSeconds); });
Check("HTTP endpoint cannot be added", () => Throws(() => (definition with { ApiUrl = "http://example.com" }).Validate()));
Check("Credential-bearing endpoint cannot be added", () => Throws(() => (definition with { ApiUrl = "https://user:password@example.com" }).Validate()));

var temporary = Path.Combine(Path.GetTempPath(), "StatusLookout.Tests." + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
try
{
    Check("Preferences round trip", () =>
    {
        var store = new JsonStore(temporary);
        var p = new Preferences { SelectedServices = ["one", "two"], PollSeconds = 90 };
        store.Save("preferences.json", p);
        var copy = store.Load<Preferences>("preferences.json");
        Equal(90, copy.PollSeconds); True(copy.SelectedServices.SetEquals(p.SelectedServices));
    });
    Check("Malformed user file is preserved", () =>
    {
        var path = Path.Combine(temporary, "broken.json");
        File.WriteAllText(path, "{broken");
        var store = new JsonStore(temporary); var p = store.Load<Preferences>("broken.json");
        True(store.Warning is not null); Equal("{broken", File.ReadAllText(path)); Equal(60, p.PollSeconds);
        var backup = Directory.EnumerateFiles(temporary, "broken.json.unreadable-*.json").Single();
        Equal("{broken", File.ReadAllText(backup));
        store.Save("broken.json", p);
        Equal("{broken", File.ReadAllText(backup));
    });
    Check("State history and baseline round trip", () =>
    {
        var store = new JsonStore(temporary);
        store.Save("state.json", new MonitorState { LastKnown = new() { ["test"] = ServiceStatus.Outage }, History = [new("test", "Test", ServiceStatus.Outage, AlertKind.Outage, "Unavailable", DateTimeOffset.UtcNow)] });
        var state = store.Load<MonitorState>("state.json");
        Equal(ServiceStatus.Outage, state.LastKnown["test"]); Equal(1, state.History.Count);
    });
}
finally { foreach (var path in Directory.EnumerateFiles(temporary)) File.Delete(path); Directory.Delete(temporary); }

await CheckAsync("Shared API endpoints fetched once", async () =>
{
    using var handler = new FakeHandler(healthy);
    using var client = new HttpClient(handler);
    var result = await new StatusMonitor(client).CheckAsync([definition, definition with { Id = "test2" }]);
    Equal(1, handler.RequestCount); Equal(2, result.Count); True(result.All(s => s.Status == ServiceStatus.Operational));
});
await CheckAsync("HTTP failure returns Unknown, never Outage", async () =>
{
    using var client = new HttpClient(new FakeHandler(healthy, HttpStatusCode.ServiceUnavailable));
    var result = await new StatusMonitor(client).CheckAsync([definition]);
    Equal(ServiceStatus.Unknown, result.Single().Status);
});
await CheckAsync("Invalid API response returns Unknown", async () =>
{
    using var client = new HttpClient(new FakeHandler("<html>challenge</html>"));
    Equal(ServiceStatus.Unknown, (await new StatusMonitor(client).CheckAsync([definition])).Single().Status);
});
await CheckAsync("Overlapping refreshes are serialized", async () =>
{
    using var handler = new FakeHandler(healthy, delay: 30);
    using var client = new HttpClient(handler);
    var monitor = new StatusMonitor(client);
    await Task.WhenAll(monitor.CheckAsync([definition]), monitor.CheckAsync([definition]));
    Equal(2, handler.RequestCount); Equal(1, handler.MaxConcurrent);
});
await CheckAsync("Caller cancellation propagates", async () =>
{
    using var client = new HttpClient(new FakeHandler(healthy, delay: 1000));
    using var cancellation = new CancellationTokenSource(20);
    try { await new StatusMonitor(client).CheckAsync([definition], cancellation.Token); throw new Exception("Cancellation was ignored."); }
    catch (OperationCanceledException) { }
});
byte[] ReleaseJson(string tag = "v0.4.0", bool prerelease = false, bool draft = false) => JsonSerializer.SerializeToUtf8Bytes(new
{
    tag_name = tag, draft, prerelease,
    assets = new[] { "win-x64", "win-x86", "win-arm64" }.Select(runtime => new
    {
        name = "StatusLookout-" + runtime + ".exe",
        browser_download_url = "https://github.com/Bouneh/statuslookout/releases/download/" + tag + "/StatusLookout-" + runtime + ".exe",
        digest = "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("verified executable"u8)), size = 19
    })
});
foreach (var runtime in new[] { "win-x64", "win-x86", "win-arm64" })
    Check("Updater selects " + runtime, () => Equal("StatusLookout-" + runtime + ".exe", UpdateService.ParseRelease(ReleaseJson(), new(0, 3, 0, 0), runtime)!.AssetName));
Check("Equal three and four component versions are current", () => True(UpdateService.ParseRelease(ReleaseJson("v0.3.0"), new(0, 3, 0, 0), "win-x64") is null));
Check("Older releases cannot downgrade the app", () => True(UpdateService.ParseRelease(ReleaseJson("v0.2.0"), new(0, 3, 0), "win-x64") is null));
Check("Prereleases and drafts are ignored", () =>
{
    True(UpdateService.ParseRelease(ReleaseJson(prerelease: true), new(0, 3, 0), "win-x64") is null);
    True(UpdateService.ParseRelease(ReleaseJson(draft: true), new(0, 3, 0), "win-x64") is null);
});
Check("Untrusted download URLs are rejected", () => Throws(() => UpdateService.ParseRelease(System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(ReleaseJson()).Replace("https://github.com/", "https://example.com/")), new(0, 3, 0), "win-x64")));
Check("Missing checksum is rejected", () => Throws(() => UpdateService.ParseRelease(System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(ReleaseJson()).Replace("sha256:", "sha1:")), new(0, 3, 0), "win-x64")));
Check("Unknown release schema fails as invalid data", () =>
{
    try { UpdateService.ParseRelease("{}"u8.ToArray(), new(0, 3, 0), "win-x64"); throw new Exception("Invalid schema was accepted."); }
    catch (InvalidDataException) { }
});
Check("Missing architecture cannot offer a wrong executable", () => Throws(() => UpdateService.ParseRelease(System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(ReleaseJson()).Replace("StatusLookout-win-arm64.exe", "Other.exe")), new(0, 3, 0), "win-arm64")));
await CheckAsync("Update API failures do not report current", async () =>
{
    using var client = new HttpClient(new FakeHandler("{}", HttpStatusCode.Forbidden));
    try { await new UpdateService(client).CheckAsync(new(0, 3, 0), "win-x64"); throw new Exception("HTTP failure was ignored."); }
    catch (HttpRequestException) { }
});
var downloadDirectory = Path.Combine(Path.GetTempPath(), "StatusLookout-updates-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(downloadDirectory);
try
{
    var update = UpdateService.ParseRelease(ReleaseJson(), new(0, 3, 0), "win-x64")!;
    await CheckAsync("Verified download is saved atomically", async () =>
    {
        using var client = new HttpClient(new FakeHandler("verified executable"));
        var destination = Path.Combine(downloadDirectory, "valid.exe");
        await new UpdateService(client).DownloadAsync(update, destination);
        Equal("verified executable", File.ReadAllText(destination));
        True(!Directory.EnumerateFiles(downloadDirectory, "*.partial").Any());
    });
    await CheckAsync("Checksum mismatch preserves existing file and removes partial", async () =>
    {
        using var client = new HttpClient(new FakeHandler("corrupt executable!"));
        var destination = Path.Combine(downloadDirectory, "original.exe"); File.WriteAllText(destination, "original");
        try { await new UpdateService(client).DownloadAsync(update, destination); throw new Exception("Corrupt download was accepted."); }
        catch (InvalidDataException) { }
        Equal("original", File.ReadAllText(destination));
        True(!Directory.EnumerateFiles(downloadDirectory, "*.partial").Any());
    });
    await CheckAsync("Incomplete download cannot leave an executable", async () =>
    {
        using var client = new HttpClient(new FakeHandler("short")); var destination = Path.Combine(downloadDirectory, "short.exe");
        try { await new UpdateService(client).DownloadAsync(update, destination); throw new Exception("Short download was accepted."); }
        catch (InvalidDataException) { }
        True(!File.Exists(destination)); True(!Directory.EnumerateFiles(downloadDirectory, "*.partial").Any());
    });
    await CheckAsync("Cancelled download leaves no executable", async () =>
    {
        using var client = new HttpClient(new FakeHandler("verified executable", delay: 1000)); using var cancellation = new CancellationTokenSource(20);
        var destination = Path.Combine(downloadDirectory, "cancelled.exe");
        try { await new UpdateService(client).DownloadAsync(update, destination, token: cancellation.Token); throw new Exception("Cancellation was ignored."); }
        catch (OperationCanceledException) { }
        True(!File.Exists(destination)); True(!Directory.EnumerateFiles(downloadDirectory, "*.partial").Any());
    });
}
finally { foreach (var path in Directory.EnumerateFiles(downloadDirectory)) File.Delete(path); Directory.Delete(downloadDirectory); }
FeatureChecks.Run(Check);
AwsChecks.Run(Check);
Check("Startup commands quote paths and launch in background", () =>
{
    var executable = Path.Combine(Path.GetTempPath(), "Folder with spaces", "StatusLookout.exe");
    Equal($"\"{executable}\" --background", StartupCommand.Create(executable));
});
Check("Startup registration follows a new portable executable", () =>
{
    var old = Path.Combine(Path.GetTempPath(), "StatusLookout-win-x64.exe");
    var current = Path.Combine(Path.GetTempPath(), "StatusLookout-0.5.1-win-x64.exe");
    True(StartupCommand.NeedsRepair(StartupCommand.Create(old), current));
    True(!StartupCommand.NeedsRepair(StartupCommand.Create(current), current));
});
Check("Disabled startup is not silently enabled by a path repair", () =>
{
    var executable = Path.Combine(Path.GetTempPath(), "StatusLookout.exe");
    True(!StartupCommand.NeedsRepair(null, executable)); True(!StartupCommand.NeedsRepair("", executable));
});
Check("Startup path casing does not cause repeated repairs", () =>
{
    var executable = Path.Combine(Path.GetTempPath(), "StatusLookout.exe");
    True(StartupCommand.Matches(StartupCommand.Create(executable).ToUpperInvariant(), executable));
});
Check("Invalid and oversized Windows startup commands are rejected", () =>
{
    Throws(() => StartupCommand.Create("relative.exe"));
    Throws(() => StartupCommand.Create(Path.Combine(Path.GetTempPath(), "bad\"name.exe")));
    Throws(() => StartupCommand.Create(Path.Combine(Path.GetTempPath(), new string('a', 270) + ".exe")));
});
await BridgeChecks.RunAsync(CheckAsync);
Console.WriteLine($"\n{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

sealed class FakeHandler(string body, HttpStatusCode code = HttpStatusCode.OK, int delay = 0) : HttpMessageHandler
{
    private int _requests, _active, _max;
    public int RequestCount => _requests;
    public int MaxConcurrent => _max;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Interlocked.Increment(ref _requests);
        var active = Interlocked.Increment(ref _active);
        _max = Math.Max(_max, active);
        try { if (delay > 0) await Task.Delay(delay, token); return new(code) { Content = new StringContent(body) }; }
        finally { Interlocked.Decrement(ref _active); }
    }
}
