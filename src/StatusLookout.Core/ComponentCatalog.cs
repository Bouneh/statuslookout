using System.Text.Json;
namespace StatusLookout.Core;

public sealed record ProviderComponent(string Id, string Name);
public static class ComponentCatalog
{
    public static IReadOnlyList<ProviderComponent> Parse(string provider, string json)
    {
        using var document = JsonDocument.Parse(json);
        var items = new List<ProviderComponent>();
        void Read(JsonElement array)
        {
            if (array.ValueKind != JsonValueKind.Array) return;
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (item.TryGetProperty("components", out var nested)) { Read(nested); if (nested.ValueKind == JsonValueKind.Array && nested.EnumerateArray().Any(c => c.ValueKind == JsonValueKind.Object)) continue; }
                var group = item.TryGetProperty("group", out var g) && g.ValueKind == JsonValueKind.True;
                if (!group && item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                    && item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(id.GetString()) && !string.IsNullOrWhiteSpace(name.GetString()))
                    items.Add(new(id.GetString()!, name.GetString()!));
            }
        }
        if (provider is "statuspage" or "instatus" && document.RootElement.TryGetProperty("components", out var components))
        {
            Read(components);
            // Statuspage publishes flat children with group_id. Include their group path
            // so identically named regions in different products remain distinguishable.
            if (provider == "statuspage" && components.ValueKind == JsonValueKind.Array)
            {
                var all = components.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.Object && c.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    .DistinctBy(c => c.GetProperty("id").GetString()).ToDictionary(c => c.GetProperty("id").GetString()!);
                string PathFor(string id, HashSet<string> seen)
                {
                    if (!seen.Add(id) || !all.TryGetValue(id, out var entry)) return "";
                    var name = entry.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var path = entry.TryGetProperty("group_id", out var parent) && parent.ValueKind == JsonValueKind.String ? PathFor(parent.GetString()!, seen) : "";
                    return path.Length == 0 ? name : path + " · " + name;
                }
                items = items.Select(c => { var path = PathFor(c.Id, []); return path.Length == 0 ? c : c with { Name = path }; }).ToList();
            }
        }
        var unique = items.DistinctBy(c => c.Id).ToArray();
        var repeated = unique.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return unique.Select(c => repeated.Contains(c.Name) ? c with { Name = c.Name + " [" + c.Id + "]" } : c).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
