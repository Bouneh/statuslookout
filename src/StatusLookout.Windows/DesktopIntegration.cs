using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Toolkit.Uwp.Notifications;
using StatusLookout.Core;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
namespace StatusLookout.Windows;

public sealed class DesktopIntegration : IDisposable
{
    private readonly Forms.NotifyIcon _tray;
    private readonly Action _show;
    private readonly Action<string> _report;
    private Drawing.Icon? _statusIcon;
    private bool _toastsReady;
    private Action? _balloonAction;
    private readonly NotificationAudio _audio;
    public bool ToastRegistrationReady => _toastsReady;
    public string LastDelivery { get; private set; } = "not attempted";
    public DesktopIntegration(Action show, Action quit, Action refresh, Action togglePause, Action<string> report)
    {
        _show = show; _report = report;
        _audio=new(report);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(L.T("Open StatusLookout"), null, (_, _) => show());
        menu.Items.Add(L.T("Refresh services"), null, (_, _) => refresh());
        menu.Items.Add(L.T("Pause / resume monitoring"), null, (_, _) => togglePause());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(L.T("Quit StatusLookout"), null, (_, _) => quit());
        _tray = new Forms.NotifyIcon { Text = "StatusLookout", Icon = Drawing.SystemIcons.Information, Visible = true, ContextMenuStrip = menu };
        _tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) show(); };
        _tray.BalloonTipClicked += (_, _) => (_balloonAction??show)();
        try { _ = ToastNotificationManagerCompat.CreateToastNotifier(); _toastsReady = true; }
        catch { _toastsReady = false; }
    }
    public void Show(IReadOnlyList<ServiceItem> services, StatusTransition change, bool sound,NotificationSoundSelection? selection=null)
    {
        var service = services[0];
        var title = service.Name + " · " + L.Alert(change.Kind);
        if (services.Count > 1) title = L.Format("{0} services · {1}", services.Count, L.Alert(change.Kind));
        var names = string.Join(", ", services.Take(3).Select(s => s.Name.Length > 40 ? s.Name[..37] + "…" : s.Name));
        if (services.Count > 3) names += " · " + L.Format("and {0} more", services.Count - 3);
        var message = services.Count > 1 ? names + "\n" + change.Message : change.Message;
        Send(title, message, services.Count == 1 ? service.Id : "", sound,selection);
    }
    public void Test(bool sound,NotificationSoundSelection? selection=null) => Send(L.T("StatusLookout is listening"), L.T("Outage, degradation and recovery alerts will appear here."), "", sound,selection);
    public void PreviewSound(NotificationSoundSelection selection)=>_audio.Play(selection,true);
    public void StopSound()=>_audio.Stop();
    public void ShowUpdate(AvailableUpdate update,Action showUpdates,bool sound,NotificationSoundSelection? selection=null)
    {
        var title=L.T("A StatusLookout update is available");var message=L.Format("Version {0} is ready to download.",update.Version.ToString(3));
        selection??=new("system",0.7);
        try
        {
            if(!_toastsReady)throw new InvalidOperationException();
            var builder=new ToastContentBuilder().AddArgument("page","updates").AddText(title).AddText(message)
                .AddButton(new ToastButton().SetContent(L.T("Download update")).AddArgument("action","download-update"));
            _audio.ConfigureToast(builder,sound,selection);builder.Show();
        }
        catch {Fallback(title,message,showUpdates,sound,selection);}
        if(sound)_audio.Play(selection);
    }
    private void Send(string title, string message, string serviceId, bool sound,NotificationSoundSelection? selection=null)
    {
        selection??=new("system",0.7);
        try
        {
            if (!_toastsReady) throw new InvalidOperationException("Windows toast registration is unavailable.");
            var builder = new ToastContentBuilder().AddArgument("service", serviceId).AddText(title).AddText(message.Length > 240 ? message[..237] + "…" : message);
            _audio.ConfigureToast(builder,sound,selection);
            builder.Show();
            LastDelivery = "toast API accepted";
        }
        catch
        {
            Fallback(title,message,_show,sound,selection);
        }
        if(sound)_audio.Play(selection);
    }
    private void Fallback(string title,string message,Action activate,bool sound,NotificationSoundSelection selection)
    {
        try
        {
            if(sound&&_audio.Resolve(selection).Key=="system"){_balloonAction=activate;_tray.ShowBalloonTip(8000,title,message,Forms.ToolTipIcon.Info);LastDelivery="tray balloon fallback";}
            else {NotificationPopup.Display(title,message,activate);LastDelivery="quiet popup fallback";}
        }
        catch(Exception ex){_report(L.T("Windows could not display a notification: ")+ex.Message);}
    }
    public void Update(MainViewModel vm)
    {
        var color = vm.IsPaused ? Drawing.Color.FromArgb(149,164,190) : vm.IssueCount > 0 ? Drawing.Color.FromArgb(255,111,134) : vm.UnknownCount > 0 ? Drawing.Color.FromArgb(255,189,105) : Drawing.Color.FromArgb(86,220,184);
        using var bitmap = new Drawing.Bitmap(32,32);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        using (var brush = new Drawing.SolidBrush(color))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var ring = new Drawing.Pen(color, 3);
            g.DrawEllipse(ring, 3, 3, 26, 26);
            g.FillEllipse(brush, 12, 12, 8, 8);
            g.FillEllipse(brush, 23, 5, 6, 6);
            using var pen = new Drawing.Pen(color,2);
            g.DrawLine(pen, 16, 16, 26, 6);
        }
        var handle = bitmap.GetHicon();
        var next = (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone();
        DestroyIcon(handle);
        _tray.Icon = next;
        _statusIcon?.Dispose(); _statusIcon = next;
        _tray.Text = vm.IsPaused ? L.T("StatusLookout · paused") : L.Format("StatusLookout · {0} watched · {1} issues · {2} unavailable", vm.WatchedCount, vm.IssueCount, vm.UnknownCount);
        var menu = _tray.ContextMenuStrip!;
        menu.Items[0].Text = L.T("Open StatusLookout"); menu.Items[1].Text = L.T("Refresh services");
        menu.Items[2].Text = L.T("Pause / resume monitoring"); menu.Items[4].Text = L.T("Quit StatusLookout");
    }
    public static void OpenStatus(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo))
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
    public void Dispose() { _audio.Dispose();_tray.Visible = false; _tray.Dispose(); _statusIcon?.Dispose(); }
}
