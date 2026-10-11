using System.Net;
namespace UptimeKite.Core;

public sealed class StatusMonitor(HttpClient http)
{
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    public async Task<IReadOnlyList<ServiceSnapshot>> CheckAsync(IEnumerable<ServiceDefinition> definitions, CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            using var concurrency = new SemaphoreSlim(6, 6);
            var tasks = definitions.GroupBy(s => s.ApiUrl).Select(async group =>
            {
                await concurrency.WaitAsync(cancellationToken);
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    string? json = null; string? error = null;
                    try
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, group.Key);
                        request.Headers.UserAgent.ParseAdd("UptimeKite/0.1 (+https://github.com/Bouneh/uptimekite)");
                        request.Headers.Accept.ParseAdd("application/json");
                        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                        response.EnsureSuccessStatusCode();
                        if (response.Content.Headers.ContentLength > 16 * 1024 * 1024) throw new InvalidDataException("Status response is too large.");
                        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                        using var buffer = new MemoryStream();
                        var bytes = new byte[16384];
                        int read;
                        while ((read = await stream.ReadAsync(bytes, timeout.Token)) > 0)
                        {
                            if (buffer.Length + read > 16 * 1024 * 1024) throw new InvalidDataException("Status response is too large.");
                            buffer.Write(bytes, 0, read);
                        }
                        json = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { error = "Status check timed out. This does not mean the service is down."; }
                    catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
                    { error = "Unable to reach the status API. " + ex.Message; }
                    cancellationToken.ThrowIfCancellationRequested();
                    return group.Select(service =>
                    {
                        if (json is null) return new ServiceSnapshot(service.Id, ServiceStatus.Unknown, error ?? "Unable to check status.", DateTimeOffset.UtcNow);
                        try { return StatusParser.Parse(service, json); }
                        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or InvalidOperationException or FormatException)
                        { return new ServiceSnapshot(service.Id, ServiceStatus.Unknown, "Status data could not be read. " + ex.Message, DateTimeOffset.UtcNow); }
                    }).ToArray();
                }
                finally { concurrency.Release(); }
            });
            return (await Task.WhenAll(tasks)).SelectMany(s => s).ToArray();
        }
        finally { _refreshGate.Release(); }
    }
}
