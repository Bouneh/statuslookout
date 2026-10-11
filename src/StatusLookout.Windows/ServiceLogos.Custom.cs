using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StatusLookout.Core;
namespace StatusLookout.Windows;

public static partial class ServiceLogos
{
    private static readonly HttpClient IconHttp = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly SemaphoreSlim IconGate = new(3, 3);
    private static ImageSource DecodeIcon(byte[] bytes)
    {
        using var source = new MemoryStream(bytes);
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 64; image.StreamSource = source; image.EndInit(); image.Freeze(); return image;
    }
    private static async Task<byte[]> ReadIconSourceAsync(Uri uri, int maximum, CancellationToken token)
    {
        for (var redirect = 0; redirect < 5; redirect++)
        {
            if (uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidDataException("Invalid icon URL.");
            using var response = await IconHttp.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } next)
            { uri = new Uri(uri, next); continue; }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("Icon response is too large.");
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var data = new MemoryStream(); var buffer = new byte[8192]; int read;
            while ((read = await stream.ReadAsync(buffer, token)) > 0)
            { if (data.Length + read > maximum) throw new InvalidDataException("Icon response is too large."); data.Write(buffer, 0, read); }
            return data.ToArray();
        }
        throw new InvalidDataException("Too many icon redirects.");
    }
    public static async Task<ImageSource?> GetCustomAsync(ServiceDefinition service, CancellationToken token)
    {
        if (Get(service) is { } bundled) return bundled;
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StatusLookout", "logos");
        var file = Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(service.StatusUrl))) + ".icon");
        ImageSource? cached = null;
        try
        {
            if (File.Exists(file) && new FileInfo(file).Length <= 512 * 1024)
            { cached = DecodeIcon(await File.ReadAllBytesAsync(file, token)); if (File.GetLastWriteTimeUtc(file) > DateTime.UtcNow.AddDays(-30)) return cached; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FileFormatException) { }
        await IconGate.WaitAsync(token);
        try
        {
            var origin = new Uri(service.StatusUrl);
            var candidates = new List<Uri>();
            try
            {
                var markup = Encoding.UTF8.GetString(await ReadIconSourceAsync(origin, 1024 * 1024, token));
                foreach (Match link in Regex.Matches(markup, @"<link\b[^>]*>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
                {
                    var rel = Regex.Match(link.Value, "\\brel\\s*=\\s*['\"]([^'\"]+)['\"]", RegexOptions.IgnoreCase).Groups[1].Value;
                    var href = WebUtility.HtmlDecode(Regex.Match(link.Value, "\\bhref\\s*=\\s*['\"]([^'\"]+)['\"]", RegexOptions.IgnoreCase).Groups[1].Value);
                    if (rel.Split(' ').Any(s => s.Equals("icon", StringComparison.OrdinalIgnoreCase) || s.Equals("apple-touch-icon", StringComparison.OrdinalIgnoreCase))
                        && Uri.TryCreate(origin, href, out var icon) && !icon.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) candidates.Add(icon);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException or RegexMatchTimeoutException) { token.ThrowIfCancellationRequested(); }
            candidates.Add(new Uri(origin, "/favicon.ico"));
            foreach (var icon in candidates.Distinct().Take(5))
            {
                try
                {
                    var bytes = await ReadIconSourceAsync(icon, 512 * 1024, token); var image = DecodeIcon(bytes);
                    try { Directory.CreateDirectory(directory); await File.WriteAllBytesAsync(file, bytes, token); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    return image;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or NotSupportedException or ArgumentException or TaskCanceledException or FileFormatException)
                { token.ThrowIfCancellationRequested(); }
            }
            return cached;
        }
        finally { IconGate.Release(); }
    }
}
