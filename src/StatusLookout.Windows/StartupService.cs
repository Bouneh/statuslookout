using Microsoft.Win32;
using StatusLookout.Core;
namespace StatusLookout.Windows;

public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                var executable = ResolveExecutable();
                return File.Exists(executable) && StartupCommand.Matches(key?.GetValue("StatusLookout") as string, executable);
            }
            catch { return false; }
        }
    }
    private static string ResolveExecutable()
    {
        var executable = Environment.ProcessPath ?? throw new IOException("The executable path is unavailable.");
        if (string.Equals(Path.GetFileName(executable), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            executable = Path.Combine(AppContext.BaseDirectory, "StatusLookout.exe");
        }
        if (!File.Exists(executable)) throw new IOException("Launch the packaged StatusLookout executable before enabling Windows startup.");
        return executable;
    }
    public static void RepairExistingRegistration()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        var registered = key?.GetValue("StatusLookout") as string;
        // An absent entry is an explicit opt-out. Update only an existing registration.
        if (string.IsNullOrWhiteSpace(registered)) return;
        if (StartupCommand.NeedsRepair(registered, ResolveExecutable())) SetEnabled(true);
    }
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true) ?? throw new IOException("The Windows startup key is unavailable.");
        if (!enabled) { key.DeleteValue("StatusLookout", throwOnMissingValue: false); return; }
        key.SetValue("StatusLookout", StartupCommand.Create(ResolveExecutable()));
    }
}
