using System.Security.Cryptography;
using System.Text;
namespace StatusLookout.Core;

public sealed record BridgeRequest(string Command, string? ServiceId = null, int Minutes = 30);
public sealed record BridgeService(string Id, string Name, ServiceStatus Status, string Message, string StatusUrl,
    bool IsMuted, DateTimeOffset? SnoozedUntil, string? Logo);
public sealed record BridgeSnapshot(int Protocol, string Version, string Language, bool IsDemo, bool IsPaused,
    bool IsRefreshing, ServiceStatus Overall, BridgeService[] Services, BridgeCatalogEntry[]? Catalog = null);
public sealed record BridgeCatalogEntry(string Id, string Name, string Category, bool Watched, string? Family = null);
public sealed record BridgeResponse(bool Ok, BridgeSnapshot? Snapshot = null, string? Error = null);

public static class StreamDeckProtocol
{
    public static string PipeName(string domain, string user) => "StatusLookout.StreamDeck.v1." +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((domain + "\\" + user).ToLowerInvariant())))[..16].ToLowerInvariant();
    public static void Validate(BridgeRequest request)
    {
        if (request.Command is not ("snapshot" or "refresh" or "openDashboard" or "openService" or "snooze" or "watch"))
            throw new InvalidDataException("Unsupported command.");
        if (request.Command is "openService" or "snooze" or "watch" && (string.IsNullOrWhiteSpace(request.ServiceId) || request.ServiceId.Length > 256))
            throw new InvalidDataException("A service ID is required.");
        if (request.Command == "snooze" && request.Minutes is not (0 or 30 or 60)) throw new InvalidDataException("Invalid snooze duration.");
    }
}
