using System.Text.Json;
namespace StatusLookout.Core;

/// <summary>Parses published status data; HTTP success alone is never treated as healthy.</summary>
public static class StatusParser
{
    public static readonly HashSet<string> Providers = ["statuspage", "instatus", "statusio", "google", "slack"];
    public static ServiceSnapshot Parse(ServiceDefinition service, string json, DateTimeOffset? now = null)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var (status, message) = service.Provider switch
        {
            "statuspage" => Statuspage(root, service.Components),
            "instatus" => Instatus(root, service.Components),
            "statusio" => Statusio(root),
            "google" => Google(root, service.Components),
            "slack" => Slack(root),
            _ => throw new InvalidDataException("Unsupported status provider.")
        };
        if (status == ServiceStatus.Unknown) throw new InvalidDataException("The provider returned an unrecognized status.");
        return new(service.Id, status, string.IsNullOrWhiteSpace(message) ? DefaultMessage(status) : message, now ?? DateTimeOffset.UtcNow,
            status == ServiceStatus.Operational ? null : IncidentIdentity(service, root));
    }
    public static string DefaultMessage(ServiceStatus status) => status switch
    {
        ServiceStatus.Operational => "All systems operational",
        ServiceStatus.Maintenance => "Scheduled maintenance in progress",
        ServiceStatus.Degraded => "Degraded performance reported",
        ServiceStatus.Outage => "Service outage reported",
        _ => "Unable to check this status"
    };
    private static string Text(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static IEnumerable<JsonElement> Array(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [];
    private static JsonElement Required(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) ? value : throw new InvalidDataException($"Missing provider field: {key}");
    private static ServiceStatus Map(string value) => value.ToLowerInvariant() switch
    {
        "none" or "operational" or "up" or "ok" or "available" => ServiceStatus.Operational,
        "maintenance" or "under_maintenance" or "undermaintenance" => ServiceStatus.Maintenance,
        "minor" or "degraded_performance" or "degradedperformance" or "partial_outage" or "partialoutage" or "minoroutage" or "hasissues" or "service_disruption" => ServiceStatus.Degraded,
        "major" or "critical" or "major_outage" or "majoroutage" or "full_outage" or "service_outage" or "outage" => ServiceStatus.Outage,
        _ => ServiceStatus.Unknown
    };
    private static (ServiceStatus, string) Statuspage(JsonElement root, string[]? componentIds)
    {
        var overall = Required(root, "status");
        var status = Map(Text(overall, "indicator"));
        var incidents = Array(root, "incidents").Where(i => Text(i, "status") is not ("resolved" or "completed" or "scheduled")).ToArray();
        if (componentIds is { Length: > 0 })
        {
            var matches = Array(root, "components").Where(c => componentIds.Contains(Text(c, "id")) || componentIds.Contains(Text(c, "name"))).ToArray();
            if (matches.Length != componentIds.Length) throw new InvalidDataException("One or more requested components are missing.");
            var states = matches.Select(c => Map(Text(c, "status"))).ToArray();
            if (states.Contains(ServiceStatus.Unknown)) throw new InvalidDataException("Unrecognized component status.");
            status = states.Max();
            incidents = incidents.Where(i => Array(i, "components").Any(c => componentIds.Contains(Text(c, "id")) || componentIds.Contains(Text(c, "name")))).ToArray();
        }
        var names = incidents.Select(i => Text(i, "name")).Where(n => n.Length > 0).Take(3).ToArray();
        return (status, names.Length > 0 ? string.Join(" · ", names) : componentIds is { Length: > 0 } ? DefaultMessage(status) : Text(overall, "description"));
    }
    private static IEnumerable<JsonElement> InstatusComponents(JsonElement root)
    {
        foreach (var component in Array(root, "components"))
        {
            if (Array(component, "components").Any())
            { foreach (var child in InstatusComponents(component)) yield return child; }
            else yield return component;
        }
    }
    private static (ServiceStatus, string) Instatus(JsonElement root, string[]? selected)
    {
        var page = Required(root, "page");
        var state = Map(Text(page, "status"));
        var components = InstatusComponents(root).ToArray();
        if (selected is { Length: > 0 })
        {
            if (selected.Any(id => !components.Any(c => Text(c, "id") == id || Text(c, "name") == id)))
                throw new InvalidDataException("One or more requested components are missing.");
            components = components.Where(c => selected.Contains(Text(c, "id")) || selected.Contains(Text(c, "name"))).ToArray();
        }
        var componentStates = components.Select(c => Map(Text(c, "status"))).ToArray();
        if (componentStates.Length > 0)
        {
            if (componentStates.Contains(ServiceStatus.Unknown)) throw new InvalidDataException("Unrecognized Instatus component state.");
            state = componentStates.Max();
        }
        return (state, DefaultMessage(state));
    }
    private static string? IncidentIdentity(ServiceDefinition service, JsonElement root)
    {
        var incidents = service.Provider == "google" ? root.EnumerateArray().ToArray().AsEnumerable()
            : Array(root, service.Provider == "slack" ? "active_incidents" : "incidents");
        incidents = incidents.Where(i => service.Provider == "google"
            ? Text(i, "end").Length == 0
            : Text(i, "status").ToLowerInvariant() is not ("resolved" or "completed" or "scheduled"));
        if (service.Components is { Length: > 0 } selected)
            incidents = incidents.Where(i => service.Provider == "google"
                ? Array(i, "affected_products").Any(p => selected.Contains(Text(p, "id"), StringComparer.OrdinalIgnoreCase) || selected.Contains(Text(p, "title"), StringComparer.OrdinalIgnoreCase))
                    || selected.Contains(Text(i, "service_name"), StringComparer.OrdinalIgnoreCase) || selected.Contains(Text(i, "service_key"), StringComparer.OrdinalIgnoreCase)
                : Array(i, "components").Any(c => c.ValueKind == JsonValueKind.String ? selected.Contains(c.GetString()!) : selected.Contains(Text(c, "id")) || selected.Contains(Text(c, "name"))));
        var ids = incidents.Select(i => Text(i, "id")).Where(id => id.Length > 0).Distinct().Order(StringComparer.Ordinal).ToArray();
        return ids.Length == 0 ? null : JsonSerializer.Serialize(ids);
    }
    private static (ServiceStatus, string) Statusio(JsonElement root)
    {
        var overall = Required(Required(root, "result"), "status_overall");
        var code = Required(overall, "status_code").GetInt32();
        var status = code switch { 100 => ServiceStatus.Operational, 200 => ServiceStatus.Maintenance, 300 or 400 => ServiceStatus.Degraded, 500 or 600 => ServiceStatus.Outage, _ => ServiceStatus.Unknown };
        return (status, Text(overall, "status"));
    }
    private static (ServiceStatus, string) Google(JsonElement root, string[]? products)
    {
        if (root.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Expected a Google incidents array.");
        var active = new List<JsonElement>();
        foreach (var incident in root.EnumerateArray())
        {
            Required(incident, "begin");
            var end = Required(incident, "end");
            if (end.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(end.GetString())) continue;
            if (end.ValueKind is not (JsonValueKind.Null or JsonValueKind.String)) throw new InvalidDataException("Invalid incident end time.");
            if (products is { Length: > 0 })
            {
                var affected = Array(incident, "affected_products").SelectMany(p => new[] { Text(p, "title"), Text(p, "id") })
                    .Append(Text(incident, "service_name")).Append(Text(incident, "service_key"));
                if (!affected.Any(p => products.Contains(p, StringComparer.OrdinalIgnoreCase))) continue;
            }
            active.Add(incident);
        }
        if (active.Count == 0) return (ServiceStatus.Operational, "No active incidents reported");
        var states = active.Select(i =>
        {
            var update = Required(i, "most_recent_update");
            var state = Map(Text(update, "status"));
            if (state == ServiceStatus.Unknown) state = Map(Text(i, "status_impact"));
            // An unresolved incident with an AVAILABLE update may be awaiting final resolution.
            return state is ServiceStatus.Operational or ServiceStatus.Unknown ? ServiceStatus.Degraded : state;
        }).ToArray();
        return (states.Max(), string.Join(" · ", active.Select(i => Text(i, "external_desc")).Take(3)));
    }
    private static (ServiceStatus, string) Slack(JsonElement root)
    {
        var value = Text(root, "status");
        if (value == "ok") return (ServiceStatus.Operational, "Slack is up and running");
        if (value != "active") throw new InvalidDataException("Unrecognized Slack status.");
        var incidents = Array(root, "active_incidents").ToArray();
        if (incidents.Length == 0) throw new InvalidDataException("Slack reports active issues without incident data.");
        var states = incidents.Select(i => Text(i, "type") switch
        {
            "outage" => ServiceStatus.Outage,
            "maintenance" => ServiceStatus.Maintenance,
            _ => ServiceStatus.Degraded
        });
        return (states.Max(), string.Join(" · ", incidents.Select(i => Text(i, "title")).Take(3)));
    }
}
