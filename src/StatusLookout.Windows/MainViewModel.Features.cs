using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using StatusLookout.Core;
namespace StatusLookout.Windows;

public sealed partial class MainViewModel
{
    private readonly DispatcherTimer _featureTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    public bool GroupRelatedAlerts { get => _preferences.GroupRelatedAlerts; set { _preferences.GroupRelatedAlerts = value; SavePreferences(); Changed(); } }
    public bool CanImportPreferences => !IsRefreshing;
    private void InitializeFeatures()
    {
        _featureTimer.Tick += (_, _) =>
        {
            ApplySystemTheme();
            var expired = _preferences.SnoozedUntil.Where(p => p.Value <= DateTimeOffset.UtcNow).Select(p => p.Key).ToArray();
            foreach (var id in expired) _preferences.SnoozedUntil.Remove(id);
            foreach (var service in AllServices) service.RefreshAppearance();
            if (expired.Length > 0) SavePreferences();
        };
        _featureTimer.Start(); SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
    }
    private void SystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!_lifetime.IsCancellationRequested) Application.Current.Dispatcher.BeginInvoke(ApplySystemTheme);
    }
    private void ApplySystemTheme() { if (Theme == "system" && L.Theme != WindowsTheme.Read()) L.Apply(Language, Theme); }
    public void Snooze(ServiceItem service, int minutes)
    {
        if (minutes is not (0 or 30 or 60)) throw new ArgumentOutOfRangeException(nameof(minutes));
        if (minutes == 0) { _preferences.SnoozedUntil.Remove(service.Id); service.SnoozedUntil = null; }
        else { service.SnoozedUntil = DateTimeOffset.UtcNow.AddMinutes(minutes); _preferences.SnoozedUntil[service.Id] = service.SnoozedUntil.Value; }
        service.RefreshAppearance(); SavePreferences();
    }
    public Task<IReadOnlyList<ProviderComponent>> GetComponentsAsync(ServiceItem service, CancellationToken token) => _monitor.GetComponentsAsync(service.Definition, token);
    public void SetComponents(ServiceItem service, string[]? components)
    {
        if (components is null) _preferences.ComponentSelections.Remove(service.Id);
        else _preferences.ComponentSelections[service.Id] = components;
        service.SetComponents(components ?? service.DefaultDefinition.Components);
        _tracker.LastKnown.Remove(service.Id); _tracker.LastIncidents.Remove(service.Id);
        SavePreferences(); SaveState();
        if (_started && !IsPaused) _ = RefreshAsync();
    }
    public string ExportPreferences() => PreferenceTransfer.Export(_preferences);
    public void ImportPreferences(string json)
    {
        if (IsRefreshing) throw new InvalidOperationException(L.T("Wait for the current check to finish before importing."));
        var imported = PreferenceTransfer.Import(json, ServiceCatalog.Load());
        if (!IsDemo) _store.Save("preferences.json", imported); // Save successfully before replacing the running settings.
        _preferences = imported; _tracker.LastKnown.Clear(); _tracker.LastIncidents.Clear();
        InitializeSounds();
        RebuildLists();
        AllServices.Clear();
        foreach (var service in ServiceCatalog.Load().Concat(imported.CustomServices).OrderBy(s => s.Name)) AddItem(service);
        RebuildLibrary();
        _timer.Interval = TimeSpan.FromSeconds(imported.PollSeconds);
        L.Apply(Language, Theme); LibraryView.Refresh(); RebuildWatchlist(); Changed(null); SaveState();
        if (_started && !IsPaused) _ = RefreshAsync();
    }
    private async Task LoadCustomLogoAsync(ServiceItem service)
    {
        try { service.SetLogo(await ServiceLogos.GetCustomAsync(service.Definition, _lifetime.Token)); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }
}
