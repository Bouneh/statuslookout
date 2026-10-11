using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StatusLookout.Core;

namespace StatusLookout.Windows;

public static class ServiceLogos
{
    private static readonly Dictionary<string, string> Index = LoadIndex();
    private static readonly Dictionary<string, ImageSource?> Cache = [];
    private static Dictionary<string, string> LoadIndex()
    {
        using var source = typeof(ServiceLogos).Assembly.GetManifestResourceStream("StatusLookout.Windows.Assets.ServiceLogos.index.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(source)!;
    }
    public static ImageSource? Get(ServiceDefinition service)
    {
        if (!Index.TryGetValue(service.Id, out var filename)) return null;
        if (Cache.TryGetValue(filename, out var cached)) return cached;
        try
        {
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 64;
            image.UriSource = new Uri("pack://application:,,,/Assets/ServiceLogos/" + Uri.EscapeDataString(filename));
            image.EndInit(); image.Freeze(); Cache[filename] = image; return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or System.IO.FileFormatException or ArgumentException)
        { Cache[filename] = null; return null; }
    }
}
