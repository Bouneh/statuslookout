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
        _vm = new((service, change, sound) => _desktop?.Show(service, change, sound,_vm?.SoundFor(change.Kind.ToString())), demo);
        if (demo) { if (e.Args.Contains("--french")) _vm.Language = "fr"; if (e.Args.Contains("--light")) _vm.Theme = "light"; }
        MainWindow = new MainWindow(_vm);
        if (demo && e.Args.Contains("--about")) ((MainWindow)MainWindow).ShowAbout();
        if (demo && e.Args.Contains("--settings")) ((MainWindow)MainWindow).ShowPreferences();
        if(demo&&e.Args.Contains("--sounds")){MainWindow.Loaded+=(_,_)=>((MainWindow)MainWindow).ShowSounds();}
        if (demo && e.Args.Contains("--library")) { ((MainWindow)MainWindow).ShowLibrary(); if(e.Args.Contains("--google")) _vm.Search="Google"; }
        if (demo && e.Args.Contains("--compact")) {MainWindow.Width=940;MainWindow.Height=670;}
        if (demo && e.Args.Contains("--collapsed")) _vm.ExpandFamilies(false);
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
            _vm.SoundsMuted+=()=>_desktop.StopSound();
            _vm.UpdateAvailable+=(update)=>_desktop.ShowUpdate(update,()=>{ShowDashboard();if(MainWindow is MainWindow window)window.ShowAbout();},_vm.NotificationSound,_vm.SoundFor("Update"));
            _desktop.Update(_vm);
            ToastNotificationManagerCompat.OnActivated += args =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    ShowDashboard();
                    var values = ToastArguments.Parse(args.Argument);
                    if(values.TryGetValue("page",out var page)&&page=="updates"&&MainWindow is MainWindow updateWindow)updateWindow.ShowAbout();
                    if(values.TryGetValue("action",out var action)&&action=="download-update") _=DownloadFromNotificationAsync();
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
        var soundFeatures=await FeatureSoundChecks.RunAsync(vm,(MainWindow)MainWindow!);
        var menu = (System.Windows.Controls.ContextMenu)MainWindow.Resources["ServiceMenu"];
        var target = new System.Windows.Controls.Button { Tag = service, ContextMenu = menu };
        menu.PlacementTarget = target; menu.IsOpen = true;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        ((System.Windows.Controls.MenuItem)menu.Items[0]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        var menuVerified = service.IsSnoozed; menu.IsOpen = false;
        var openaiItem=vm.AllServices.Single(s=>s.Id=="extra.OpenAI");target.Tag=openaiItem;menu.IsOpen=true;
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        var openaiComponentsEnabled=((System.Windows.Controls.MenuItem)menu.Items[4]).IsEnabled&&openaiItem.SupportsComponents;
        menu.IsOpen=false;if(!openaiComponentsEnabled)throw new InvalidOperationException("OpenAI component picker is disabled.");
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
        vm.Search="Google";var googleFamily=vm.LibraryFamilies.Single(g=>g.Name=="Google");
        var hierarchy=googleFamily.HasChildren&&googleFamily.IsExpanded&&googleFamily.VisibleMembers.Count>200;
        ((MainWindow)MainWindow).ShowLibrary();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);MainWindow.UpdateLayout();
        IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) {var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}
        }
        var groupCheckbox=Descendants(MainWindow).OfType<System.Windows.Controls.CheckBox>().Single(box=>ReferenceEquals(box.DataContext,googleFamily));
        var libraryList=Descendants(MainWindow).OfType<System.Windows.Controls.ListBox>().Single(list=>list.Name=="LibraryList");
        var libraryScroll=Descendants(libraryList).OfType<System.Windows.Controls.ScrollViewer>().First();
        libraryScroll.LineDown();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);MainWindow.UpdateLayout();
        var pixelScroll=libraryScroll.ScrollableHeight>4000&&libraryScroll.VerticalOffset>0&&libraryScroll.VerticalOffset<libraryScroll.ViewportHeight;
        async Task<bool> RevealChild(ServiceItem child)
        {
            var label=Descendants(libraryList).OfType<System.Windows.Controls.TextBlock>().First(t=>ReferenceEquals(t.DataContext,child)&&t.Text==child.Name);
            label.BringIntoView();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);MainWindow.UpdateLayout();
            var position=label.TransformToAncestor(libraryScroll).Transform(new Point(0,label.ActualHeight/2));
            return position.Y>=0&&position.Y<libraryScroll.ActualHeight&&googleFamily.IsExpanded;
        }
        var middleVisible=await RevealChild(googleFamily.VisibleMembers[googleFamily.VisibleMembers.Count/2]);
        var lastVisible=await RevealChild(googleFamily.VisibleMembers[^1]);
        libraryScroll.ScrollToTop();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);MainWindow.UpdateLayout();
        if(!pixelScroll||!middleVisible||!lastVisible)throw new InvalidOperationException($"Library scrolling failed: pixels={pixelScroll}, middle={middleVisible}, last={lastVisible}.");
        groupCheckbox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        var selectAll=googleFamily.IsSelected==true&&googleFamily.Members.All(s=>s.IsWatched);
        googleFamily.Members[0].IsWatched=false;var partial=googleFamily.IsSelected is null;
        var selectedCount=googleFamily.Members.Count(s=>s.IsWatched);var familyExport=vm.ExportPreferences();vm.ImportPreferences(familyExport);
        googleFamily=vm.LibraryFamilies.Single(g=>g.Name=="Google");var familyPersisted=googleFamily.Members.Count(s=>s.IsWatched)==selectedCount;
        vm.ExpandFamilies(false);var collapsed=!googleFamily.IsExpanded;vm.ExpandFamilies(true);var expanded=googleFamily.IsExpanded;
        vm.SetFamilyWatched(googleFamily,false);var clearAll=googleFamily.IsSelected==false&&googleFamily.Members.All(s=>!s.IsWatched);
        vm.Search="No matching service 64f112";var noMatches=vm.IsLibraryEmpty;vm.Search="";
        if(!hierarchy||!selectAll||!partial||!familyPersisted||!collapsed||!expanded||!clearAll||!noMatches)throw new InvalidOperationException("Library hierarchy verification failed.");
        var workSelection=vm.Watchlist.Select(s=>s.Id).ToHashSet();vm.ActiveListId="personal";
        var personalEmpty=vm.WatchedCount==0&&vm.Lists.Count==2;
        vm.AllServices.Single(s=>s.Id=="statuspage.Discord").IsWatched=true;
        vm.CreateList("Studio");var customId=vm.ActiveListId;var customEmpty=vm.WatchedCount==0;
        vm.AllServices.Single(s=>s.Id=="statuspage.GitHub").IsWatched=true;vm.RenameList("Streaming");
        var customName=vm.Lists.Single(l=>l.Id==customId).Name=="Streaming";
        vm.ImportPreferences(vm.ExportPreferences());var listsPersisted=vm.ActiveListId==customId&&vm.Watchlist.Select(s=>s.Id).SequenceEqual(["statuspage.GitHub"]);
        vm.ActiveListId="work";var workPreserved=workSelection.SetEquals(vm.Watchlist.Select(s=>s.Id));vm.ActiveListId="personal";
        var personalPreserved=vm.Watchlist.Select(s=>s.Id).SequenceEqual(["statuspage.Discord"]);
        vm.Language="fr";var translatedLists=vm.Lists.Single(l=>l.Id=="work").Name=="Travail"&&vm.Lists.Single(l=>l.Id=="personal").Name=="Personnel";
        vm.ActiveListId=customId;vm.DeleteList();var listDeleted=vm.ActiveListId=="work"&&vm.Lists.Count==2;
        if(!personalEmpty||!customEmpty||!customName||!listsPersisted||!workPreserved||!personalPreserved||!translatedLists||!listDeleted)throw new InvalidOperationException("Monitoring list verification failed.");
        using var updateHandler=new FeatureUpdateHandler();using var updateVm=new MainViewModel((_,_,_)=>{},true,new HttpClient(updateHandler));
        var updateNotifications=0;updateVm.UpdateAvailable+=_=>updateNotifications++;
        await updateVm.CheckForUpdatesAsync(true);var updateOffered=updateVm.HasAvailableUpdate&&updateVm.CanDownloadUpdate&&updateNotifications==1;
        await updateVm.CheckForUpdatesAsync(true);var noRepeatedUpdateAlert=updateNotifications==1;
        updateVm.AutomaticUpdates=false;updateHandler.VersionMajor++;await updateVm.CheckForUpdatesAsync(true);var updateOptOut=updateNotifications==1;
        updateVm.AutomaticUpdates=true;await updateVm.CheckForUpdatesAsync(true);var nextVersionAlert=updateNotifications==2;
        updateHandler.Fail=true;await updateVm.CheckForUpdatesAsync(true);var updateFailure=updateNotifications==2&&updateVm.UpdateStatus.StartsWith(L.T("Update failed: ").TrimEnd());
        if(!updateOffered||!noRepeatedUpdateAlert||!updateOptOut||!nextVersionAlert||!updateFailure)throw new InvalidOperationException("Automatic update verification failed.");
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
        return new { SoundFeatures=soundFeatures, ServiceMenu = menuVerified, Snooze30 = thirty, Snooze60 = hour, SnoozeResume = resumed, FrenchSnooze = frenchSnooze, SystemTheme = systemTheme,
            ComponentScope = scoped, OpenAiComponentsEnabled=openaiComponentsEnabled, PreferenceImport = imported, DefaultRestored = defaultRestored, GoogleProducts = products.Count, LibraryHierarchy=hierarchy, LibraryPixelScroll=pixelScroll, LibraryMiddleChildVisible=middleVisible, LibraryLastChildVisible=lastVisible, FamilySelectAll=selectAll, FamilyPartialSelection=partial, FamilyPreferences=familyPersisted, FamilyCollapse=collapsed, FamilyExpand=expanded, FamilyClearAll=clearAll, LibraryEmptySearch=noMatches, MonitoringLists=listsPersisted&&workPreserved&&personalPreserved&&translatedLists&&listDeleted, AutomaticUpdateOffer=updateOffered, AutomaticUpdateNoRepeat=noRepeatedUpdateAlert, AutomaticUpdateOptOut=updateOptOut, AutomaticUpdateNextVersion=nextVersionAlert, AutomaticUpdateFailure=updateFailure, MailjetLogo = mailjetLogo, StatuspageComponents = statuspageComponents };
    }
    public void TestNotification() => _desktop?.Test(_vm?.NotificationSound ?? true,_vm?.SoundFor("Outage"));
    public void PreviewSound(string key){if(_vm is not null&&_vm.NotificationSound)_desktop?.PreviewSound(_vm.SoundFor(key));}
    private async Task DownloadFromNotificationAsync()
    {
        if(_vm is null)return;
        if(MainWindow is MainWindow window)window.ShowAbout();
        if(!_vm.CanDownloadUpdate)await _vm.CheckForUpdatesAsync();
        await _vm.DownloadUpdateAsync();
    }
    private StatusLookout.Core.BridgeSnapshot HandleStreamDeck(StatusLookout.Core.BridgeRequest request)
    {
        var vm = _vm!;
        if (request.Command == "openDashboard") ShowDashboard();
        else if (request.Command == "watch")
        {
            var service = vm.AllServices.FirstOrDefault(s => s.Id == request.ServiceId) ?? throw new InvalidDataException("Unknown catalog service.");
            service.IsWatched = true;
        }
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
