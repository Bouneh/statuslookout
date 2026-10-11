using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using StatusLookout.Core;
namespace StatusLookout.Windows;

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
    public string Category => L.T(Definition.Category);
    public string ProviderLabel => Definition.Provider switch { "statuspage" => L.T("Statuspage-compatible API"), "google" => "Google Status Dashboard", "statusio" => "Status.io", "instatus" => "Instatus", _ => "Slack Status API" };
    public string Initials => string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(n => n[0])).ToUpperInvariant();
    public ImageSource? Logo => ServiceLogos.Get(Definition);
    public bool HasLogo => Logo is not null;
    private bool _watched = watched, _muted = muted;
    public Action<ServiceItem>? WatchChanged { get; set; }
    public Action<ServiceItem>? MuteChanged { get; set; }
    public bool IsWatched { get => _watched; set { if (_watched == value) return; _watched = value; Changed(); WatchChanged?.Invoke(this); } }
    public bool IsMuted { get => _muted; set { if (_muted == value) return; _muted = value; Changed(); MuteChanged?.Invoke(this); } }
    public ServiceStatus Status { get; private set; } = ServiceStatus.Unknown;
    public string StatusLabel => L.Status(Status);
    private string? _message;
    public string Message => _message ?? L.T("Waiting for the first status check");
    private DateTimeOffset? _checkedAt;
    public string CheckedLabel => _checkedAt is null ? L.T("Not checked yet") : L.T("Checked ") + _checkedAt.Value.ToLocalTime().ToString("HH:mm:ss");
    private ServiceStatus? _baseline;
    public string LastConfirmed => Status == ServiceStatus.Unknown && _baseline is not null ? L.Format("Last confirmed: {0}", L.Status(_baseline.Value)) + (_lastGoodAt is not null ? L.T(" at ") + _lastGoodAt.Value.ToLocalTime().ToString("HH:mm") : "") : "";
    public void RefreshAppearance() => Changed(null);
    private DateTimeOffset? _lastGoodAt;
    public SolidColorBrush StatusBrush => BrushFor(Status);
    public static SolidColorBrush BrushFor(ServiceStatus status) => new((Color)ColorConverter.ConvertFromString((status, L.Theme == "light") switch
    {
        (ServiceStatus.Operational, true) => "#08765B", (ServiceStatus.Degraded, true) => "#956000", (ServiceStatus.Outage, true) => "#B32646", (ServiceStatus.Maintenance, true) => "#6D49B8", (ServiceStatus.Operational, false) => "#56DCB8", (ServiceStatus.Degraded, false) => "#FFBD69", (ServiceStatus.Outage, false) => "#FF6F86", (ServiceStatus.Maintenance, false) => "#A79BFF", _ => L.Theme == "light" ? "#52627B" : "#95A4BE"
    }));
    public SolidColorBrush StatusBackground => new(Color.FromArgb(25, StatusBrush.Color.R, StatusBrush.Color.G, StatusBrush.Color.B));
    public void Update(ServiceSnapshot snapshot, ServiceStatus? baseline)
    {
        Status = snapshot.Status;
        _message = snapshot.Message.Length > 600 ? snapshot.Message[..597] + "…" : snapshot.Message;
        _checkedAt = snapshot.CheckedAt; _baseline = baseline;
        if (snapshot.Status != ServiceStatus.Unknown) _lastGoodAt = snapshot.CheckedAt;
        foreach (var property in new[] { nameof(Status), nameof(StatusLabel), nameof(StatusBrush), nameof(StatusBackground), nameof(Message), nameof(CheckedLabel), nameof(LastConfirmed) }) Changed(property);
    }
}
public sealed class ActivityItem(ActivityEntry entry) : ObservableObject
{
    public ActivityEntry Entry { get; } = entry;
    public void RefreshAppearance() => Changed(null);
    public string Title => Entry.ServiceName + " · " + L.Alert(Entry.Kind);
    public string Message => Entry.Message;
    public string Time => Entry.At.ToLocalTime().ToString("dd MMM · HH:mm:ss");
    public SolidColorBrush Color => ServiceItem.BrushFor(Entry.Status);
}
