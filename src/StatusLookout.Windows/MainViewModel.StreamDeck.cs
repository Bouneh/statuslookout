using System.Windows.Media;
using System.Windows.Media.Imaging;
using StatusLookout.Core;
namespace StatusLookout.Windows;

public sealed partial class MainViewModel
{
    private readonly Dictionary<ImageSource, string> _bridgeLogos = [];
    public BridgeSnapshot StreamDeckSnapshot()
    {
        string? Logo(ServiceItem item)
        {
            if (item.Logo is not BitmapSource source) return null;
            if (_bridgeLogos.TryGetValue(source, out var value)) return value;
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(source));
            using var data = new MemoryStream(); png.Save(data);
            return _bridgeLogos[source] = "data:image/png;base64," + Convert.ToBase64String(data.ToArray());
        }
        var overall = WatchedCount == 0 || UnknownCount > 0 && IssueCount == 0 ? ServiceStatus.Unknown
            : IssueCount > 0 ? Watchlist.Max(s => s.Status) : ServiceStatus.Operational;
        return new(1, InstalledVersion.ToString(3), Language, IsDemo, IsPaused, IsRefreshing, overall,
            Watchlist.Select(s => new BridgeService(s.Id, s.Name, s.Status, s.Message, s.Definition.StatusUrl, s.IsMuted, s.SnoozedUntil, Logo(s))).ToArray(),
            AllServices.Select(s => new BridgeCatalogEntry(s.Id, s.Name, s.Category, s.IsWatched, s.DefaultDefinition.Family ?? s.Name)).ToArray());
    }
}
