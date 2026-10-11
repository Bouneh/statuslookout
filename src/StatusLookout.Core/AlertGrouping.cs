namespace StatusLookout.Core;

public sealed record PendingAlert(ServiceDefinition Service, StatusTransition Change);
public static class AlertGrouping
{
    // Only identical incident sets on the same API are related. Unknown identities stay separate.
    public static IReadOnlyList<PendingAlert[]> Group(IEnumerable<PendingAlert> alerts, bool enabled) => alerts
        .GroupBy(a => enabled && !string.IsNullOrEmpty(a.Change.IncidentKey)
            ? (a.Service.ApiUrl, a.Change.Kind, a.Change.IncidentKey)
            : (a.Service.ApiUrl, a.Change.Kind, "service:" + a.Service.Id))
        .Select(g => g.ToArray()).ToArray();
}
