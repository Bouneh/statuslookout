namespace StatusLookout.Core;

/// <summary>Tracks confirmed provider states. Transport failures never overwrite the baseline.</summary>
public sealed class NotificationTracker(Dictionary<string, ServiceStatus>? lastKnown = null, Dictionary<string, string>? lastIncidents = null)
{
    public Dictionary<string, ServiceStatus> LastKnown { get; } = lastKnown ?? [];
    public Dictionary<string, string> LastIncidents { get; } = lastIncidents ?? [];
    public StatusTransition? Observe(ServiceSnapshot snapshot, bool alertOnFirstProblem = true)
    {
        if (snapshot.Status == ServiceStatus.Unknown) return null;
        ServiceStatus? previous = LastKnown.TryGetValue(snapshot.ServiceId, out var old) && old != ServiceStatus.Unknown ? old : null;
        LastIncidents.TryGetValue(snapshot.ServiceId, out var previousIncident);
        if (snapshot.Status == ServiceStatus.Operational) LastIncidents.Remove(snapshot.ServiceId);
        else if (snapshot.IncidentKey is not null) LastIncidents[snapshot.ServiceId] = snapshot.IncidentKey;
        else LastIncidents.Remove(snapshot.ServiceId);
        LastKnown[snapshot.ServiceId] = snapshot.Status;
        if (previous == snapshot.Status) return null;
        if (previous is null && (!alertOnFirstProblem || snapshot.Status == ServiceStatus.Operational)) return null;
        var kind = snapshot.Status switch
        {
            ServiceStatus.Operational => AlertKind.Recovery,
            ServiceStatus.Outage => AlertKind.Outage,
            ServiceStatus.Maintenance => AlertKind.Maintenance,
            _ => AlertKind.Degraded
        };
        return new(snapshot.ServiceId, previous, snapshot.Status, kind, snapshot.Message, snapshot.CheckedAt,
            kind == AlertKind.Recovery ? previousIncident : snapshot.IncidentKey);
    }
}
