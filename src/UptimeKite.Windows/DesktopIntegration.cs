using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Toolkit.Uwp.Notifications;
using UptimeKite.Core;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
namespace UptimeKite.Windows;

public sealed class DesktopIntegration : IDisposable
{
    private readonly Forms.NotifyIcon _tray;
    private readonly Action _show;
    private readonly Action<string> _report;
    private Drawing.Icon? _statusIcon;
    private bool _toastsReady;
    public bool ToastRegistrationReady => _toastsReady;
    public string LastDelivery { get; private set; } = "not attempted";
    public DesktopIntegration(Action show, Action quit, Action refresh, Action togglePause, Action<string> report)
    {
        _show = show; _report = report;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open UptimeKite", null, (_, _) => show());
        menu.Items.Add("Refresh services", null, (_, _) => refresh());
        menu.Items.Add("Pause / resume monitoring", null, (_, _) => togglePause());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit UptimeKite", null, (_, _) => quit());
        _tray = new Forms.NotifyIcon { Text = "UptimeKite", Icon = Drawing.SystemIcons.Information, Visible = true, ContextMenuStrip = menu };
        _tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) show(); };
        _tray.BalloonTipClicked += (_, _) => show();
        try { _ = ToastNotificationManagerCompat.CreateToastNotifier(); _toastsReady = true; }
        catch { _toastsReady = false; }
    }
    public void Show(ServiceItem service, StatusTransition change, bool sound)
    {
        var title = service.Name + " · " + (change.Kind == AlertKind.Recovery ? "Back to normal" : change.Kind.ToString());
        Send(title, change.Message, service.Id, sound);
    }
    public void Test(bool sound) => Send("UptimeKite is listening", "Outage, degradation and recovery alerts will appear here.", "", sound);
    private void Send(string title, string message, string serviceId, bool sound)
    {
        try
        {
            if (!_toastsReady) throw new InvalidOperationException("Windows toast registration is unavailable.");
            var builder = new ToastContentBuilder().AddArgument("service", serviceId).AddText(title).AddText(message.Length > 240 ? message[..237] + "…" : message);
            if (!sound) builder.AddAudio(new ToastAudio { Silent = true });
            builder.Show();
            LastDelivery = "toast API accepted";
        }
        catch
        {
            try { _tray.ShowBalloonTip(8000, title, message, Forms.ToolTipIcon.Info); LastDelivery = "tray balloon fallback"; }
            catch (Exception ex) { _report("Windows could not display a notification: " + ex.Message); }
        }
    }
    public void Update(MainViewModel vm)
    {
        var color = vm.IsPaused ? Drawing.Color.FromArgb(149,164,190) : vm.IssueCount > 0 ? Drawing.Color.FromArgb(255,111,134) : vm.UnknownCount > 0 ? Drawing.Color.FromArgb(255,189,105) : Drawing.Color.FromArgb(86,220,184);
        using var bitmap = new Drawing.Bitmap(32,32);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        using (var brush = new Drawing.SolidBrush(color))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.FillPolygon(brush, [new(16,2), new(29,13), new(16,25), new(3,13)]);
            using var pen = new Drawing.Pen(color,2);
            g.DrawLines(pen, [new(16,25),new(12,29),new(18,31)]);
        }
        var handle = bitmap.GetHicon();
        var next = (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone();
        DestroyIcon(handle);
        _tray.Icon = next;
        _statusIcon?.Dispose(); _statusIcon = next;
        _tray.Text = vm.IsPaused ? "UptimeKite · paused" : $"UptimeKite · {vm.WatchedCount} watched · {vm.IssueCount} issues · {vm.UnknownCount} unavailable";
    }
    public static void OpenStatus(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo))
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
    public void Dispose() { _tray.Visible = false; _tray.Dispose(); _statusIcon?.Dispose(); }
}
