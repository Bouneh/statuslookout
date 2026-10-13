using System.Text.Json;
namespace StatusLookout.Core;

public static class PreferenceTransfer
{
    private sealed record Document(string Application, int Schema, Preferences Preferences);
    public static string Export(Preferences preferences)
    {
        var copy = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(preferences, JsonStore.Options), JsonStore.Options)!;
        copy.SnoozedUntil.Clear(); // Temporary pauses belong to this monitoring session, not another PC.
        copy.LastNotifiedUpdateVersion = null;
        return JsonSerializer.Serialize(new Document("StatusLookout", 1, copy), JsonStore.Options);
    }
    public static Preferences Import(string json, IEnumerable<ServiceDefinition> catalog)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 4 * 1024 * 1024) throw new InvalidDataException("The preferences file is too large.");
        var document = JsonSerializer.Deserialize<Document>(json, JsonStore.Options)
            ?? throw new InvalidDataException("The preferences file is empty.");
        if (document.Application != "StatusLookout" || document.Schema != 1 || document.Preferences is null)
            throw new InvalidDataException("Unsupported preferences file.");
        var preferences = document.Preferences;
        if (preferences.CustomServices is null || preferences.SelectedServices is null || preferences.MutedServices is null || preferences.ComponentSelections is null)
            throw new InvalidDataException("Incomplete preferences file.");
        foreach (var service in preferences.CustomServices)
        {
            if (service is null || !service.Id.StartsWith("custom.", StringComparison.Ordinal)) throw new InvalidDataException("Invalid custom service ID.");
            service.Validate();
        }
        if (preferences.CustomServices.Select(s => s.Id).Distinct().Count() != preferences.CustomServices.Count)
            throw new InvalidDataException("Duplicate custom services.");
        var ids = catalog.Concat(preferences.CustomServices).Select(s => s.Id).ToHashSet();
        if(preferences.MonitoringLists is { } lists)
        {
            foreach(var list in lists) {if(list is null)throw new InvalidDataException("Invalid monitoring list.");list.Validate();if(list.Services.Any(id=>!ids.Contains(id)))throw new InvalidDataException("Unknown service in monitoring list.");}
            if(lists.Select(list=>list.Id).Distinct().Count()!=lists.Count||!lists.Any(list=>list.Id==preferences.ActiveListId))throw new InvalidDataException("Invalid active monitoring list.");
        }
        if (preferences.SelectedServices.Concat(preferences.MutedServices).Concat(preferences.ComponentSelections.Keys).Any(id => !ids.Contains(id))
            || preferences.ComponentSelections.Values.Any(v => v is null || v.Any(string.IsNullOrWhiteSpace)))
            throw new InvalidDataException("Unknown service or invalid component selection.");
        preferences.Normalize(); preferences.SnoozedUntil.Clear();
        return preferences;
    }
}
