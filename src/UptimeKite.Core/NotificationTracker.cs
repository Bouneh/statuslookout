namespace UptimeKite.Core;

/// <summary>Tracks confirmed provider states. Transport failures never overwrite the baseline.</summary>
public sealed class NotificationTracker(Dictionary<string, ServiceStatus>? lastKnown = null)
{
    public Dictionary<string, ServiceStatus> LastKnown { get; } = lastKnown ?? [];
    public StatusTransition? Observe(ServiceSnapshot snapshot, bool alertOnFirstProblem = true)
    {
        if (snapshot.Status == ServiceStatus.Unknown) return null;
        ServiceStatus? previous = LastKnown.TryGetValue(snapshot.ServiceId, out var old) && old != ServiceStatus.Unknown ? old : null;
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
        return new(snapshot.ServiceId, previous, snapshot.Status, kind, snapshot.Message, snapshot.CheckedAt);
    }
}
