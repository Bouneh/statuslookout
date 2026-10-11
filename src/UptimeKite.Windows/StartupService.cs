using Microsoft.Win32;
namespace UptimeKite.Windows;

public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool IsEnabled
    {
        get { try { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue("UptimeKite") is string; } catch { return false; } }
    }
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true) ?? throw new IOException("The Windows startup key is unavailable.");
        if (!enabled) { key.DeleteValue("UptimeKite", throwOnMissingValue: false); return; }
        var executable = Environment.ProcessPath ?? throw new IOException("The executable path is unavailable.");
        if (!File.Exists(executable)) throw new IOException("Keep UptimeKite.exe in a permanent folder before enabling startup.");
        key.SetValue("UptimeKite", $"\"{executable}\" --background");
    }
}
