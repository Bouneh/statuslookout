using System.Reflection;
using System.Runtime.InteropServices;
using StatusLookout.Core;
using System.Windows.Threading;

namespace StatusLookout.Windows;

public sealed partial class MainViewModel
{
    private readonly HttpClient _updateHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    private AvailableUpdate? _latestUpdate;
    private CancellationTokenSource? _downloadCancellation;
    private string _updateState = "idle", _updateError = "", _downloadedPath = "";
    private double _downloadProgress;
    private DateTimeOffset? _updateCheckedAt;
    private readonly DispatcherTimer _automaticUpdateTimer=new(){Interval=TimeSpan.FromMinutes(1)};
    private DateTimeOffset _nextAutomaticUpdateCheck=DateTimeOffset.UtcNow.AddSeconds(30);
    public event Action<AvailableUpdate>? UpdateAvailable;
    public bool AutomaticUpdates {get=>_preferences.AutomaticUpdates;set{_preferences.AutomaticUpdates=value;SavePreferences();Changed();if(value)_nextAutomaticUpdateCheck=DateTimeOffset.UtcNow;}}
    public bool HasAvailableUpdate=>_latestUpdate is not null&&_updateState!="downloaded";
    public string AvailableUpdateMessage=>_latestUpdate is null?"":L.Format("StatusLookout {0} is available.",_latestUpdate.Version.ToString(3));
    private void StartAutomaticUpdates()
    {
        if(IsDemo)return;
        _automaticUpdateTimer.Tick+=async (_,_)=>await CheckAutomaticUpdatesAsync();
        _automaticUpdateTimer.Start();_ = InitialAutomaticUpdateCheckAsync();
    }
    private async Task InitialAutomaticUpdateCheckAsync()
    {
        try{await Task.Delay(TimeSpan.FromSeconds(30),_lifetime.Token);await CheckAutomaticUpdatesAsync();}catch(OperationCanceledException){}
    }
    private async Task CheckAutomaticUpdatesAsync()
    {
        if(IsDemo||!AutomaticUpdates||DateTimeOffset.UtcNow<_nextAutomaticUpdateCheck||!CanCheckUpdates||_updateState=="downloaded")return;
        _nextAutomaticUpdateCheck=DateTimeOffset.UtcNow.AddHours(24);
        await CheckForUpdatesAsync(true);
        if(_updateState=="error")_nextAutomaticUpdateCheck=DateTimeOffset.UtcNow.AddHours(1);
    }
    public static Version InstalledVersion => Assembly.GetExecutingAssembly().GetName().Version!;
    public string CurrentVersion => InstalledVersion.ToString(3);
    public string AppVersionLabel => "v" + CurrentVersion + " · OPEN SOURCE";
    public string VersionDescription => L.Format("Installed version: {0}", CurrentVersion);
    public string UpdateStatus => _updateState switch
    {
        "checking" => L.T("Checking GitHub for updates…"),
        "current" => L.T("You are using the latest stable version."),
        "available" => L.Format("Version {0} is available.", _latestUpdate!.Version.ToString(3)),
        "downloading" => L.Format("Downloading… {0:0}%", _downloadProgress),
        "downloaded" => L.T("Download verified. Quit StatusLookout and replace the old executable to install the update."),
        "cancelled" => L.T("Download cancelled. You can try again."),
        "error" => L.Format("Update failed: {0}", L.T(_updateError)),
        _ => L.T("Check for a newer version on GitHub.")
    };
    public string LastUpdateCheck => _updateCheckedAt is null ? "" : L.Format("Last checked: {0}", _updateCheckedAt.Value.ToLocalTime().ToString("g"));
    public bool IsDownloading => _updateState == "downloading";
    public bool CanCheckUpdates => _updateState is not ("checking" or "downloading");
    public bool CanDownloadUpdate => _latestUpdate is not null && CanCheckUpdates;
    public bool HasDownloadedFile => !string.IsNullOrEmpty(_downloadedPath) && File.Exists(_downloadedPath);
    public double DownloadProgress => _downloadProgress;
    public string DownloadedPath => _downloadedPath;
    public string DownloadUpdateLabel => _latestUpdate is null ? L.T("Download update") : L.Format("Download {0}", _latestUpdate.Version.ToString(3));
    public string LatestReleaseUrl => _latestUpdate?.ReleaseUrl ?? "https://github.com/Bouneh/statuslookout/releases/latest";
    private static string WindowsRuntime => RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => "win-x64", Architecture.X86 => "win-x86", Architecture.Arm64 => "win-arm64",
        _ => throw new NotSupportedException("Unsupported Windows architecture.")
    };
    private void UpdateChanged() => Changed(null);
    public async Task CheckForUpdatesAsync(bool automatic=false)
    {
        if (!CanCheckUpdates) return;
        _updateState = "checking"; _downloadedPath = ""; UpdateChanged();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            _latestUpdate = await new UpdateService(_updateHttp).CheckAsync(InstalledVersion, WindowsRuntime, timeout.Token);
            _updateCheckedAt = DateTimeOffset.Now;
            _updateState = _latestUpdate is null ? "current" : "available";
            if(_latestUpdate is { } update&&AutomaticUpdatePolicy.ShouldNotify(update.Version,InstalledVersion,_preferences.LastNotifiedUpdateVersion))
            {
                if(automatic&&AutomaticUpdates) UpdateAvailable?.Invoke(update);
                if(!automatic||AutomaticUpdates){_preferences.LastNotifiedUpdateVersion=update.Version.ToString(3);SavePreferences();}
            }
        }
        catch (OperationCanceledException) { _updateError = "The request was cancelled or timed out."; _updateState = "error"; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        { _updateError = ex.Message; _updateState = "error"; }
        UpdateChanged();
    }
    public async Task DownloadUpdateAsync()
    {
        if (!CanDownloadUpdate || _latestUpdate is null) return;
        _updateState = "downloading"; _downloadProgress = 0; UpdateChanged();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        cancellation.CancelAfter(TimeSpan.FromMinutes(30)); _downloadCancellation = cancellation;
        try
        {
            var destination = Path.Combine(DownloadsFolder(), "StatusLookout-" + _latestUpdate.Version.ToString(3) + "-" + WindowsRuntime + ".exe");
            var progress = new Progress<double>(value => { if (!IsDownloading) return; _downloadProgress = value; Changed(nameof(DownloadProgress)); Changed(nameof(UpdateStatus)); });
            await new UpdateService(_updateHttp).DownloadAsync(_latestUpdate, destination, progress, cancellation.Token);
            _downloadedPath = destination; _downloadProgress = 100; _updateState = "downloaded";
        }
        catch (OperationCanceledException) { _updateState = "cancelled"; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or ExternalException)
        { _updateError = ex.Message; _updateState = "error"; }
        finally { _downloadCancellation = null; UpdateChanged(); }
    }
    public void CancelUpdateDownload() => _downloadCancellation?.Cancel();
    private static string DownloadsFolder()
    {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        Marshal.ThrowExceptionForHR(SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var path));
        try { return Marshal.PtrToStringUni(path) ?? throw new IOException("The Downloads folder is unavailable."); }
        finally { Marshal.FreeCoTaskMem(path); }
    }
    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
}
