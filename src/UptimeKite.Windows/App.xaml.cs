using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Toolkit.Uwp.Notifications;
namespace UptimeKite.Windows;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _activation;
    private RegisteredWaitHandle? _waitHandle;
    private MainViewModel? _vm;
    private DesktopIntegration? _desktop;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var previewIndex = Array.IndexOf(e.Args, "--preview");
        var preview = previewIndex >= 0 && e.Args.Length > previewIndex + 1 ? e.Args[previewIndex + 1] : null;
        var smokeIndex = Array.IndexOf(e.Args, "--smoke-test");
        var smoke = smokeIndex >= 0 && e.Args.Length > smokeIndex + 1 ? e.Args[smokeIndex + 1] : null;
        var demo = e.Args.Contains("--demo") || preview is not null || smoke is not null;
        if (preview is null && smoke is null)
        {
            _mutex = new Mutex(true, @"Local\UptimeKite.Instance", out var first);
            if (!first)
            {
                try { using var signal = EventWaitHandle.OpenExisting(@"Local\UptimeKite.Activate"); signal.Set(); } catch { }
                Shutdown(); return;
            }
            _activation = new(false, EventResetMode.AutoReset, @"Local\UptimeKite.Activate");
            _waitHandle = ThreadPool.RegisterWaitForSingleObject(_activation, (_, _) => Dispatcher.BeginInvoke(ShowDashboard), null, Timeout.Infinite, false);
        }
        _vm = new((service, change, sound) => _desktop?.Show(service, change, sound), demo);
        MainWindow = new MainWindow(_vm);
        if (preview is null)
        {
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
                            catch (Exception ex) { _vm.ReportError("The status page could not be opened: " + ex.Message); }
                        }
                    }
                });
            };
        }
        if (preview is not null)
        {
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
                    _desktop!.Test(false);
                    await Task.Delay(1000);
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(smoke))!);
                    File.WriteAllText(smoke, System.Text.Json.JsonSerializer.Serialize(new
                    {
                        CatalogCount = _vm.CatalogCount, WatchlistCount = _vm.WatchedCount,
                        TrayInitialized = true, _desktop.ToastRegistrationReady, _desktop.LastDelivery
                    }, UptimeKite.Core.JsonStore.Options));
                    Shutdown();
                });
            }
        }
    }
    public void TestNotification() => _desktop?.Test(_vm?.NotificationSound ?? true);
    public void ShowDashboard()
    {
        if (MainWindow is null) return;
        MainWindow.Show(); MainWindow.WindowState = WindowState.Normal; MainWindow.Activate();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _vm?.Dispose(); _desktop?.Dispose(); _waitHandle?.Unregister(null); _activation?.Dispose(); _mutex?.Dispose();
        base.OnExit(e);
    }
}
