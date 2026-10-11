using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
namespace StatusLookout.Core;

/// <summary>One bounded JSON request per connection, accessible only to the current Windows user.</summary>
public sealed class LocalBridgeServer : IDisposable, IAsyncDisposable
{
    private readonly string _name;
    private readonly Func<BridgeRequest, CancellationToken, Task<BridgeSnapshot>> _handle;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<int, Task> _clients = [];
    private readonly Task _loop;
    private readonly SemaphoreSlim _slots = new(7, 7);
    private int _nextClient, _disposed;
    public LocalBridgeServer(string name, Func<BridgeRequest, CancellationToken, Task<BridgeSnapshot>> handle)
    { _name = name; _handle = handle; _loop = ListenAsync(_stop.Token); }
    private async Task ListenAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await _slots.WaitAsync(token);
                var pipe = new NamedPipeServerStream(_name, PipeDirection.InOut, 8, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try { await pipe.WaitForConnectionAsync(token); }
                catch { pipe.Dispose(); throw; }
                var id = Interlocked.Increment(ref _nextClient); var task = ServeAndReleaseAsync(pipe, token); _clients[id] = task;
                _ = task.ContinueWith(_ => _clients.TryRemove(id, out var ignored), TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (IOException) when (token.IsCancellationRequested) { }
    }
    private async Task ServeAndReleaseAsync(NamedPipeServerStream pipe, CancellationToken token)
    { try { await ServeAsync(pipe, token); } finally { _slots.Release(); } }
    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        await using (pipe)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                BridgeResponse reply;
                try
                {
                    using var data = new MemoryStream(); var buffer = new byte[1024];
                    while (true)
                    {
                        var count = await pipe.ReadAsync(buffer, timeout.Token); if (count == 0) return;
                        var newline = Array.IndexOf(buffer, (byte)'\n', 0, count); var length = newline < 0 ? count : newline;
                        if (data.Length + length > 16384) throw new InvalidDataException("Request is too large.");
                        data.Write(buffer, 0, length); if (newline >= 0) break;
                    }
                    var request = JsonSerializer.Deserialize<BridgeRequest>(data.ToArray(), JsonStore.Options) ?? throw new InvalidDataException("Empty command.");
                    StreamDeckProtocol.Validate(request);
                    reply = new(true, await _handle(request, timeout.Token).WaitAsync(timeout.Token));
                }
                catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException)
                { reply = new(false, Error: ex.Message); }
                // The client uses newline framing, so the response must occupy one line.
                var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(reply, new JsonSerializerOptions(JsonStore.Options) { WriteIndented = false }) + "\n");
                await pipe.WriteAsync(payload, timeout.Token); await pipe.FlushAsync(timeout.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        }
    }
    public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) _stop.Cancel(); }
    public async ValueTask DisposeAsync() { Dispose(); await _loop; await Task.WhenAll(_clients.Values); _stop.Dispose(); }
}
