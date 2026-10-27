using System.Text.Json;
using HtmlAgilityPack;

namespace StatusLookout.Core;

/// <summary>Adapters for additional published provider status feeds.</summary>
public static class ExtendedServiceProviders
{
    public static readonly HashSet<string> Providers = ["hetzner", "betterstack-json", "streamlabs"];
    private static JsonElement Field(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field)
        ? field : throw new InvalidDataException("Missing status field: " + name);
    private static string Text(JsonElement value, string name) => Field(value, name).GetString() ?? throw new InvalidDataException("Missing text: " + name);
    private static JsonElement[] Items(JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : throw new InvalidDataException("Expected a status array.");
    private static JsonDocument HetznerDocument(string body)
    {
        var html = new HtmlDocument(); html.LoadHtml(body);
        var data = html.DocumentNode.SelectSingleNode("//script[@id='__NEXT_DATA__']") ?? throw new InvalidDataException("Missing Hetzner status data.");
        return JsonDocument.Parse(data.InnerText);
    }
    private static JsonElement HetznerPage(JsonElement root) => Field(Field(root, "props"), "pageProps");
    private static JsonElement[] HetznerSystems(JsonElement page) => Items(Field(page, "systems"));
    private static JsonElement[] Resources(JsonElement root) => Items(Field(root, "included")).Where(v => Text(v, "type") == "status_page_resource").ToArray();
    private static ServiceStatus BetterStackState(string value) => value switch
    {
        "operational" => ServiceStatus.Operational, "maintenance" => ServiceStatus.Maintenance,
        "degraded" => ServiceStatus.Degraded, "downtime" => ServiceStatus.Outage,
        "not_monitored" => ServiceStatus.Unknown, _ => throw new InvalidDataException("Unknown Better Stack resource state.")
    };
    private static string[] StreamlabsIds(JsonElement root)
    {
        var all = Items(Field(root, "components"));
        var group = all.SingleOrDefault(c => Text(c, "name") == "Streamlabs" && c.TryGetProperty("group", out var flag) && flag.ValueKind == JsonValueKind.True);
        if (group.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("Missing Streamlabs component group.");
        var id = Text(group, "id");
        var ids = all.Where(c => c.TryGetProperty("group_id", out var parent) && parent.GetString() == id).Select(c => Text(c, "id")).ToArray();
        return ids.Length > 0 ? ids : throw new InvalidDataException("Missing Streamlabs components.");
    }
    public static IReadOnlyList<ProviderComponent> GetComponents(string provider, string body)
    {
        using var document = provider == "hetzner" ? HetznerDocument(body) : JsonDocument.Parse(body);
        var root = document.RootElement;
        IEnumerable<ProviderComponent> components = provider switch
        {
            "hetzner" => HetznerSystems(HetznerPage(root)).Where(s => Text(s, "systemState") == "published").Select(s => new ProviderComponent(Text(s, "@id"), Text(s, "titleEn"))),
            "betterstack-json" => Resources(root).Select(r => new ProviderComponent(Text(r, "id"), Text(Field(r, "attributes"), "public_name"))),
            "streamlabs" => Items(Field(root, "components")).Where(c => StreamlabsIds(root).Contains(Text(c, "id"))).Select(c => new ProviderComponent(Text(c, "id"), Text(c, "name"))),
            _ => []
        };
        return components.DistinctBy(c => c.Id).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static ServiceSnapshot Parse(ServiceDefinition service, string body, DateTimeOffset now)
    {
        using var document = service.Provider == "hetzner" ? HetznerDocument(body) : JsonDocument.Parse(body);
        var root = document.RootElement;
        var selected = service.Components ?? [];
        if (service.Provider == "streamlabs")
        {
            var allowed = StreamlabsIds(root);
            if (selected.Any(id => !allowed.Contains(id))) throw new InvalidDataException("Requested component is outside Streamlabs.");
            return StatusParser.Parse(service with { Provider = "statuspage", Components = selected.Length > 0 ? selected : allowed }, body, now);
        }
        if (service.Provider == "betterstack-json")
        {
            if (Text(Field(root, "data"), "type") != "status_page") throw new InvalidDataException("Missing Better Stack status page.");
            var resources = Resources(root);
            if (resources.Length == 0 || selected.Any(id => !resources.Any(r => Text(r, "id") == id))) throw new InvalidDataException("Missing Better Stack resources.");
            var states = resources.Where(r => selected.Length == 0 || selected.Contains(Text(r, "id"))).Select(r => BetterStackState(Text(Field(r, "attributes"), "status"))).ToArray();
            var state = states.Contains(ServiceStatus.Unknown) ? ServiceStatus.Unknown : states.Max();
            return new(service.Id, state, StatusParser.DefaultMessage(state), now);
        }
        var page = HetznerPage(root);
        var systems = HetznerSystems(page);
        if (systems.Length == 0 || selected.Any(id => !systems.Any(s => Text(s, "@id") == id))) throw new InvalidDataException("Missing Hetzner system.");
        var scope = selected.ToHashSet(StringComparer.Ordinal);
        // Selecting a parent includes its descendants, including affected datacenters.
        bool added;
        do
        {
            added = false;
            foreach (var system in systems)
                if (system.TryGetProperty("parent", out var parent) && parent.ValueKind == JsonValueKind.String && scope.Contains(parent.GetString()!) && scope.Add(Text(system, "@id"))) added = true;
        } while (added);
        var lists = Field(page, "incidents");
        var active = new List<JsonElement>();
        foreach (var key in new[] { "topNotification", "informationList", "maintenanceList" })
            foreach (var incident in Items(Field(lists, key)))
            {
                if (selected.Length > 0 && !scope.Contains(Text(incident, "system"))) continue;
                var phase = Text(incident, "incidentState");
                if (phase is "resolved" or "post_mortem" or "planned" or "scheduled") continue;
                if (phase is not ("identified" or "in_progress" or "investigating" or "monitoring" or "update" or "verifying" or "watching")) throw new InvalidDataException("Unknown Hetzner incident phase.");
                if (Text(incident, "incidentType") != "other") active.Add(incident);
            }
        active = active.DistinctBy(i => Field(i, "id").ToString()).ToList();
        var status = active.Select(i => Text(i, "incidentType") switch
        {
            "maintenance" => ServiceStatus.Maintenance, "warning" => ServiceStatus.Degraded,
            "outage" => ServiceStatus.Outage, _ => throw new InvalidDataException("Unknown Hetzner incident type.")
        }).DefaultIfEmpty(ServiceStatus.Operational).Max();
        var message = active.Count > 0 ? string.Join(" · ", active.Take(3).Select(i => Text(i, "titleEn"))) : "No active incidents reported";
        return new(service.Id, status, message, now, active.Count == 0 ? null : JsonSerializer.Serialize(active.Select(i => Field(i, "id").ToString()).Order()));
    }
}
