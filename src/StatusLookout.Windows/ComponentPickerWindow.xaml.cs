using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using StatusLookout.Core;
namespace StatusLookout.Windows;

public partial class ComponentPickerWindow : Window
{
    public sealed class Choice(string id, string name, bool selected) : ObservableObject
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        private bool _selected = selected;
        public bool Selected { get => _selected; set { _selected = value; Changed(); } }
    }
    private readonly MainViewModel _vm;
    private readonly ServiceItem _service;
    private readonly CancellationTokenSource _cancel = new();
    private Choice[] _choices = [];
    public ComponentPickerWindow(MainViewModel vm, ServiceItem service)
    {
        _vm = vm; _service = service; InitializeComponent(); ServiceTitle.Text = service.Name;
        if (service.Definition.Provider == "aws") ScopeExplanation.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty, "AwsScopeExplanation");
        Closed += (_, _) => _cancel.Cancel(); Loaded += async (_, _) => await LoadAsync();
    }
    private async Task LoadAsync()
    {
        Feedback.Text = L.T("Loading provider components…");
        try
        {
            var components = await _vm.GetComponentsAsync(_service, _cancel.Token);
            if (_cancel.IsCancellationRequested) return;
            _choices = components.Select(c => new Choice(c.Id, c.Name, _service.Definition.Components?.Contains(c.Id) == true || _service.Definition.Components?.Contains(c.Name) == true)).ToArray();
            Choices.ItemsSource = _choices; SearchChanged(this, new TextChangedEventArgs(TextBox.TextChangedEvent, UndoAction.None));
            Feedback.Text = _choices.Length == 0 ? L.T("This provider does not expose selectable components.") : L.Format("{0} available components", _choices.Length);
            var selected = _service.Definition.Components;
            if (selected?.Any(id => !_choices.Any(c => c.Id == id || c.Name == id)) == true)
                Feedback.Text = L.T("Some saved components are no longer available. Choose a new selection.");
            SaveButton.IsEnabled = _choices.Length > 0;
        }
        catch (OperationCanceledException) when (_cancel.IsCancellationRequested) { }
        catch (Exception ex) { Feedback.Text = L.T("Components could not be loaded: ") + ex.Message; }
    }
    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        if (Choices?.ItemsSource is null) return;
        CollectionViewSource.GetDefaultView(Choices.ItemsSource).Filter = item => item is Choice choice && choice.Name.Contains(SearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase);
    }
    private void AllClick(object sender, RoutedEventArgs e) { foreach (var choice in _choices) choice.Selected = false; }
    private void DefaultClick(object sender, RoutedEventArgs e) { _vm.SetComponents(_service, null); DialogResult = true; }
    private void SaveClick(object sender, RoutedEventArgs e) { _vm.SetComponents(_service, _choices.Where(c => c.Selected).Select(c => c.Id).ToArray()); DialogResult = true; }
    private void CancelClick(object sender, RoutedEventArgs e) => Close();
}
