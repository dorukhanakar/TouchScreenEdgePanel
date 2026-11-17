using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace EdgePanel.Core;

public sealed class RpcException(string message) : Exception(message);

/// <summary>
/// Minimal client for the Discord desktop app's local RPC pipe (\\.\pipe\discord-ipc-N).
/// Frame = int32 opcode + int32 length + UTF-8 JSON. Events are raised on the reader thread.
/// </summary>
public sealed class DiscordRpc : IDisposable
{
    const int OP_HANDSHAKE = 0, OP_FRAME = 1, OP_CLOSE = 2, OP_PING = 3, OP_PONG = 4;

    NamedPipeClientStream? _pipe;
    readonly CancellationTokenSource _cts = new();
    readonly SemaphoreSlim _write = new(1, 1);
    readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int _closed;

    public event Action<string, JsonElement>? Event;
    public event Action? Closed;
    public string LastError { get; private set; } = "";

    /// <summary>Returns false if Discord isn't running or rejected the handshake.</summary>
    public async Task<bool> ConnectAsync(string clientId)
    {
        string[] pipes;
        try
        {
            pipes = Directory.GetFiles(@"\\.\pipe\")
                .Select(Path.GetFileName)
                .Where(n => n != null && n.StartsWith("discord-ipc-", StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n)
                .ToArray()!;
        }
        catch { pipes = Enumerable.Range(0, 10).Select(i => "discord-ipc-" + i).ToArray(); }

        foreach (var name in pipes)
        {
            var p = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            try { await p.ConnectAsync(1000); _pipe = p; break; }
            catch { p.Dispose(); }
        }
        if (_pipe == null) { LastError = "discord_not_running"; return false; }

        _ = Task.Run(ReadLoop);
        await WriteAsync(OP_HANDSHAKE, JsonSerializer.SerializeToUtf8Bytes(new { v = 1, client_id = clientId }));
        var done = await Task.WhenAny(_ready.Task, Task.Delay(6000));
        if (done != _ready.Task || !_ready.Task.Result)
        {
            if (LastError.Length == 0) LastError = "handshake_timeout";
            Dispose();
            return false;
        }
        return true;
    }

    public async Task<JsonElement> CommandAsync(string cmd, object? args = null, string? evt = null, int timeoutMs = 8000)
    {
        if (_pipe == null || _closed != 0) throw new RpcException(L.RpcNoConnection);
        var nonce = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[nonce] = tcs;

        var frame = new Dictionary<string, object?> { ["cmd"] = cmd, ["args"] = args ?? new { }, ["nonce"] = nonce };
        if (evt != null) frame["evt"] = evt;
        await WriteAsync(OP_FRAME, JsonSerializer.SerializeToUtf8Bytes(frame));

        using var timeout = new CancellationTokenSource(timeoutMs);
        using (timeout.Token.Register(() =>
               {
                   if (_pending.TryRemove(nonce, out var t)) t.TrySetException(new TimeoutException(L.RpcTimeout(cmd)));
               }))
        {
            return await tcs.Task;
        }
    }

    async Task WriteAsync(int op, byte[] payload)
    {
        var pipe = _pipe ?? throw new RpcException(L.RpcNoConnection);
        var buf = new byte[8 + payload.Length];
        BitConverter.TryWriteBytes(buf.AsSpan(0, 4), op);
        BitConverter.TryWriteBytes(buf.AsSpan(4, 4), payload.Length);
        payload.CopyTo(buf, 8);
        await _write.WaitAsync();
        try { await pipe.WriteAsync(buf); await pipe.FlushAsync(); }
        finally { _write.Release(); }
    }

    async Task ReadLoop()
    {
        var header = new byte[8];
        try
        {
            while (!_cts.IsCancellationRequested && _pipe != null)
            {
                await _pipe.ReadExactlyAsync(header, _cts.Token);
                int op = BitConverter.ToInt32(header, 0);
                int len = BitConverter.ToInt32(header, 4);
                if (len < 0 || len > 8 * 1024 * 1024) break;
                var body = new byte[len];
                await _pipe.ReadExactlyAsync(body, _cts.Token);

                if (op == OP_PING) { await WriteAsync(OP_PONG, body); continue; }
                if (op == OP_CLOSE)
                {
                    try
                    {
                        using var cd = JsonDocument.Parse(body);
                        if (cd.RootElement.TryGetProperty("message", out var msg)) LastError = msg.GetString() ?? "closed";
                    }
                    catch { LastError = "closed"; }
                    break;
                }
                if (op != OP_FRAME) continue;
                Handle(body);
            }
        }
        catch { /* pipe broken / cancelled */ }
        finally { Dispose(); }
    }

    void Handle(byte[] body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        string? cmd = root.TryGetProperty("cmd", out var c) ? c.GetString() : null;
        string? evt = root.TryGetProperty("evt", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        var data = root.TryGetProperty("data", out var d) ? d.Clone() : default;

        if (cmd == "DISPATCH")
        {
            if (evt == "READY") _ready.TrySetResult(true);
            else if (evt != null) Event?.Invoke(evt, data);
            return;
        }
        if (root.TryGetProperty("nonce", out var n) && n.ValueKind == JsonValueKind.String &&
            _pending.TryRemove(n.GetString()!, out var tcs))
        {
            if (evt == "ERROR")
            {
                var message = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("message", out var m) ? m.GetString() : null;
                tcs.TrySetException(new RpcException(message ?? L.RpcError));
            }
            else tcs.TrySetResult(data);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        try { _cts.Cancel(); } catch { }
        try { _pipe?.Dispose(); } catch { }
        _ready.TrySetResult(false);
        foreach (var kv in _pending)
            if (_pending.TryRemove(kv.Key, out var t)) t.TrySetException(new RpcException(L.RpcClosed));
        Closed?.Invoke();
    }
}
