using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using StatusLookout.Core;
namespace StatusLookout.Windows;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly JsonStore _store;
    private Preferences _preferences;
    private readonly NotificationTracker _tracker;
    private readonly StatusMonitor _monitor;
    private readonly HttpClient _http = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _timer = new();
    private readonly Action<IReadOnlyList<ServiceItem>, StatusTransition, bool> _notify;
    private bool _refreshAgain, _started;
    public bool IsDemo { get; }
    public ObservableCollection<ServiceItem> AllServices { get; } = [];
    public ObservableCollection<ServiceItem> Watchlist { get; } = [];
    public ObservableCollection<ActivityItem> Activity { get; } = [];
    public ICollectionView LibraryView { get; }
    public ObservableCollection<string> Categories { get; } = [L.T("All categories")];
    public event Action? Updated;
    public MainViewModel(Action<IReadOnlyList<ServiceItem>, StatusTransition, bool> notify, bool demo = false)
    {
        IsDemo = demo;
        _notify = notify;
        _store = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StatusLookout"));
        _preferences = demo ? new() : _store.Load<Preferences>("preferences.json");
        _preferences.Normalize();
        L.Apply(_preferences.Language, _preferences.Theme);
        Categories[0] = _category = L.T("All categories");
        var state = demo ? new MonitorState() : _store.Load<MonitorState>("state.json");
        _tracker = new(state.LastKnown ?? [], state.LastIncidents ?? []);
        foreach (var entry in (state.History ?? []).OrderByDescending(e => e.At).Take(300)) Activity.Add(new(entry));
        foreach (var service in ServiceCatalog.Load().Concat(_preferences.CustomServices).DistinctBy(s => s.Id).OrderBy(s => s.Name))
            AddItem(service);
        foreach (var category in AllServices.Select(s => s.Category).Distinct().Order()) Categories.Add(category);
        LibraryView = CollectionViewSource.GetDefaultView(AllServices);
        LibraryView.Filter = FilterService;
        _monitor = new(_http);
        _timer.Interval = TimeSpan.FromSeconds(_preferences.PollSeconds);
        _timer.Tick += async (_, _) => await RefreshAsync();
        _error = _store.Warning ?? "";
        _launchAtLogin = !demo && StartupService.IsEnabled;
        RebuildWatchlist();
        if (demo) SeedDemo();
        L.Updated += AppearanceUpdated;
        InitializeFeatures();
    }
    private void AddItem(ServiceDefinition service)
    {
        var item = new ServiceItem(service, _preferences.SelectedServices.Contains(service.Id), _preferences.MutedServices.Contains(service.Id));
        item.WatchChanged = WatchChanged;
        item.MuteChanged = s => { if (s.IsMuted) _preferences.MutedServices.Add(s.Id); else _preferences.MutedServices.Remove(s.Id); SavePreferences(); };
        if (_preferences.ComponentSelections.TryGetValue(service.Id, out var components)) item.SetComponents(components);
        if (_preferences.SnoozedUntil.TryGetValue(service.Id, out var until)) item.SnoozedUntil = until;
        AllServices.Add(item);
        if (!IsDemo && service.Id.StartsWith("custom.", StringComparison.Ordinal)) _ = LoadCustomLogoAsync(item);
    }
    public async Task StartAsync()
    {
        _started = true;
        if (IsDemo) return;
        _timer.Start();
        await RefreshAsync();
    }
    private bool _isRefreshing;
    public bool IsRefreshing { get => _isRefreshing; private set { _isRefreshing = value; Changed(); Changed(nameof(CanImportPreferences)); Changed(nameof(RefreshLabel)); Changed(nameof(SyncLabel)); } }
    public string RefreshLabel => IsRefreshing ? L.T("Checking…") : L.T("Refresh now");
    private bool _isPaused;
    public bool IsPaused { get => _isPaused; private set { _isPaused = value; Changed(); Changed(nameof(PauseLabel)); Changed(nameof(SyncLabel)); } }
    public string PauseLabel => IsPaused ? L.T("Resume monitoring") : L.T("Pause");
    private DateTimeOffset? _lastCheck;
    public string SyncLabel => IsDemo ? L.T("Demo preview · simulated states") : IsPaused ? L.T("Monitoring paused") : IsRefreshing ? L.T("Checking official status APIs…") : _lastCheck is null ? L.T("Ready to monitor") : L.T("Monitoring · synced ") + _lastCheck.Value.ToLocalTime().ToString("HH:mm:ss");
    public int WatchedCount => Watchlist.Count;
    public int HealthyCount => Watchlist.Count(s => s.Status == ServiceStatus.Operational);
    public int IssueCount => Watchlist.Count(s => s.Status is ServiceStatus.Outage or ServiceStatus.Degraded or ServiceStatus.Maintenance);
    public int UnknownCount => Watchlist.Count(s => s.Status == ServiceStatus.Unknown);
    public int CatalogCount => AllServices.Count;
    public string CatalogDescription => L.Format("{0} built-in and custom services. Select the ones you want to monitor.", CatalogCount);
    public string Language { get => _preferences.Language; set { if (value is not ("en" or "fr") || value == Language) return; _preferences.Language = value; L.Apply(Language, Theme); SavePreferences(); } }
    public string Theme { get => _preferences.Theme; set { if (value is not ("dark" or "light" or "system") || value == Theme) return; _preferences.Theme = value; L.Apply(Language, Theme); SavePreferences(); } }
    private void AppearanceUpdated()
    {
        var all = Category == Categories[0];
        var category = L.English(Category);
        Categories.Clear(); Categories.Add(L.T("All categories"));
        foreach (var name in AllServices.Select(s => s.Category).Distinct().Order()) Categories.Add(name);
        Category = all ? Categories[0] : L.T(category);
        foreach (var item in AllServices) item.RefreshAppearance();
        foreach (var item in Activity) item.RefreshAppearance();
        Changed(null); Updated?.Invoke();
    }
    public bool IsWatchlistEmpty => Watchlist.Count == 0;
    public bool IsActivityEmpty => Activity.Count == 0;
    public string OverallTitle => WatchedCount == 0 ? L.T("Make room for your services.") : IssueCount > 0 ? L.T("A few signals need your attention.") : UnknownCount > 0 ? L.T("Some signals are still unconfirmed.") : L.T("All clear. Keep doing your thing.");
    public string OverallDescription => WatchedCount == 0 ? L.T("Pick your services from the library to begin.") : IssueCount > 0 ? L.T("Provider reports are listed below. Open a status page for the latest details.") : UnknownCount > 0 ? L.T("An unavailable check is not a confirmed outage. Your last known states are preserved.") : L.T("Your selected providers report normal operation. We'll alert you when that changes.");
    private string _search = "", _category = L.T("All categories");
    public string Search { get => _search; set { _search = value; Changed(); LibraryView.Refresh(); } }
    public string Category { get => _category; set { _category = value; Changed(); LibraryView.Refresh(); } }
    private bool FilterService(object value) => value is ServiceItem service && (Category == L.T("All categories") || service.Category == Category)
        && (string.IsNullOrWhiteSpace(Search) || service.Name.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase) || service.Category.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase));
    private string _error = "";
    public string ErrorMessage { get => _error; private set { _error = value; Changed(); Changed(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public void DismissError() => ErrorMessage = "";
    public void ReportError(string message) => ErrorMessage = message;
    public void TogglePause()
    {
        IsPaused = !IsPaused;
        if (IsPaused) _timer.Stop();
        else if (!IsDemo) { _timer.Start(); _ = RefreshAsync(); }
        Updated?.Invoke();
    }
    public async Task RefreshAsync()
    {
        if (IsDemo) { SeedDemo(); return; }
        if (IsRefreshing) { _refreshAgain = true; return; }
        IsRefreshing = true;
        try
        {
            var definitions = Watchlist.Select(s => s.Definition).ToArray();
            var snapshots = await _monitor.CheckAsync(definitions, _lifetime.Token);
            var pending = new List<PendingAlert>();
            foreach (var snapshot in snapshots)
            {
                var item = AllServices.FirstOrDefault(s => s.Id == snapshot.ServiceId && s.IsWatched);
                if (item is null) continue;
                if (!definitions.Contains(item.Definition)) continue; // Ignore results for filters changed during this refresh.
                var change = _tracker.Observe(snapshot, _preferences.AlertOnFirstProblem);
                _tracker.LastKnown.TryGetValue(item.Id, out var baseline);
                item.Update(snapshot, baseline == ServiceStatus.Unknown ? null : baseline);
                if (change is null) continue;
                Activity.Insert(0, new(new(item.Id, item.Name, change.Current, change.Kind, change.Message, change.At)));
                if (_preferences.ShouldNotify(change)) pending.Add(new(item.Definition, change));
            }
            foreach (var group in AlertGrouping.Group(pending, _preferences.GroupRelatedAlerts))
                _notify(group.Select(a => AllServices.Single(s => s.Id == a.Service.Id)).ToArray(), group[0].Change, _preferences.NotificationSound);
            while (Activity.Count > 300) Activity.RemoveAt(Activity.Count - 1);
            _lastCheck = DateTimeOffset.UtcNow;
            SaveState();
            RebuildWatchlist();
            Changed(nameof(IsActivityEmpty));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { ErrorMessage = L.T("The refresh could not finish: ") + ex.Message; }
        finally { IsRefreshing = false; Updated?.Invoke(); }
        if (_refreshAgain && !_lifetime.IsCancellationRequested) { _refreshAgain = false; await RefreshAsync(); }
    }
    private void WatchChanged(ServiceItem service)
    {
        if (service.IsWatched) _preferences.SelectedServices.Add(service.Id);
        else { _preferences.SelectedServices.Remove(service.Id); _tracker.LastKnown.Remove(service.Id); _tracker.LastIncidents.Remove(service.Id); }
        SavePreferences(); SaveState(); RebuildWatchlist();
        if (_started && !IsPaused && service.IsWatched) _ = RefreshAsync();
    }
    private void RebuildWatchlist()
    {
        var ordered = AllServices.Where(s => s.IsWatched).OrderByDescending(s => s.Status == ServiceStatus.Outage ? 4 : s.Status == ServiceStatus.Degraded ? 3 : s.Status == ServiceStatus.Maintenance ? 2 : s.Status == ServiceStatus.Unknown ? 1 : 0).ThenBy(s => s.Name).ToArray();
        Watchlist.Clear();
        foreach (var service in ordered) Watchlist.Add(service);
        foreach (var p in new[] { nameof(WatchedCount), nameof(HealthyCount), nameof(IssueCount), nameof(UnknownCount), nameof(IsWatchlistEmpty), nameof(OverallTitle), nameof(OverallDescription), nameof(SyncLabel) }) Changed(p);
        Updated?.Invoke();
    }
    public async Task<bool> AddCustomAsync(string name, string url, string provider, string components)
    {
        try
        {
            var statusUrl = url.Trim().TrimEnd('/');
            var apiUrl = statusUrl + (provider == "instatus" ? "/summary.json" : "/api/v2/summary.json");
            var service = new ServiceDefinition("custom." + Guid.NewGuid().ToString("N"), name.Trim(), "Custom services", provider, statusUrl, apiUrl,
                string.IsNullOrWhiteSpace(components) ? null : components.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            service.Validate();
            var result = (await _monitor.CheckAsync([service], _lifetime.Token)).Single();
            if (result.Status == ServiceStatus.Unknown) throw new InvalidDataException(result.Message);
            _preferences.CustomServices.Add(service);
            _preferences.SelectedServices.Add(service.Id);
            AddItem(service);
            var item = AllServices.Last();
            item.Update(result, null);
            if (!Categories.Contains(L.T("Custom services"))) Categories.Add(L.T("Custom services"));
            SavePreferences(); RebuildWatchlist(); Changed(nameof(CatalogCount)); LibraryView.Refresh();
            if (_started && !IsPaused) _ = RefreshAsync();
            return true;
        }
        catch (Exception ex) { ErrorMessage = L.T("This service could not be added: ") + ex.Message; return false; }
    }
    public void ClearActivity()
    {
        Activity.Clear(); SaveState(); Changed(nameof(IsActivityEmpty));
    }
    private void SavePreferences()
    {
        if (IsDemo) return;
        try { _store.Save("preferences.json", _preferences); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorMessage = L.T("Preferences could not be saved: ") + ex.Message; }
    }
    private void SaveState()
    {
        if (IsDemo) return;
        try { _store.Save("state.json", new MonitorState { LastKnown = _tracker.LastKnown, LastIncidents = _tracker.LastIncidents, History = Activity.Select(a => a.Entry).Take(300).ToList() }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorMessage = L.T("History could not be saved: ") + ex.Message; }
    }
    private bool _launchAtLogin;
    public bool LaunchAtLogin
    {
        get => _launchAtLogin;
        set
        {
            if (IsDemo || _launchAtLogin == value) return;
            try { StartupService.SetEnabled(value); _launchAtLogin = value; Changed(); }
            catch (Exception ex) { ErrorMessage = L.T("Windows startup could not be updated: ") + ex.Message; Changed(); }
        }
    }
    public bool StartMinimized { get => _preferences.StartMinimized; set { _preferences.StartMinimized = value; SavePreferences(); Changed(); } }
    public bool NotifyOutage { get => _preferences.NotifyOutage; set { _preferences.NotifyOutage = value; SavePreferences(); Changed(); } }
    public bool NotifyDegraded { get => _preferences.NotifyDegraded; set { _preferences.NotifyDegraded = value; SavePreferences(); Changed(); } }
    public bool NotifyRecovery { get => _preferences.NotifyRecovery; set { _preferences.NotifyRecovery = value; SavePreferences(); Changed(); } }
    public bool NotifyMaintenance { get => _preferences.NotifyMaintenance; set { _preferences.NotifyMaintenance = value; SavePreferences(); Changed(); } }
    public bool NotificationSound { get => _preferences.NotificationSound; set { _preferences.NotificationSound = value; SavePreferences(); Changed(); } }
    public bool AlertOnFirstProblem { get => _preferences.AlertOnFirstProblem; set { _preferences.AlertOnFirstProblem = value; SavePreferences(); Changed(); } }
    public int PollSeconds
    {
        get => _preferences.PollSeconds;
        set { _preferences.PollSeconds = Math.Clamp(value, 30, 900); _timer.Interval = TimeSpan.FromSeconds(_preferences.PollSeconds); SavePreferences(); Changed(); }
    }
    private void SeedDemo()
    {
        var demoStates = new Dictionary<string, (ServiceStatus, string)>
        {
            ["statuspage.GitHub"] = (ServiceStatus.Operational, "All systems operational"),
            ["statuspage.Cloudflare"] = (ServiceStatus.Degraded, "Elevated latency in a subset of regions"),
            ["statuspage.Discord"] = (ServiceStatus.Operational, "All systems operational"),
            ["extra.OpenAI"] = (ServiceStatus.Outage, "API requests are experiencing elevated error rates"),
            ["extra.Slack"] = (ServiceStatus.Operational, "Slack is up and running")
        };
        foreach (var item in Watchlist)
        {
            var state = demoStates.GetValueOrDefault(item.Id, (ServiceStatus.Operational, "All systems operational"));
            item.Update(new(item.Id, state.Item1, state.Item2, DateTimeOffset.UtcNow), null);
        }
        if (Activity.Count == 0)
        {
            Activity.Add(new(new("extra.OpenAI", "OpenAI", ServiceStatus.Outage, AlertKind.Outage, "API requests are experiencing elevated error rates", DateTimeOffset.UtcNow.AddMinutes(-3))));
            Activity.Add(new(new("statuspage.Cloudflare", "Cloudflare", ServiceStatus.Degraded, AlertKind.Degraded, "Elevated latency in a subset of regions", DateTimeOffset.UtcNow.AddMinutes(-8))));
            Activity.Add(new(new("statuspage.GitHub", "GitHub", ServiceStatus.Operational, AlertKind.Recovery, "All systems operational", DateTimeOffset.UtcNow.AddMinutes(-14))));
        }
        RebuildWatchlist();
    }
    public void Dispose()
    {
        _featureTimer.Stop(); Microsoft.Win32.SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged;
        _downloadCancellation?.Cancel(); _updateHttp.Dispose();
        L.Updated -= AppearanceUpdated;
        _timer.Stop(); _lifetime.Cancel(); _http.Dispose();
    }
}
