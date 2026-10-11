using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace StatusLookout.Windows;

/// <summary>Live application resources shared by every window and the tray.</summary>
public static class L
{
    private sealed record Translation(string Key, string En, string Fr);
    private sealed record Palette(string Key, string Dark, string Light);
    private static readonly List<Translation> Strings = Read<List<Translation>>("Translations.json");
    private static readonly List<Palette> Colors = Read<List<Palette>>("Palettes.json");
    public static string Language { get; private set; } = "en";
    public static string Theme { get; private set; } = "dark";
    public static event Action? Updated;
    private static T Read<T>(string name)
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream("StatusLookout.Windows." + name)!;
        return JsonSerializer.Deserialize<T>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
    public static string T(string english) => Language == "fr" ? Strings.FirstOrDefault(s => s.En == english)?.Fr ?? english : english;
    public static string English(string text) => Strings.FirstOrDefault(s => s.Fr == text)?.En ?? text;
    public static string Format(string english, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(english), args);
    public static string Status(Core.ServiceStatus status) => T(status switch
    {
        Core.ServiceStatus.Operational => "Operational", Core.ServiceStatus.Degraded => "Degraded",
        Core.ServiceStatus.Outage => "Outage", Core.ServiceStatus.Maintenance => "Maintenance", _ => "Unavailable"
    });
    public static string Alert(Core.AlertKind kind) => kind == Core.AlertKind.Recovery ? T("Back to normal") : T(kind.ToString());
    public static void Apply(string language, string theme)
    {
        Language = language == "fr" ? "fr" : "en";
        Theme = theme == "light" ? "light" : "dark";
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(Language == "fr" ? "fr-FR" : "en-US");
        var resources = Application.Current.Resources;
        foreach (var entry in Strings) resources[entry.Key] = Language == "fr" ? entry.Fr : entry.En;
        foreach (var entry in Colors)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Theme == "light" ? entry.Light : entry.Dark));
            brush.Freeze(); resources[entry.Key] = brush;
        }
        Updated?.Invoke();
    }
}
