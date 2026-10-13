using System.Text.Json;
namespace StatusLookout.Core;

public static class AwsStatus
{
    private static string Text(JsonElement item, string key) => item.TryGetProperty(key, out var value) ? value.ToString() : "";
    public static (ServiceStatus Status, string Message, string? Identity) Parse(JsonElement root, string[]? selection)
    {
        if (root.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Expected the AWS public events array.");
        var products = (selection ?? []).Where(s => s.StartsWith("product:")).Select(s => s[8..]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var regions = (selection ?? []).Where(s => s.StartsWith("region:")).Select(s => s[7..]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var services = (selection ?? []).Where(s => s.StartsWith("service:")).Select(s => s[8..]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selection?.Any(s => !s.StartsWith("product:") && !s.StartsWith("region:") && !s.StartsWith("service:")) == true) throw new InvalidDataException("Invalid AWS scope.");
        bool Matches(string id, string name) => (products.Count == 0 && services.Count == 0 || products.Contains(name) || services.Contains(id))
            && (regions.Count == 0 || regions.Any(region => id.EndsWith("-" + region, StringComparison.OrdinalIgnoreCase)));
        var affected = new List<JsonElement>();
        foreach (var incident in root.EnumerateArray())
        {
            if (incident.ValueKind != JsonValueKind.Object || !incident.TryGetProperty("status", out _)
                || !incident.TryGetProperty("service", out _) || !incident.TryGetProperty("summary", out _)
                || !incident.TryGetProperty("impacted_services", out var impacts) || impacts.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Unrecognized AWS event data.");
            var status = Text(incident, "status");
            if (status == "0") continue;
            if (status is not ("1" or "2" or "3" or "4")) throw new InvalidDataException("Unrecognized AWS event status.");
            var matches = Matches(Text(incident, "service"), Text(incident, "service_name"));
            foreach (var impact in impacts.EnumerateObject())
            {
                var current = Text(impact.Value, "current");
                if (current == "0") continue;
                if (current is not ("1" or "2" or "3" or "4")) throw new InvalidDataException("Unrecognized AWS affected service status.");
                matches |= Matches(impact.Name, Text(impact.Value, "service_name"));
            }
            if (matches) affected.Add(incident);
        }
        if (affected.Count == 0) return (ServiceStatus.Operational, "No active AWS public events in this scope", null);
        var ids = affected.Select(i => Text(i, "arn")).Where(s => s.Length > 0).Distinct().Order().ToArray();
        // This public feed does not provide a documented, reliable severity mapping. Report confirmed issues as degraded.
        return (ServiceStatus.Degraded, string.Join(" · ", affected.Take(3).Select(i => Text(i, "summary") + " (" + Text(i, "region_name") + ")")),
            ids.Length == 0 ? null : JsonSerializer.Serialize(ids));
    }
}
