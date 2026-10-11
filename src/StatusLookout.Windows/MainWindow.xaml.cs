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
    private void LicenseClick(object sender, RoutedEventArgs e) => TryOpen("https://github.com/Bouneh/statuslookout/blob/main/LICENSE");
    private void ReleaseClick(object sender, RoutedEventArgs e) => TryOpen(_vm.LatestReleaseUrl);
    private async void CheckUpdatesClick(object sender, RoutedEventArgs e) => await _vm.CheckForUpdatesAsync();
    private async void DownloadUpdateClick(object sender, RoutedEventArgs e) => await _vm.DownloadUpdateAsync();
    private void CancelDownloadClick(object sender, RoutedEventArgs e) => _vm.CancelUpdateDownload();
    private void OpenDownloadClick(object sender, RoutedEventArgs e)
    {
        try { if (_vm.HasDownloadedFile) Process.Start(new ProcessStartInfo(Path.GetDirectoryName(_vm.DownloadedPath)!) { UseShellExecute = true }); }
        catch (Exception ex) { _vm.ReportError(ex.Message); }
    }
    public void ShowPreferences() => NavigateClick(Navigation.Children.OfType<ToggleButton>().Single(b => (string)b.Tag == "Settings"), new RoutedEventArgs());
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
}
