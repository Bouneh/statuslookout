using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using StatusLookout.Core;

static class BridgeChecks
{
    public static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        var name = "StatusLookout.Tests." + Guid.NewGuid().ToString("N");
        var commands = new List<string>();
        var snapshot = new BridgeSnapshot(1, "0.5.0", "fr", false, false, false, ServiceStatus.Operational,
            [new("test", "Service français", ServiceStatus.Operational, "Tout fonctionne", "https://example.com", false, null, null)]);
        await using var server = new LocalBridgeServer(name, (request, token) =>
        { lock (commands) commands.Add(request.Command); return Task.FromResult(snapshot); });
        async Task<BridgeResponse> Send(string request)
        {
            using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await pipe.ConnectAsync(timeout.Token);
            await pipe.WriteAsync(Encoding.UTF8.GetBytes(request + "\n"), timeout.Token);
            using var reader = new StreamReader(pipe);
            var line = await reader.ReadLineAsync(timeout.Token) ?? throw new Exception("Missing bridge response.");
            return JsonSerializer.Deserialize<BridgeResponse>(line, JsonStore.Options)!;
        }
        await check("Bridge snapshot uses one-line UTF8 JSON", async () =>
        {
            var response = await Send("{\"command\":\"snapshot\"}");
            if (!response.Ok || response.Snapshot?.Services[0].Name != "Service français") throw new Exception("Snapshot mismatch.");
        });
        await check("Bridge rejects unknown commands without invoking app", async () =>
        {
            var count = commands.Count; var response = await Send("{\"command\":\"deleteFiles\"}");
            if (response.Ok || commands.Count != count) throw new Exception("Untrusted command was accepted.");
        });
        await check("Bridge validates snooze durations and service IDs", async () =>
        {
            if ((await Send("{\"command\":\"snooze\",\"serviceId\":\"test\",\"minutes\":5}")).Ok
                || (await Send("{\"command\":\"openService\"}")).Ok) throw new Exception("Invalid service command was accepted.");
        });
        await check("Bridge survives malformed JSON", async () =>
        {
            if ((await Send("not-json")).Ok || !(await Send("{\"command\":\"snapshot\"}")).Ok) throw new Exception("Bridge did not recover.");
        });
        await check("Bridge handles simultaneous clients", async () =>
        {
            var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Send("{\"command\":\"snapshot\"}")));
            if (responses.Any(r => !r.Ok)) throw new Exception("Concurrent bridge request failed.");
        });
        await check("Bridge pipe names match case-insensitive Windows account", () =>
        {
            if (StreamDeckProtocol.PipeName("PC", "Bouneh") != StreamDeckProtocol.PipeName("pc", "bouneh")) throw new Exception("Account mismatch.");
            return Task.CompletedTask;
        });
    }
}
