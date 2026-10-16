using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace StatusLookout.Windows;

/// <summary>Quiet fallback when Windows toast registration is unavailable.
/// Unlike tray balloons, it does not add an uncontrollable second system sound.</summary>
internal sealed class NotificationPopup : Window
{
    private static NotificationPopup? _current;
    public static void Display(string title,string message,Action activate)
    {
        if(SHQueryUserNotificationState(out var state)<0||state!=5)return;
        _current?.Close();var popup=new NotificationPopup(title,message,activate);_current=popup;popup.Show();
    }
    private NotificationPopup(string title,string message,Action activate)
    {
        Width=360;SizeToContent=SizeToContent.Height;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=false;Topmost=true;AllowsTransparency=true;Background=Brushes.Transparent;
        var border=new Border{CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1),Padding=new Thickness(18)};border.SetResourceReference(Border.BackgroundProperty,"Palette22");border.SetResourceReference(Border.BorderBrushProperty,"Palette23");Content=border;
        var panel=new StackPanel();border.Child=panel;
        var brand=new TextBlock{Text="StatusLookout",FontSize=11,Margin=new Thickness(0,0,0,9)};brand.SetResourceReference(TextBlock.ForegroundProperty,"Palette2");panel.Children.Add(brand);
        foreach(var (text,size) in new[]{(title,14d),(message,12d)}){var label=new TextBlock{Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,7)};label.SetResourceReference(TextBlock.ForegroundProperty,"Palette1");panel.Children.Add(label);}
        MouseLeftButtonUp+=(_,_)=>{Close();activate();};
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(8)};timer.Tick+=(_,_)=>Close();
        Loaded+=(_,_)=>{Left=SystemParameters.WorkArea.Right-ActualWidth-18;Top=SystemParameters.WorkArea.Bottom-ActualHeight-18;timer.Start();};
        Closed+=(_,_)=>{timer.Stop();if(ReferenceEquals(_current,this))_current=null;};
    }
    [DllImport("shell32.dll")]private static extern int SHQueryUserNotificationState(out int state);
}
