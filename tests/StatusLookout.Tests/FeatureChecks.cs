using System.Text.Json;
using StatusLookout.Core;

static class FeatureChecks
{
    public static void Run(Action<string, Action> check)
    {
        void Assert(bool value) { if (!value) throw new Exception("Feature assertion failed."); }
        void Reject(Action action) { try { action(); } catch (Exception ex) when (ex is InvalidDataException or JsonException) { return; } throw new Exception("Invalid preferences accepted."); }
        var service = new ServiceDefinition("a", "A", "Test", "statuspage", "https://example.com", "https://example.com/api/v2/summary.json");
        var b = service with { Id = "b", Name = "B" };
        var now = DateTimeOffset.UtcNow;
        StatusTransition Change(string id, string? incident, AlertKind kind = AlertKind.Outage) => new(id, ServiceStatus.Operational, ServiceStatus.Outage, kind, "Outage", now, incident);
        PendingAlert[] Pair(string? first = "incident", string? second = "incident") => [new(service, Change("a", first)), new(b, Change("b", second))];
        check("Related incidents share one notification", () => Assert(AlertGrouping.Group(Pair(), true).Single().Length == 2));
        check("Unrelated incident IDs remain separate", () => Assert(AlertGrouping.Group(Pair("one", "two"), true).Count == 2));
        check("Unknown incident identities remain separate", () => Assert(AlertGrouping.Group(Pair(null, null), true).Count == 2));
        check("Grouping can be disabled", () => Assert(AlertGrouping.Group(Pair(), false).Count == 2));
        check("Grouping separates providers and alert kinds", () =>
        {
            Assert(AlertGrouping.Group([new(service, Change("a", "same")), new(b with { ApiUrl = "https://other.com/api" }, Change("b", "same"))], true).Count == 2);
            Assert(AlertGrouping.Group([new(service, Change("a", "same")), new(b, Change("b", "same", AlertKind.Degraded))], true).Count == 2);
        });
        check("Recovery identity survives failure and restart", () =>
        {
            var tracker = new NotificationTracker(); tracker.Observe(new("a", ServiceStatus.Outage, "", now, "shared"));
            tracker.Observe(new("a", ServiceStatus.Unknown, "", now));
            var restarted = new NotificationTracker(tracker.LastKnown, tracker.LastIncidents);
            Assert(restarted.Observe(new("a", ServiceStatus.Operational, "", now))!.IncidentKey == "shared");
            Assert(!restarted.LastIncidents.ContainsKey("a"));
        });
        check("Active snooze filters alert but preserves monitoring baseline", () =>
        {
            var preferences = new Preferences { SnoozedUntil = new() { ["a"] = now.AddMinutes(30) } };
            var tracker = new NotificationTracker(); var change = tracker.Observe(new("a", ServiceStatus.Outage, "", now))!;
            Assert(!preferences.ShouldNotify(change)); Assert(tracker.LastKnown["a"] == ServiceStatus.Outage);
            Assert(tracker.Observe(new("a", ServiceStatus.Outage, "", now)) is null);
        });
        check("Expired snooze permits future transitions", () => Assert(new Preferences { SnoozedUntil = new() { ["a"] = now.AddSeconds(-1) } }.ShouldNotify(Change("a", null))));
        check("Permanent mute remains independent of expired snooze", () => Assert(!new Preferences { MutedServices = ["a"], SnoozedUntil = new() { ["a"] = now.AddSeconds(-1) } }.ShouldNotify(Change("a", null))));
        check("Snooze and system theme persist across restart", () =>
        {
            var preferences = new Preferences { Theme = "system", SnoozedUntil = new() { ["a"] = now.AddHours(1) } };
            var restored = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(preferences, JsonStore.Options), JsonStore.Options)!;
            restored.Normalize(); Assert(restored.Theme == "system" && restored.SnoozedUntil["a"] > now);
        });
        var summary = """{"status":{"indicator":"major"},"components":[{"id":"api","name":"API","status":"major_outage"},{"id":"web","name":"Website","status":"operational"}],"incidents":[{"id":"one","name":"API problem","status":"investigating","components":[{"id":"api"}]},{"id":"old","status":"resolved","components":[{"id":"api"}]}]}""";
        check("Statuspage incident identity follows selected components", () =>
        {
            var api = StatusParser.Parse(service with { Components = ["api"] }, summary);
            var web = StatusParser.Parse(service with { Components = ["web"] }, summary);
            Assert(api.IncidentKey == "[\"one\"]" && web.IncidentKey is null && web.Status == ServiceStatus.Operational);
            Assert(web.Message == "All systems operational");
        });
        check("Component picker excludes group headings", () =>
        {
            var values = ComponentCatalog.Parse("statuspage", """{"components":[{"id":"group","name":"Region","group":true},{"id":"api","name":"API","group":false}]}""");
            Assert(values.Count == 1 && values[0].Id == "api");
        });
        check("Statuspage group lists can contain child IDs", () =>
        {
            var values = ComponentCatalog.Parse("statuspage", """{"components":[{"id":"group","name":"Region","group":true,"components":["api"]},{"id":"api","name":"API","group":false}]}""");
            Assert(values.Count == 1 && values[0].Id == "api");
        });
        var nested = """{"page":{"status":"HASISSUES"},"components":[{"id":"group","name":"Europe","components":[{"id":"api","name":"API","status":"MAJOROUTAGE"},{"id":"web","name":"Website","status":"UP"}]}],"incidents":[{"id":"outage","status":"INVESTIGATING","components":[{"id":"api"}]}]}""";
        check("Nested Instatus choices and selection affect parsed status", () =>
        {
            Assert(ComponentCatalog.Parse("instatus", nested).Count == 2);
            Assert(StatusParser.Parse(service with { Provider = "instatus", Components = ["web"] }, nested).Status == ServiceStatus.Operational);
            Assert(StatusParser.Parse(service with { Provider = "instatus", Components = ["api"] }, nested).IncidentKey == "[\"outage\"]");
        });
        check("Missing Instatus selection cannot look healthy", () => Reject(() => StatusParser.Parse(service with { Provider = "instatus", Components = ["missing"] }, nested)));
        check("Google shared incident produces shared identity", () =>
        {
            var json = """[{"id":"google-event","begin":"2026-01-01","end":null,"affected_products":[{"id":"a","title":"A"},{"id":"b","title":"B"}],"most_recent_update":{"status":"SERVICE_OUTAGE"}}]""";
            Assert(StatusParser.Parse(service with { Provider = "google", Components = ["a"] }, json).IncidentKey == StatusParser.Parse(b with { Provider = "google", Components = ["b"] }, json).IncidentKey);
        });
        check("Transfer preserves custom services and component scope without snoozes", () =>
        {
            var custom = service with { Id = "custom.mailjet" };
            var source = new Preferences { SelectedServices = [custom.Id], CustomServices = [custom], Language = "fr", Theme = "system", GroupRelatedAlerts = false, ComponentSelections = new() { [custom.Id] = ["api"] }, SnoozedUntil = new() { [custom.Id] = now.AddHours(1) } };
            var restored = PreferenceTransfer.Import(PreferenceTransfer.Export(source), [service]);
            Assert(restored.CustomServices.Count == 1 && restored.SelectedServices.Contains(custom.Id));
            Assert(restored.Theme == "system" && restored.Language == "fr" && !restored.GroupRelatedAlerts);
            Assert(restored.ComponentSelections[custom.Id].Single() == "api" && restored.SnoozedUntil.Count == 0);
            Assert(source.SnoozedUntil.Count == 1);
        });
        check("Import rejects other apps and unknown services", () =>
        {
            var source = new Preferences { SelectedServices = ["a"] }; var json = PreferenceTransfer.Export(source);
            Reject(() => PreferenceTransfer.Import(json.Replace("StatusLookout", "OtherApp"), [service]));
            Reject(() => PreferenceTransfer.Import(json, []));
        });
        check("Import rejects invalid custom endpoints and preserves original preferences", () =>
        {
            var custom = service with { Id = "custom.test" }; var source = new Preferences { SelectedServices = [custom.Id], CustomServices = [custom] };
            Reject(() => PreferenceTransfer.Import(PreferenceTransfer.Export(source).Replace("https://", "http://"), []));
            Assert(source.CustomServices[0].StatusUrl.StartsWith("https://"));
        });
        check("Empty component override survives transfer as entire page", () =>
        {
            var source = new Preferences { SelectedServices = ["a"], ComponentSelections = new() { ["a"] = [] } };
            Assert(PreferenceTransfer.Import(PreferenceTransfer.Export(source), [service]).ComponentSelections["a"].Length == 0);
        });
    }
}
