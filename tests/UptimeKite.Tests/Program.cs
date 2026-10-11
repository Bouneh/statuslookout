using System.Net;
using System.Text.Json;
using UptimeKite.Core;

var passed = 0;
var failed = 0;
void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}."); }
void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
void Throws(Action action) { try { action(); } catch (Exception ex) when (ex is InvalidDataException or JsonException) { return; } throw new Exception("Expected invalid data to be rejected."); }
void Check(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); } }
async Task CheckAsync(string name, Func<Task> action) { try { await action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); } }
var definition = new ServiceDefinition("test", "Test", "Developer services", "statuspage", "https://example.com", "https://example.com/api/v2/summary.json");
var healthy = """{"status":{"indicator":"none","description":"All clear"},"components":[{"id":"api","name":"API","status":"operational"}],"incidents":[]}""";
var outage = """{"status":{"indicator":"major","description":"Major outage"},"components":[{"id":"api","name":"API","status":"major_outage"}],"incidents":[{"name":"API unavailable","status":"investigating","components":[{"id":"api"}]},{"name":"Old incident","status":"resolved"}]}""";

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
Check("Catalog has unique IDs and valid HTTPS endpoints", () =>
{
    var services = ServiceCatalog.Load();
    True(services.Count > 350);
    foreach (var service in services) service.Validate();
    Equal(services.Count, services.Select(s => s.Id).Distinct().Count());
    foreach (var id in new Preferences().SelectedServices) True(services.Any(s => s.Id == id));
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

var temporary = Path.Combine(Path.GetTempPath(), "UptimeKite.Tests." + Guid.NewGuid().ToString("N"));
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
