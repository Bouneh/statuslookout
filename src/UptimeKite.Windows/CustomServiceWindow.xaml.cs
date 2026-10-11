using System.Windows;
using System.Windows.Controls;
namespace UptimeKite.Windows;
public partial class CustomServiceWindow : Window
{
    private readonly MainViewModel _vm;
    public CustomServiceWindow(MainViewModel vm) { _vm = vm; InitializeComponent(); }
    private async void AddClick(object sender, RoutedEventArgs e)
    {
        AddButton.IsEnabled = false; Feedback.Text = "Checking the public status API…";
        var provider = ((ComboBoxItem)Provider.SelectedItem).Tag.ToString()!;
        var added = await _vm.AddCustomAsync(ServiceName.Text, StatusUrl.Text, provider, Components.Text);
        if (!IsVisible) return;
        if (added) { DialogResult = true; return; }
        Feedback.Text = _vm.ErrorMessage; AddButton.IsEnabled = true;
    }
}
