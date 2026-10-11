using System.Text.Json.Serialization;
namespace StatusLookout.Core;

public enum ServiceStatus { Unknown, Operational, Maintenance, Degraded, Outage }
public enum AlertKind { Degraded, Outage, Recovery, Maintenance }

public sealed record ServiceDefinition(string Id, string Name, string Category, string Provider,
    string StatusUrl, string ApiUrl, string[]? Components = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name))
            throw new InvalidDataException("A service needs an ID and a name.");
        foreach (var url in new[] { StatusUrl, ApiUrl })
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
                throw new InvalidDataException("Service endpoints must be absolute HTTPS URLs without credentials.");
        if (!StatusParser.Providers.Contains(Provider))
            throw new InvalidDataException($"Unsupported provider: {Provider}");
    }
}
public sealed record ServiceSnapshot(string ServiceId, ServiceStatus Status, string Message, DateTimeOffset CheckedAt, string? IncidentKey = null);
public sealed record StatusTransition(string ServiceId, ServiceStatus? Previous, ServiceStatus Current,
    AlertKind Kind, string Message, DateTimeOffset At, string? IncidentKey = null);
public sealed record ActivityEntry(string ServiceId, string ServiceName, ServiceStatus Status,
    AlertKind Kind, string Message, DateTimeOffset At);
public sealed class Preferences
{
    public string Language { get; set; } = "en";
    public string Theme { get; set; } = "dark";
    public HashSet<string> SelectedServices { get; set; } = ["statuspage.GitHub", "statuspage.Cloudflare", "statuspage.Discord", "extra.OpenAI", "extra.Slack"];
    public HashSet<string> MutedServices { get; set; } = [];
    public Dictionary<string, DateTimeOffset> SnoozedUntil { get; set; } = [];
    public Dictionary<string, string[]> ComponentSelections { get; set; } = [];
    public bool GroupRelatedAlerts { get; set; } = true;
    public List<ServiceDefinition> CustomServices { get; set; } = [];
    public int PollSeconds { get; set; } = 60;
    public bool StartMinimized { get; set; }
    public bool NotifyDegraded { get; set; } = true;
    public bool NotifyOutage { get; set; } = true;
    public bool NotifyRecovery { get; set; } = true;
    public bool NotifyMaintenance { get; set; }
    public bool NotificationSound { get; set; } = true;
    public bool AlertOnFirstProblem { get; set; } = true;
    public void Normalize()
    {
        Language = Language == "fr" ? "fr" : "en";
        Theme = Theme is "light" or "system" ? Theme : "dark";
        PollSeconds = Math.Clamp(PollSeconds, 30, 900);
        SelectedServices ??= []; MutedServices ??= []; CustomServices ??= [];
        SnoozedUntil ??= []; ComponentSelections ??= [];
        ComponentSelections = ComponentSelections.Where(p => p.Value is not null).ToDictionary(p => p.Key, p => p.Value.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().ToArray());
        CustomServices = CustomServices.Where(s => { try { s.Validate(); return true; } catch { return false; } }).DistinctBy(s => s.Id).ToList();
    }
    public bool ShouldNotify(StatusTransition change) => !MutedServices.Contains(change.ServiceId)
        && (!SnoozedUntil.TryGetValue(change.ServiceId, out var until) || until <= DateTimeOffset.UtcNow) && change.Kind switch
    {
        AlertKind.Degraded => NotifyDegraded,
        AlertKind.Outage => NotifyOutage,
        AlertKind.Recovery => NotifyRecovery,
        AlertKind.Maintenance => NotifyMaintenance,
        _ => false
    };
}
public sealed class MonitorState
{
    public Dictionary<string, ServiceStatus> LastKnown { get; set; } = [];
    public Dictionary<string, string> LastIncidents { get; set; } = [];
    public List<ActivityEntry> History { get; set; } = [];
}
public static class ServiceCatalog
{
    public static IReadOnlyList<ServiceDefinition> Load()
    {
        using var stream = typeof(ServiceCatalog).Assembly.GetManifestResourceStream("StatusLookout.Core.Catalog.services.json")
            ?? throw new InvalidDataException("The built-in service catalog is missing.");
        var services = System.Text.Json.JsonSerializer.Deserialize<List<ServiceDefinition>>(stream, JsonStore.Options)
            ?? throw new InvalidDataException("The service catalog is empty.");
        foreach (var service in services) service.Validate();
        if (services.Select(s => s.Id).Distinct().Count() != services.Count) throw new InvalidDataException("Duplicate service IDs.");
        return services.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
