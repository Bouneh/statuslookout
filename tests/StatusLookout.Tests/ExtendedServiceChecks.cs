using System.Net;
using System.Text.Json;
using StatusLookout.Core;

internal static class ExtendedServiceChecks
{
    private const string Streamlabs = """{"status":{"indicator":"major","description":"Logitech incident"},"components":[{"id":"sl","name":"Streamlabs","group":true,"status":"operational"},{"id":"api","name":"API","group_id":"sl","status":"operational"},{"id":"chat","name":"Chatbot","group_id":"sl","status":"degraded_performance"},{"id":"other","name":"GHUB","status":"major_outage"}],"incidents":[]}""";
    private const string BetterStack = """{"data":{"type":"status_page"},"included":[{"id":"hub","type":"status_page_resource","attributes":{"public_name":"Hub","status":"operational"}},{"id":"spaces","type":"status_page_resource","attributes":{"public_name":"Spaces","status":"downtime"}}]}""";
    private static ServiceDefinition Definition(string provider, string[]? scope = null) => new("fixture", "Fixture", "Test", provider, "https://example.com/", "https://example.com/", scope);
    private static string Hetzner(string incidents) => """<script id="__NEXT_DATA__" type="application/json">{"props":{"pageProps":{"systems":[{"@id":"/systems/2","titleEn":"Cloud","systemState":"published","parent":null},{"@id":"/systems/3","titleEn":"Cloud Server","systemState":"published","parent":"/systems/2"},{"@id":"/systems/11","titleEn":"Storage Box","systemState":"published","parent":null}],"incidents":{"topNotification":INCIDENTS,"informationList":[],"maintenanceList":[]}}}}</script>""".Replace("INCIDENTS", incidents);
    private static string Incident(string phase, string type) => """[{"id":12,"system":"/systems/3","incidentType":"TYPE","incidentState":"PHASE","titleEn":"Cloud incident"}]""".Replace("TYPE", type).Replace("PHASE", phase);
    public static async Task Run(Action<string, Action> check, Func<string, Func<Task>, Task> checkAsync)
    {
        void Assert(bool condition) { if (!condition) throw new Exception("Extended service assertion failed."); }
        void Reject(Action action) { try { action(); } catch (Exception ex) when (ex is InvalidDataException or JsonException) { return; } throw new Exception("Malformed status was accepted."); }
        ServiceStatus Parse(string provider, string body, string[]? scope = null) => StatusParser.Parse(Definition(provider, scope), body).Status;
        check("Extended catalog includes ten providers and six grouped OVHcloud sources", () =>
        {
            var added = ServiceCatalog.Load().Where(s => s.Id.StartsWith("extended.")).ToArray();
            Assert(added.Length == 15 && added.Select(s => s.Family).Distinct().Count() == 10);
            Assert(added.Count(s => s.Family == "OVHcloud") == 6);
            Assert(!added.Any(s => s.Name.Contains("Steam") || s.Name.Contains("CS2")));
        });
        check("Provider group paths distinguish identically named regions", () =>
        {
            const string groups = """{"components":[{"id":"db","name":"Database","group":true},{"id":"storage","name":"Storage","group":true},{"id":"a","name":"Paris","group_id":"db"},{"id":"b","name":"Paris","group_id":"storage"}]}""";
            var names = ComponentCatalog.Parse("statuspage", groups).Select(c => c.Name).ToArray();
            Assert(names.Contains("Database · Paris") && names.Contains("Storage · Paris"));
            const string nested = """{"components":[{"id":"group","name":"Region","group":true,"components":[{"id":"child","name":"API"}]}]}""";
            Assert(ComponentCatalog.Parse("statuspage", nested).Single().Name == "API");
        });
        check("Streamlabs ignores unrelated Logitech incidents", () => Assert(Parse("streamlabs", Streamlabs, ["api"]) == ServiceStatus.Operational));
        check("Streamlabs default monitors all of its own components", () => Assert(Parse("streamlabs", Streamlabs) == ServiceStatus.Degraded));
        check("Streamlabs picker excludes unrelated Logitech products", () => Assert(ExtendedServiceProviders.GetComponents("streamlabs", Streamlabs).Select(c => c.Id).Order().SequenceEqual(new[] { "api", "chat" })));
        check("Streamlabs refuses scope outside its group or a missing group", () =>
        {
            Reject(() => Parse("streamlabs", Streamlabs, ["other"]));
            Reject(() => Parse("streamlabs", Streamlabs.Replace("\"name\":\"Streamlabs\"", "\"name\":\"Renamed\"")));
        });
        check("Better Stack JSON isolates selected Hugging Face products", () =>
        {
            Assert(Parse("betterstack-json", BetterStack) == ServiceStatus.Outage);
            Assert(Parse("betterstack-json", BetterStack, ["hub"]) == ServiceStatus.Operational);
            Assert(ExtendedServiceProviders.GetComponents("betterstack-json", BetterStack).Count == 2);
        });
        check("Unmonitored Better Stack resources remain unknown", () => Assert(Parse("betterstack-json", BetterStack.Replace("downtime", "not_monitored")) == ServiceStatus.Unknown));
        check("Better Stack schema changes and absent scopes fail closed", () =>
        {
            Reject(() => Parse("betterstack-json", BetterStack, ["absent"]));
            Reject(() => Parse("betterstack-json", BetterStack.Replace("downtime", "new_state")));
            Reject(() => Parse("betterstack-json", "{\"data\":{\"type\":\"status_page\"},\"included\":[]}"));
        });
        check("Hetzner parent selection includes descendant incidents", () =>
        {
            Assert(Parse("hetzner", Hetzner(Incident("investigating", "outage")), ["/systems/2"]) == ServiceStatus.Outage);
            Assert(Parse("hetzner", Hetzner(Incident("investigating", "outage")), ["/systems/11"]) == ServiceStatus.Operational);
        });
        check("Hetzner distinguishes active maintenance, planned work and resolved incidents", () =>
        {
            Assert(Parse("hetzner", Hetzner(Incident("in_progress", "maintenance"))) == ServiceStatus.Maintenance);
            Assert(Parse("hetzner", Hetzner(Incident("planned", "maintenance"))) == ServiceStatus.Operational);
            Assert(Parse("hetzner", Hetzner(Incident("resolved", "outage"))) == ServiceStatus.Operational);
            Assert(Parse("hetzner", Hetzner(Incident("monitoring", "warning"))) == ServiceStatus.Degraded);
            Assert(Parse("hetzner", Hetzner(Incident("identified", "other"))) == ServiceStatus.Operational);
        });
        check("Hetzner uses stable incident identity for grouping", () => Assert(StatusParser.Parse(Definition("hetzner"), Hetzner(Incident("investigating", "outage"))).IncidentKey == "[\"12\"]"));
        check("Hetzner picker and missing-system checks use the published catalog", () =>
        {
            Assert(ExtendedServiceProviders.GetComponents("hetzner", Hetzner("[]")).Count == 3);
            Reject(() => Parse("hetzner", Hetzner("[]"), ["/systems/missing"]));
            Reject(() => Parse("hetzner", "<html>Challenge</html>"));
            Reject(() => Parse("hetzner", Hetzner(Incident("mystery", "outage"))));
        });
        foreach (var provider in new[] { "hetzner", "betterstack-json", "streamlabs" })
            check(provider + " never accepts an empty payload as healthy", () => Reject(() => Parse(provider, "{}")));
        await checkAsync("Supabase discovers and checks components missing from its summary", async () =>
        {
            var service = ServiceCatalog.Load().Single(s => s.Id == "extended.Supabase");
            using var handler = new ComponentHandler(); using var http = new HttpClient(handler); var monitor = new StatusMonitor(http);
            var choices = await monitor.GetComponentsAsync(service); Assert(choices.Any(c => c.Id == "database"));
            var result = (await monitor.CheckAsync([service with { Components = ["database"] }])).Single();
            Assert(result.Status == ServiceStatus.Outage);
            handler.Fail = true;
            Assert((await monitor.CheckAsync([service with { Components = ["database"] }])).Single().Status == ServiceStatus.Unknown);
        });
    }
    public static async Task<int> AuditAsync(string destination)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        var monitor = new StatusMonitor(http);
        var definitions = ServiceCatalog.Load().Where(s => s.Id.StartsWith("extended.")).ToArray();
        var snapshots = await monitor.CheckAsync(definitions);
        var failed = snapshots.Any(s => s.Status == ServiceStatus.Unknown);
        foreach (var snapshot in snapshots) Console.WriteLine(snapshot.ServiceId + " | " + snapshot.Status + " | " + snapshot.Message);
        var discovery = new List<object>();
        foreach (var service in definitions)
        {
            try
            {
                var choices = await monitor.GetComponentsAsync(service);
                if (choices.Count == 0) throw new InvalidDataException("No selectable components.");
                var scoped = (await monitor.CheckAsync([service with { Components = [choices.Last().Id] }])).Single();
                if (scoped.Status == ServiceStatus.Unknown) throw new InvalidDataException(scoped.Message);
                discovery.Add(new { service.Id, Count = choices.Count, Selected = choices.Last(), scoped.Status });
                Console.WriteLine(service.Id + " | " + choices.Count + " components | scoped: " + scoped.Status);
            }
            catch (Exception ex) { failed = true; Console.WriteLine(service.Id + " | ERROR: " + ex.Message); }
        }
        File.WriteAllText(destination, JsonSerializer.Serialize(new { snapshots, discovery }, JsonStore.Options));
        return failed ? 1 : 0;
    }
    private sealed class ComponentHandler : HttpMessageHandler
    {
        public bool Fail { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var components = request.RequestUri!.AbsolutePath.EndsWith("components.json");
            const string summary = """{"status":{"indicator":"none","description":"Operational"},"components":[{"id":"branching","name":"Branching","status":"operational"}]}""";
            const string full = """{"components":[{"id":"branching","name":"Branching","status":"operational"},{"id":"database","name":"Database","status":"major_outage"}]}""";
            return Task.FromResult(new HttpResponseMessage(components && Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) { Content = new StringContent(components ? full : summary) });
        }
    }
}
