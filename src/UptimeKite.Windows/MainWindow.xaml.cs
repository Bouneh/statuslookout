using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
namespace UptimeKite.Windows;

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
        PageTitle.Text = page switch { "Library" => "Service library", "Activity" => "Alert history", "Settings" => "Preferences", _ => "Your watchlist" };
    }
    private void OnClosing(object? sender, CancelEventArgs e) { e.Cancel = true; Hide(); }
    private void HideClick(object sender, RoutedEventArgs e) => Hide();
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private async void RefreshClick(object sender, RoutedEventArgs e) => await _vm.RefreshAsync();
    private void PauseClick(object sender, RoutedEventArgs e) => _vm.TogglePause();
    private void DismissClick(object sender, RoutedEventArgs e) => _vm.DismissError();
    private void OpenServiceClick(object sender, RoutedEventArgs e) { if (sender is Button { Tag: ServiceItem service }) TryOpen(service.Definition.StatusUrl); }
    private void RemoveServiceClick(object sender, RoutedEventArgs e) { if (sender is Button { Tag: ServiceItem service }) service.IsWatched = false; }
    private void GitHubClick(object sender, RoutedEventArgs e) => TryOpen("https://github.com/Bouneh/uptimekite");
    private void TryOpen(string url) { try { DesktopIntegration.OpenStatus(url); } catch (Exception ex) { _vm.ReportError("The status page could not be opened: " + ex.Message); } }
    private void ClearHistoryClick(object sender, RoutedEventArgs e) => _vm.ClearActivity();
    private void TestNotificationClick(object sender, RoutedEventArgs e) => ((App)System.Windows.Application.Current).TestNotification();
    private void DataFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UptimeKite");
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception ex) { _vm.ReportError("The data folder could not be opened: " + ex.Message); }
    }
    private void CustomClick(object sender, RoutedEventArgs e)
    {
        var dialog = new CustomServiceWindow(_vm) { Owner = this }; dialog.ShowDialog();
    }
}
