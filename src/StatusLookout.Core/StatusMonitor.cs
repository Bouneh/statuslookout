using System.Net;
namespace StatusLookout.Core;

public sealed class StatusMonitor(HttpClient http)
{
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    public async Task<IReadOnlyList<ProviderComponent>> GetComponentsAsync(ServiceDefinition service, CancellationToken token = default)
    {
        service.Validate();
        if (service.Provider == "aws")
            return ServiceCatalog.Load().Where(s => s.Provider == "aws" && s.Components is { Length: > 0 })
                .Select(s => new ProviderComponent(s.Components!.FirstOrDefault(c => c.StartsWith("region:")) ?? s.Components!.FirstOrDefault(c=>c.StartsWith("product:")) ?? "product:" + s.Name.Replace("AWS · ", "").Split(" (")[0],s.Name))
                .DistinctBy(c=>c.Id).OrderBy(c=>c.Name).ToArray();
        if (service.Provider == "google")
            return ServiceCatalog.Load().Where(s => s.ApiUrl == service.ApiUrl && s.Components is { Length: > 0 })
                .SelectMany(s => s.Components!.Select(id => new ProviderComponent(id, s.Name)))
                .DistinctBy(c => c.Id).OrderBy(c => c.Name).ToArray();
        if (service.Provider is not ("statuspage" or "instatus")) return [];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await http.GetAsync(OpenAiComponents.Applies(service)?OpenAiComponents.ComponentsUrl:service.ApiUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 16 * 1024 * 1024) throw new InvalidDataException("Status response is too large.");
        await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var data = new MemoryStream(); var buffer = new byte[16384]; int read;
        while ((read = await source.ReadAsync(buffer, timeout.Token)) > 0)
        {
            if (data.Length + read > 16 * 1024 * 1024) throw new InvalidDataException("Status response is too large.");
            data.Write(buffer, 0, read);
        }
        return ComponentCatalog.Parse(service.Provider, StatusPayload.Decode(data.ToArray()));
    }
    public async Task<IReadOnlyList<ServiceSnapshot>> CheckAsync(IEnumerable<ServiceDefinition> definitions, CancellationToken cancellationToken = default)
    {
        var entries = definitions.ToArray();
        if (entries.Any(s => s.Provider == "aggregate"))
        {
            var children = ServiceCatalog.Load().Where(s => s.Family == "Sendbird" && s.Provider != "aggregate").ToArray();
            var expanded = await CheckAsync(entries.Where(s => s.Provider != "aggregate").Concat(children).DistinctBy(s => s.Id), cancellationToken);
            var results = expanded.Where(s => entries.Any(e => e.Id == s.ServiceId)).ToList();
            var states = expanded.Where(s => children.Any(c => c.Id == s.ServiceId)).ToArray();
            foreach (var aggregate in entries.Where(s => s.Provider == "aggregate"))
            {
                var worst = states.Any(s => s.Status == ServiceStatus.Unknown) ? ServiceStatus.Unknown : states.Length == 0 ? ServiceStatus.Unknown : states.Max(s => s.Status);
                results.Add(new(aggregate.Id,worst,StatusParser.DefaultMessage(worst),DateTimeOffset.UtcNow));
            }
            return results;
        }
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            using var concurrency = new SemaphoreSlim(6, 6);
            var tasks = entries.GroupBy(s => s.ApiUrl).Select(async group =>
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
                        request.Headers.UserAgent.ParseAdd("StatusLookout/0.1 (+https://github.com/Bouneh/statuslookout)");
                        request.Headers.Accept.ParseAdd("*/*");
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
                        var body = StatusPayload.Decode(buffer.ToArray());
                        if(OpenAiComponents.Applies(group.First())&&group.Any(s=>s.Components is {Length:>0}))
                        {
                            using var componentResponse=await http.GetAsync(OpenAiComponents.ComponentsUrl,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
                            componentResponse.EnsureSuccessStatusCode();
                            body=OpenAiComponents.Merge(body,await ReadBodyAsync(componentResponse,timeout.Token));
                        }
                        json=body;
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
    private static async Task<string> ReadBodyAsync(HttpResponseMessage response,CancellationToken token)
    {
        if(response.Content.Headers.ContentLength>16*1024*1024)throw new InvalidDataException("Status response is too large.");
        await using var stream=await response.Content.ReadAsStreamAsync(token);using var output=new MemoryStream();var buffer=new byte[16384];int count;
        while((count=await stream.ReadAsync(buffer,token))>0){if(output.Length+count>16*1024*1024)throw new InvalidDataException("Status response is too large.");output.Write(buffer,0,count);}
        return StatusPayload.Decode(output.ToArray());
    }
}
