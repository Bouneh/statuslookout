using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Toolkit.Uwp.Notifications;
namespace StatusLookout.Windows;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _activation;
    private RegisteredWaitHandle? _waitHandle;
    private MainViewModel? _vm;
    private DesktopIntegration? _desktop;
    private StatusLookout.Core.LocalBridgeServer? _bridge;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var previewIndex = Array.IndexOf(e.Args, "--preview");
        var preview = previewIndex >= 0 && e.Args.Length > previewIndex + 1 ? e.Args[previewIndex + 1] : null;
        var smokeIndex = Array.IndexOf(e.Args, "--smoke-test");
        var smoke = smokeIndex >= 0 && e.Args.Length > smokeIndex + 1 ? e.Args[smokeIndex + 1] : null;
        var featureIndex = Array.IndexOf(e.Args, "--feature-test");
        var features = featureIndex >= 0 && e.Args.Length > featureIndex + 1 ? e.Args[featureIndex + 1] : null;
        var demo = e.Args.Contains("--demo") || preview is not null || smoke is not null || features is not null;
        if (preview is null && smoke is null && features is null)
        {
            _mutex = new Mutex(true, @"Local\StatusLookout.Instance", out var first);
            if (!first)
            {
                try { using var signal = EventWaitHandle.OpenExisting(@"Local\StatusLookout.Activate"); signal.Set(); } catch { }
                Shutdown(); return;
            }
            _activation = new(false, EventResetMode.AutoReset, @"Local\StatusLookout.Activate");
            _waitHandle = ThreadPool.RegisterWaitForSingleObject(_activation, (_, _) => Dispatcher.BeginInvoke(ShowDashboard), null, Timeout.Infinite, false);
        }
        _vm = new((service, change, sound) => _desktop?.Show(service, change, sound), demo);
        if (demo) { if (e.Args.Contains("--french")) _vm.Language = "fr"; if (e.Args.Contains("--light")) _vm.Theme = "light"; }
        MainWindow = new MainWindow(_vm);
        if (demo && e.Args.Contains("--about")) ((MainWindow)MainWindow).ShowAbout();
        if (demo && e.Args.Contains("--settings")) ((MainWindow)MainWindow).ShowPreferences();
        if (features is not null)
        {
            MainWindow.Loaded += async (_, _) =>
            {
                try
                {
                    var result = await VerifyFeaturesAsync(e.Args.Contains("--network"));
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(features))!);
                    File.WriteAllText(features, System.Text.Json.JsonSerializer.Serialize(result, StatusLookout.Core.JsonStore.Options));
                    Shutdown();
                }
                catch (Exception ex) { File.WriteAllText(features, ex.ToString()); Shutdown(1); }
            };
            MainWindow.Show(); return;
        }
        if (demo && e.Args.Contains("--components"))
            MainWindow = new ComponentPickerWindow(_vm, _vm.AllServices.Single(s => s.Id == "extra.GoogleCloud"));
        if (preview is null)
        {
            if (smoke is null) _bridge = new(StatusLookout.Core.StreamDeckProtocol.PipeName(Environment.UserDomainName, Environment.UserName),
                (request, token) => Dispatcher.InvokeAsync(() => HandleStreamDeck(request), DispatcherPriority.Normal, token).Task);
            _desktop = new(ShowDashboard, Shutdown, () => _ = _vm.RefreshAsync(), _vm.TogglePause, _vm.ReportError);
            _vm.Updated += () => _desktop.Update(_vm);
            _desktop.Update(_vm);
            ToastNotificationManagerCompat.OnActivated += args =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    ShowDashboard();
                    var values = ToastArguments.Parse(args.Argument);
                    if (values.TryGetValue("service", out var id))
                    {
                        var service = _vm.AllServices.FirstOrDefault(s => s.Id == id);
                        if (service is not null)
                        {
                            try { DesktopIntegration.OpenStatus(service.Definition.StatusUrl); }
                            catch (Exception ex) { _vm.ReportError(L.T("The status page could not be opened: ") + ex.Message); }
                        }
                    }
                });
            };
        }
        if (preview is not null)
        {
            if (_vm.AllServices.Any(service => !service.HasLogo)) throw new InvalidDataException("A bundled service logo is missing or unreadable.");
            MainWindow.Loaded += async (_, _) =>
            {
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                MainWindow.UpdateLayout();
                var image = new RenderTargetBitmap((int)MainWindow.ActualWidth, (int)MainWindow.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(MainWindow);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(preview))!);
                using var file = File.Create(preview);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); png.Save(file);
                Shutdown();
            };
            MainWindow.Show();
        }
        else
        {
            if (smoke is null && !e.Args.Contains("--background") && !_vm.StartMinimized && !ToastNotificationManagerCompat.WasCurrentProcessToastActivated()) MainWindow.Show();
            _ = _vm.StartAsync();
            if (smoke is not null)
            {
                Dispatcher.BeginInvoke(async () =>
                {
                    var appearanceVerified = await VerifyAppearanceAsync();
                    _desktop!.Test(false);
                    await Task.Delay(1000);
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(smoke))!);
                    File.WriteAllText(smoke, System.Text.Json.JsonSerializer.Serialize(new
                    {
                        CatalogCount = _vm.CatalogCount, WatchlistCount = _vm.WatchedCount,
                        TrayInitialized = true, LogoCount = _vm.AllServices.Count(s => s.HasLogo), AppearanceVerified = appearanceVerified, _desktop.ToastRegistrationReady, _desktop.LastDelivery
                    }, StatusLookout.Core.JsonStore.Options));
                    Shutdown();
                });
            }
        }
    }
    private async Task<bool> VerifyAppearanceAsync()
    {
        if (_vm is null || MainWindow is not MainWindow window) return false;
        var service = _vm.AllServices.First(s => s.Id == "extra.OpenAI");
        _vm.Category = "AI services";
        window.ShowPreferences();
        _vm.Language = "fr"; _vm.Theme = "light";
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        var frenchLight = _vm.Language == "fr" && _vm.Theme == "light"
            && service.StatusLabel == "Panne" && _vm.Category == "Services d’IA"
            && window.Title.Contains("Surveillance")
            && ((SolidColorBrush)window.Background).Color == (Color)ColorConverter.ConvertFromString("#F4F7FC")
            && L.T("Open StatusLookout") == "Ouvrir StatusLookout";
        _vm.Language = "en"; _vm.Theme = "dark";
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        var englishDark = service.StatusLabel == "Outage" && _vm.Category == "AI services"
            && window.Title.Contains("Cloud service")
            && ((SolidColorBrush)window.Background).Color == (Color)ColorConverter.ConvertFromString("#0C1220")
            && L.T("Open StatusLookout") == "Open StatusLookout";
        if (!frenchLight || !englishDark) throw new InvalidOperationException("Live appearance verification failed.");
        return true;
    }
    private async Task<object> VerifyFeaturesAsync(bool network)
    {
        var vm = _vm!; var service = vm.Watchlist.First();
        var menu = (System.Windows.Controls.ContextMenu)MainWindow.Resources["ServiceMenu"];
        var target = new System.Windows.Controls.Button { Tag = service, ContextMenu = menu };
        menu.PlacementTarget = target; menu.IsOpen = true;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        ((System.Windows.Controls.MenuItem)menu.Items[0]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        var menuVerified = service.IsSnoozed; menu.IsOpen = false;
        vm.Snooze(service, 30); var thirty = service.IsSnoozed && service.SnoozedUntil > DateTimeOffset.UtcNow.AddMinutes(29);
        vm.Language = "fr"; var frenchSnooze = service.SnoozeLabel.StartsWith("Alertes suspendues");
        vm.Snooze(service, 60); var hour = service.SnoozedUntil > DateTimeOffset.UtcNow.AddMinutes(59);
        vm.Snooze(service, 0); var resumed = !service.IsSnoozed;
        vm.Theme = "system"; var systemTheme = vm.Theme == "system" && L.Theme == WindowsTheme.Read();
        vm.SetComponents(service, ["test-api"]); var scoped = service.Definition.Components?.Single() == "test-api";
        var export = vm.ExportPreferences(); vm.ImportPreferences(export);
        var imported = vm.AllServices.Single(s => s.Id == service.Id).Definition.Components?.Single() == "test-api";
        service = vm.AllServices.Single(s => s.Id == service.Id); vm.SetComponents(service, null);
        var defaultRestored = service.Definition.Components == service.DefaultDefinition.Components;
        var google = vm.AllServices.Single(s => s.Id == "extra.GoogleCloud");
        var products = await vm.GetComponentsAsync(google, CancellationToken.None);
        var googlePicker = products.Count > 200;
        bool? mailjetLogo = null; int? statuspageComponents = null;
        if (network)
        {
            var custom = new StatusLookout.Core.ServiceDefinition("custom.mailjet-audit", "Mailjet", "Custom services", "statuspage", "https://status.mailjet.com", "https://status.mailjet.com/api/v2/summary.json");
            var logo = await ServiceLogos.GetCustomAsync(custom, CancellationToken.None); mailjetLogo = logo is not null;
            if (logo is BitmapSource bitmap)
            {
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(Environment.CurrentDirectory, ".verification", "mailjet-icon.png")); png.Save(file);
            }
            statuspageComponents = (await vm.GetComponentsAsync(vm.AllServices.Single(s => s.Id == "statuspage.Cloudflare"), CancellationToken.None)).Count;
        }
        if (!menuVerified || !thirty || !hour || !resumed || !frenchSnooze || !systemTheme || !scoped || !imported || !defaultRestored || !googlePicker
            || network && (mailjetLogo != true || statuspageComponents <= 0)) throw new InvalidOperationException("Native feature verification failed.");
        return new { ServiceMenu = menuVerified, Snooze30 = thirty, Snooze60 = hour, SnoozeResume = resumed, FrenchSnooze = frenchSnooze, SystemTheme = systemTheme,
            ComponentScope = scoped, PreferenceImport = imported, DefaultRestored = defaultRestored, GoogleProducts = products.Count, MailjetLogo = mailjetLogo, StatuspageComponents = statuspageComponents };
    }
    public void TestNotification() => _desktop?.Test(_vm?.NotificationSound ?? true);
    private StatusLookout.Core.BridgeSnapshot HandleStreamDeck(StatusLookout.Core.BridgeRequest request)
    {
        var vm = _vm!;
        if (request.Command == "openDashboard") ShowDashboard();
        else if (request.Command == "refresh") _ = vm.RefreshAsync();
        else if (request.Command is "openService" or "snooze")
        {
            var service = vm.Watchlist.FirstOrDefault(s => s.Id == request.ServiceId) ?? throw new InvalidDataException("This service is not monitored.");
            if (request.Command == "openService") DesktopIntegration.OpenStatus(service.Definition.StatusUrl);
            else vm.Snooze(service, request.Minutes);
        }
        return vm.StreamDeckSnapshot();
    }
    public void ShowDashboard()
    {
        if (MainWindow is null) return;
        MainWindow.Show(); MainWindow.WindowState = WindowState.Normal; MainWindow.Activate();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _bridge?.Dispose(); _vm?.Dispose(); _desktop?.Dispose(); _waitHandle?.Unregister(null); _activation?.Dispose(); _mutex?.Dispose();
        base.OnExit(e);
    }
}
