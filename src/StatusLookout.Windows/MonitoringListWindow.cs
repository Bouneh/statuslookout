using System.Windows;
using System.Windows.Controls;
namespace StatusLookout.Windows;

public sealed class MonitoringListWindow : Window
{
    public MonitoringListWindow(MainViewModel vm,bool rename)
    {
        Title=L.T(rename?"Rename list":"New list");Width=440;Height=260;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"Palette22");SetResourceReference(ForegroundProperty,"Palette1");
        var panel=new StackPanel {Margin=new Thickness(24)};Content=panel;
        var label=new TextBlock {Text=L.T("List name"),Margin=new Thickness(0,0,0,8)};label.SetResourceReference(TextBlock.ForegroundProperty,"Palette1");panel.Children.Add(label);
        var name=new TextBox {Text=rename?vm.Lists.Single(list=>list.Id==vm.ActiveListId).Name:"",Padding=new Thickness(10,8,10,8),MaxLength=48};name.SetResourceReference(BackgroundProperty,"Palette0");name.SetResourceReference(ForegroundProperty,"Palette1");panel.Children.Add(name);
        var feedback=new TextBlock {Text=L.T("Choose the services for this list in the catalog."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,14)};feedback.SetResourceReference(TextBlock.ForegroundProperty,"Palette2");panel.Children.Add(feedback);
        var save=new Button {Content=L.T("Save list"),IsDefault=true,Padding=new Thickness(14,8,14,8),HorizontalAlignment=HorizontalAlignment.Right,Foreground=System.Windows.Media.Brushes.White};save.SetResourceReference(BackgroundProperty,"Palette15");panel.Children.Add(save);
        save.Click+=(_,_)=>{try{if(rename)vm.RenameList(name.Text);else vm.CreateList(name.Text);DialogResult=true;}catch(Exception ex)when(ex is InvalidDataException or ArgumentException){feedback.Text=L.T(ex.Message);}};
        Loaded+=(_,_)=>{name.Focus();name.SelectAll();};
    }
}
