using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
namespace StatusLookout.Windows;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    public MainWindow(MainViewModel vm) { _vm = vm; InitializeComponent(); DataContext = vm; }
    private void NavigateClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton selected) return;
        foreach (var button in Navigation.Children.OfType<ToggleButton>()) button.IsChecked = button == selected;
        var page = selected.Tag?.ToString();
        WatchPage.Visibility = page == "Watch" ? Visibility.Visible : Visibility.Collapsed;
        LibraryPage.Visibility = page == "Library" ? Visibility.Visible : Visibility.Collapsed;
        ActivityPage.Visibility = page == "Activity" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = page == "About" ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty, page switch { "Library" => "Ui094", "Activity" => "Ui095", "Settings" => "Ui096", "About" => "AboutTitle", _ => "Ui014" });
    }
    public void ShowAbout() => NavigateClick(Navigation.Children.OfType<ToggleButton>().Single(b => (string)b.Tag == "About"), new RoutedEventArgs());
    private void ProfileClick(object sender, RoutedEventArgs e) => TryOpen("https://github.com/Bouneh");
    private void NewListClick(object sender,RoutedEventArgs e)=>new MonitoringListWindow(_vm,false){Owner=this}.ShowDialog();
    private void RenameListClick(object sender,RoutedEventArgs e)=>new MonitoringListWindow(_vm,true){Owner=this}.ShowDialog();
    private void DeleteListClick(object sender,RoutedEventArgs e)
    {
        if(_vm.CanEditList&&MessageBox.Show(this,L.T("Delete this list? Its services remain available in the catalog."),L.T("Delete list"),MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes)_vm.DeleteList();
    }
    private void ViewUpdateClick(object sender,RoutedEventArgs e)=>ShowAbout();
    private void LicenseClick(object sender, RoutedEventArgs e) => TryOpen("https://github.com/Bouneh/statuslookout/blob/main/LICENSE");
    private void ReleaseClick(object sender, RoutedEventArgs e) => TryOpen(_vm.LatestReleaseUrl);
    private async void CheckUpdatesClick(object sender, RoutedEventArgs e) => await _vm.CheckForUpdatesAsync();
    private async void DownloadUpdateClick(object sender, RoutedEventArgs e) => await _vm.DownloadUpdateAsync();
    private void CancelDownloadClick(object sender, RoutedEventArgs e) => _vm.CancelUpdateDownload();
    private async void InstallUpdateClick(object sender, RoutedEventArgs e)
    {
        if (!_vm.CanInstallUpdate) return;
        if (MessageBox.Show(this, L.T("Close StatusLookout and open the installer? Your preferences will be kept."), L.T("Install update"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (await _vm.LaunchUpdateInstallerAsync()) Application.Current.Shutdown();
    }
    private void OpenDownloadClick(object sender, RoutedEventArgs e)
    {
        try { if (_vm.HasDownloadedFile) Process.Start(new ProcessStartInfo(Path.GetDirectoryName(_vm.DownloadedPath)!) { UseShellExecute = true }); }
        catch (Exception ex) { _vm.ReportError(ex.Message); }
    }
    public void ShowPreferences() => NavigateClick(Navigation.Children.OfType<ToggleButton>().Single(b => (string)b.Tag == "Settings"), new RoutedEventArgs());
    public void ShowLibrary() => NavigateClick(Navigation.Children.OfType<ToggleButton>().Single(b => (string)b.Tag == "Library"), new RoutedEventArgs());
    private void SelectFamilyClick(object sender,RoutedEventArgs e) {if(sender is CheckBox {DataContext:ServiceFamily group})_vm.SetFamilyWatched(group,group.IsSelected!=true);}
    private void ExpandFamiliesClick(object sender,RoutedEventArgs e)=>_vm.ExpandFamilies(true);
    private void CollapseFamiliesClick(object sender,RoutedEventArgs e)=>_vm.ExpandFamilies(false);
    private void OnClosing(object? sender, CancelEventArgs e) { e.Cancel = true; Hide(); }
    private void HideClick(object sender, RoutedEventArgs e) => Hide();
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private async void RefreshClick(object sender, RoutedEventArgs e) => await _vm.RefreshAsync();
    private void PauseClick(object sender, RoutedEventArgs e) => _vm.TogglePause();
    private void DismissClick(object sender, RoutedEventArgs e) => _vm.DismissError();
    private void OpenServiceClick(object sender, RoutedEventArgs e) { if (sender is Button { Tag: ServiceItem service }) TryOpen(service.Definition.StatusUrl); }
    private void RemoveServiceClick(object sender, RoutedEventArgs e) { if (sender is Button { Tag: ServiceItem service }) service.IsWatched = false; }
    private void GitHubClick(object sender, RoutedEventArgs e) => TryOpen("https://github.com/Bouneh/statuslookout");
    private void TryOpen(string url) { try { DesktopIntegration.OpenStatus(url); } catch (Exception ex) { _vm.ReportError(L.T("The status page could not be opened: ") + ex.Message); } }
    private void ClearHistoryClick(object sender, RoutedEventArgs e) => _vm.ClearActivity();
    private void TestNotificationClick(object sender, RoutedEventArgs e) => ((App)System.Windows.Application.Current).TestNotification();
    private void PreviewSoundClick(object sender,RoutedEventArgs e){if(sender is Button{Tag:SoundEventItem item})((App)System.Windows.Application.Current).PreviewSound(item.Key);}
    private async void ImportSoundClick(object sender,RoutedEventArgs e)
    {
        if(sender is not Button{Tag:SoundEventItem item})return;
        var dialog=new Microsoft.Win32.OpenFileDialog{Filter="WAV (*.wav)|*.wav",CheckFileExists=true,Multiselect=false,Title=L.T("Import a notification sound")};
        if(dialog.ShowDialog(this)!=true)return;
        try{await _vm.ImportSoundAsync(item,dialog.FileName);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException){_vm.ReportError(L.T(ex.Message));}
    }
    public void ShowSounds()
    {
        ShowPreferences();SoundSettingsExpander.IsExpanded=true;
        Dispatcher.BeginInvoke(new Action(()=>{UpdateLayout();SoundSettingsExpander.BringIntoView(new Rect(0,0,SoundSettingsExpander.ActualWidth,Math.Min(150,SoundSettingsExpander.ActualHeight)));}),System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private void DataFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StatusLookout");
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception ex) { _vm.ReportError(L.T("The data folder could not be opened: ") + ex.Message); }
    }
    private void CustomClick(object sender, RoutedEventArgs e)
    {
        var dialog = new CustomServiceWindow(_vm) { Owner = this }; dialog.ShowDialog();
    }
    private void ServiceOptionsClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button) { menu.PlacementTarget = button; menu.IsOpen = true; }
    }
    private void SnoozeClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: ServiceItem service } item && int.TryParse(item.Tag?.ToString(), out var minutes)) _vm.Snooze(service, minutes);
    }
    private void ComponentsClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: ServiceItem service }) new ComponentPickerWindow(_vm, service) { Owner = this }.ShowDialog();
    }
    private void ExportPreferencesClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "StatusLookout-preferences.json", Filter = "JSON (*.json)|*.json", DefaultExt = ".json" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, _vm.ExportPreferences()); TransferFeedback.Text = L.T("Preferences exported."); }
        catch (Exception ex) { _vm.ReportError(L.T("Preferences could not be exported: ") + ex.Message); }
    }
    private void ImportPreferencesClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "JSON (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 4 * 1024 * 1024) throw new InvalidDataException(L.T("The preferences file is too large."));
            _vm.ImportPreferences(File.ReadAllText(dialog.FileName)); TransferFeedback.Text = L.T("Preferences imported.");
        }
        catch (Exception ex) { _vm.ReportError(L.T("Preferences could not be imported: ") + L.T(ex.Message)); }
    }
}
