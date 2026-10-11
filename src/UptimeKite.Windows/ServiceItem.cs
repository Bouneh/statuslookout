using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using UptimeKite.Core;
namespace UptimeKite.Windows;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
public sealed class ServiceItem(ServiceDefinition definition, bool watched, bool muted) : ObservableObject
{
    public ServiceDefinition Definition { get; } = definition;
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public string Category => Definition.Category;
    public string ProviderLabel => Definition.Provider switch { "statuspage" => "Statuspage-compatible API", "google" => "Google Status Dashboard", "statusio" => "Status.io", "instatus" => "Instatus", _ => "Slack Status API" };
    public string Initials => string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(n => n[0])).ToUpperInvariant();
    private bool _watched = watched, _muted = muted;
    public Action<ServiceItem>? WatchChanged { get; set; }
    public Action<ServiceItem>? MuteChanged { get; set; }
    public bool IsWatched { get => _watched; set { if (_watched == value) return; _watched = value; Changed(); WatchChanged?.Invoke(this); } }
    public bool IsMuted { get => _muted; set { if (_muted == value) return; _muted = value; Changed(); MuteChanged?.Invoke(this); } }
    public ServiceStatus Status { get; private set; } = ServiceStatus.Unknown;
    public string StatusLabel => Status switch { ServiceStatus.Operational => "Operational", ServiceStatus.Degraded => "Degraded", ServiceStatus.Outage => "Outage", ServiceStatus.Maintenance => "Maintenance", _ => "Unavailable" };
    public string Message { get; private set; } = "Waiting for the first status check";
    public string CheckedLabel { get; private set; } = "Not checked yet";
    public string LastConfirmed { get; private set; } = "";
    private DateTimeOffset? _lastGoodAt;
    public SolidColorBrush StatusBrush => new((Color)ColorConverter.ConvertFromString(Status switch
    {
        ServiceStatus.Operational => "#56DCB8", ServiceStatus.Degraded => "#FFBD69", ServiceStatus.Outage => "#FF6F86", ServiceStatus.Maintenance => "#A79BFF", _ => "#95A4BE"
    }));
    public SolidColorBrush StatusBackground => new(Color.FromArgb(25, StatusBrush.Color.R, StatusBrush.Color.G, StatusBrush.Color.B));
    public void Update(ServiceSnapshot snapshot, ServiceStatus? baseline)
    {
        Status = snapshot.Status;
        Message = snapshot.Message.Length > 600 ? snapshot.Message[..597] + "…" : snapshot.Message;
        CheckedLabel = "Checked " + snapshot.CheckedAt.ToLocalTime().ToString("HH:mm:ss");
        if (snapshot.Status != ServiceStatus.Unknown) _lastGoodAt = snapshot.CheckedAt;
        LastConfirmed = snapshot.Status == ServiceStatus.Unknown && baseline is not null
            ? $"Last confirmed: {baseline}" + (_lastGoodAt is not null ? " at " + _lastGoodAt.Value.ToLocalTime().ToString("HH:mm") : "") : "";
        foreach (var property in new[] { nameof(Status), nameof(StatusLabel), nameof(StatusBrush), nameof(StatusBackground), nameof(Message), nameof(CheckedLabel), nameof(LastConfirmed) }) Changed(property);
    }
}
public sealed class ActivityItem(ActivityEntry entry)
{
    public ActivityEntry Entry { get; } = entry;
    public string Title => Entry.ServiceName + " · " + (Entry.Kind == AlertKind.Recovery ? "Back to normal" : Entry.Kind.ToString());
    public string Message => Entry.Message;
    public string Time => Entry.At.ToLocalTime().ToString("dd MMM · HH:mm:ss");
    public SolidColorBrush Color => new((Color)ColorConverter.ConvertFromString(Entry.Kind switch { AlertKind.Recovery => "#56DCB8", AlertKind.Outage => "#FF6F86", AlertKind.Degraded => "#FFBD69", _ => "#A79BFF" }));
}
