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
        if (provider is "statuspage" or "instatus" && document.RootElement.TryGetProperty("components", out var components)) Read(components);
        return items.DistinctBy(c => c.Id).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
