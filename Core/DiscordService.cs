using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace EdgePanel.Core;

public enum DiscordState { Unconfigured, NeedAuth, Offline, Connecting, Authorizing, Ready, Error }

public sealed class DiscordMember
{
    public string Id = "", Name = "", Avatar = "";
    public bool Mute, Deaf, Speaking;
}

public sealed record DiscordItem(string Id, string Name);

/// <summary>
/// Discord voice control through the desktop client's local RPC (the same channel the Stream Deck plugin uses).
/// Needs the user's own Discord application (client id + secret, redirect http://localhost).
/// All state lives on the UI thread; RPC events are marshalled onto it.
/// </summary>
public sealed class DiscordService
{
    public static DiscordService Instance { get; } = new();

    const string TokenUrl = "https://discord.com/api/oauth2/token";
    const string Redirect = "http://localhost";
    static readonly string[] Scopes = { "rpc", "rpc.voice.read", "rpc.voice.write", "identify" };
    static readonly string[] ChannelEvents =
        { "VOICE_STATE_CREATE", "VOICE_STATE_UPDATE", "VOICE_STATE_DELETE", "SPEAKING_START", "SPEAKING_STOP" };

    readonly Dispatcher _ui = Application.Current.Dispatcher;
    readonly DispatcherTimer _retry = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(15) };
    DiscordRpc? _rpc;
    bool _busy;
    bool _retryInteractive;          // a "Bağlan" tap that hit a closed Discord finishes once it opens
    DateTime _fastRetryUntil;        // after "Discord'u Aç": poll every 3 s instead of 15 s

    /// <summary>True for ~90 s after we started Discord ourselves.</summary>
    public bool Launching => DateTime.Now < _fastRetryUntil;
    string? _subscribedChannel;

    public DiscordState State { get; private set; } = DiscordState.Unconfigured;
    public string Message { get; private set; } = "";
    public string Username { get; private set; } = "";

    public bool Mute, Deaf, NoiseSuppression, EchoCancellation, AutoGain, Qos;
    public int InputVolume = 100, OutputVolume = 100;
    public string Mode = "VOICE_ACTIVITY";
    public string? ChannelId, ChannelName;
    public readonly List<DiscordMember> Members = new();

    public event Action? Changed;
    public event Action<string, bool>? SpeakingChanged;

    DiscordService()
    {
        _retry.Tick += async (_, _) => { _retry.Stop(); await StartAsync(_retryInteractive); };
    }

    void SetState(DiscordState s, string msg = "")
    {
        State = s; Message = msg;
        Changed?.Invoke();
    }

    sealed record Token(string Access, string Refresh, DateTime ExpiresUtc);

    static Token? LoadToken()
    {
        var json = AppSettings.Unprotect(AppSettings.Current.DiscordToken);
        if (json.Length == 0) return null;
        try { return JsonSerializer.Deserialize<Token>(json); } catch { return null; }
    }

    static void SaveToken(Token? t)
    {
        AppSettings.Current.DiscordToken = t == null ? "" : AppSettings.Protect(JsonSerializer.Serialize(t));
        AppSettings.Save();
    }

    // ── lifecycle ────────────────────────────────────────────────

    /// <param name="interactive">true = allowed to pop the "Authorize" dialog inside Discord.</param>
    public async Task StartAsync(bool interactive)
    {
        if (_busy) return;
        _busy = true;
        _retry.Stop();
        try
        {
            Disconnect();
            var s = AppSettings.Current;
            var clientId = s.DiscordClientId.Trim();
            var secret = AppSettings.Unprotect(s.DiscordSecret);
            if (clientId.Length == 0 || secret.Length == 0) { SetState(DiscordState.Unconfigured); return; }

            var token = LoadToken();
            if (token == null && !interactive) { SetState(DiscordState.NeedAuth); return; }

            if (State != DiscordState.Offline) SetState(DiscordState.Connecting);   // silent background retries
            var rpc = new DiscordRpc();
            if (!await rpc.ConnectAsync(clientId))
            {
                if (rpc.LastError == "discord_not_running")
                {
                    _retryInteractive = interactive;
                    SetState(DiscordState.Offline, L.DsNotRunning);
                    ScheduleRetry();
                }
                else SetState(DiscordState.Error, L.DsRejected(rpc.LastError));
                return;
            }
            _rpc = rpc;
            rpc.Event += (evt, data) => _ui.BeginInvoke(() => OnEvent(rpc, evt, data));
            rpc.Closed += () => _ui.BeginInvoke(() => OnClosed(rpc));

            JsonElement? auth = null;
            if (token != null)
            {
                if (token.ExpiresUtc < DateTime.UtcNow.AddMinutes(10)) token = await RefreshAsync(clientId, secret, token);
                if (token != null) auth = await TryAuthenticate(token.Access);
                if (auth == null && token != null)
                {
                    token = await RefreshAsync(clientId, secret, token);
                    if (token != null) auth = await TryAuthenticate(token.Access);
                }
            }

            if (auth == null)
            {
                if (!interactive) { Disconnect(); SetState(DiscordState.NeedAuth); return; }
                SetState(DiscordState.Authorizing, L.DsAuthorize);
                var r = await rpc.CommandAsync("AUTHORIZE", new { client_id = clientId, scopes = Scopes }, timeoutMs: 120_000);
                var code = r.GetProperty("code").GetString() ?? "";
                token = await ExchangeAsync(clientId, secret, code);
                auth = await TryAuthenticate(token.Access) ?? throw new RpcException(L.DsAuthFailed);
            }
            SaveToken(token);

            var user = auth.Value.TryGetProperty("user", out var u) ? u : default;
            Username = Name(user);

            ApplyVoice(await rpc.CommandAsync("GET_VOICE_SETTINGS"));
            await rpc.CommandAsync("SUBSCRIBE", new { }, "VOICE_SETTINGS_UPDATE");
            await rpc.CommandAsync("SUBSCRIBE", new { }, "VOICE_CHANNEL_SELECT");
            await LoadChannelAsync();
            _retryInteractive = false;
            _fastRetryUntil = default;
            SetState(DiscordState.Ready);
        }
        catch (Exception ex)
        {
            Disconnect();
            SetState(DiscordState.Error, ex.Message);
        }
        finally { _busy = false; }
    }

    public void Disconnect()
    {
        var r = _rpc;
        _rpc = null;
        _subscribedChannel = null;
        ChannelId = ChannelName = null;
        Members.Clear();
        r?.Dispose();
    }

    public void Logout()
    {
        _retry.Stop();
        Disconnect();
        SaveToken(null);
        SetState(string.IsNullOrEmpty(AppSettings.Current.DiscordClientId) ? DiscordState.Unconfigured : DiscordState.NeedAuth);
    }

    void ScheduleRetry()
    {
        _retry.Interval = TimeSpan.FromSeconds(Launching ? 3 : 15);
        _retry.Start();
    }

    /// <summary>Discord (any flavour) has its local RPC pipe open.</summary>
    public static bool IsDiscordRunning()
    {
        try { return Directory.GetFiles(@"\\.\pipe\").Any(p => Path.GetFileName(p).StartsWith("discord-ipc-", StringComparison.OrdinalIgnoreCase)); }
        catch { return true; }
    }

    /// <summary>
    /// Starts the Discord desktop app and connects as soon as it is up. Uses the same launcher as the
    /// Start-menu shortcut (%LOCALAPPDATA%\Discord\Discord.exe --processStart Discord.exe), then PTB / Canary,
    /// then the discord:// protocol. Never Update.exe: Windows treats an exe named "Update" as an installer
    /// and demands elevation (error 740).
    /// </summary>
    public void LaunchDiscord()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        bool started = false;
        foreach (var (dir, exe) in new[] { ("Discord", "Discord.exe"), ("DiscordPTB", "DiscordPTB.exe"), ("DiscordCanary", "DiscordCanary.exe") })
        {
            var launcher = Path.Combine(local, dir, exe);
            if (!File.Exists(launcher)) continue;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(launcher, "--processStart " + exe)
                    { UseShellExecute = false, WorkingDirectory = Path.Combine(local, dir) })?.Dispose();
                started = true;
                break;
            }
            catch (Exception ex) { App.Log(ex); }
        }
        if (!started)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("discord://") { UseShellExecute = true })?.Dispose(); started = true; }
            catch (Exception ex) { App.Log(ex); }
        }
        if (!started) { SetState(DiscordState.Offline, L.DsNotFound); return; }

        _fastRetryUntil = DateTime.Now.AddSeconds(90);
        SetState(DiscordState.Offline, L.DsLaunching);
        _retry.Stop();
        ScheduleRetry();
    }

    void OnClosed(DiscordRpc rpc)
    {
        if (rpc != _rpc) return;   // an old socket we already replaced
        Disconnect();
        SetState(DiscordState.Offline, L.DsClosedReconnect);
        ScheduleRetry();
    }

    // ── OAuth ────────────────────────────────────────────────────

    async Task<JsonElement?> TryAuthenticate(string accessToken)
    {
        try { return await _rpc!.CommandAsync("AUTHENTICATE", new { access_token = accessToken }); }
        catch { return null; }
    }

    static async Task<Token> ExchangeAsync(string clientId, string secret, string code) =>
        await PostTokenAsync(new()
        {
            ["client_id"] = clientId, ["client_secret"] = secret, ["grant_type"] = "authorization_code",
            ["code"] = code, ["redirect_uri"] = Redirect,
        });

    static async Task<Token?> RefreshAsync(string clientId, string secret, Token t)
    {
        try
        {
            return await PostTokenAsync(new()
            {
                ["client_id"] = clientId, ["client_secret"] = secret, ["grant_type"] = "refresh_token",
                ["refresh_token"] = t.Refresh,
            });
        }
        catch { return null; }
    }

    static async Task<Token> PostTokenAsync(Dictionary<string, string> form)
    {
        using var resp = await Http.Client.PostAsync(TokenUrl, new FormUrlEncodedContent(form));
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var r = doc.RootElement;
        if (!resp.IsSuccessStatusCode)
        {
            var err = r.TryGetProperty("error_description", out var d) ? d.GetString()
                    : r.TryGetProperty("error", out var e) ? e.GetString() : resp.StatusCode.ToString();
            throw new RpcException(L.DsTokenFailed(err ?? ""));
        }
        return new Token(
            r.GetProperty("access_token").GetString()!,
            r.GetProperty("refresh_token").GetString()!,
            DateTime.UtcNow.AddSeconds(r.GetProperty("expires_in").GetInt32()));
    }

    // ── state from Discord ───────────────────────────────────────

    static bool Bool(JsonElement o, string k, bool fallback) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean() : fallback;

    static string Str(JsonElement o, string k) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    static string Name(JsonElement user)
    {
        var n = Str(user, "global_name");
        return n.Length > 0 ? n : Str(user, "username");
    }

    void ApplyVoice(JsonElement d)
    {
        if (d.ValueKind != JsonValueKind.Object) return;
        Mute = Bool(d, "mute", Mute);
        Deaf = Bool(d, "deaf", Deaf);
        NoiseSuppression = Bool(d, "noise_suppression", NoiseSuppression);
        EchoCancellation = Bool(d, "echo_cancellation", EchoCancellation);
        AutoGain = Bool(d, "automatic_gain_control", AutoGain);
        Qos = Bool(d, "qos", Qos);
        if (d.TryGetProperty("input", out var i) && i.TryGetProperty("volume", out var iv) && iv.TryGetDouble(out var ivd))
            InputVolume = (int)Math.Round(ivd);
        if (d.TryGetProperty("output", out var o) && o.TryGetProperty("volume", out var ov) && ov.TryGetDouble(out var ovd))
            OutputVolume = (int)Math.Round(ovd);
        if (d.TryGetProperty("mode", out var m)) { var t = Str(m, "type"); if (t.Length > 0) Mode = t; }
    }

    static DiscordMember? ParseMember(JsonElement vs)
    {
        if (!vs.TryGetProperty("user", out var u)) return null;
        var id = Str(u, "id");
        if (id.Length == 0) return null;
        var st = vs.TryGetProperty("voice_state", out var s) ? s : default;
        var nick = Str(vs, "nick");
        return new DiscordMember
        {
            Id = id,
            Name = nick.Length > 0 ? nick : Name(u),
            Avatar = AvatarUrl(u, id),
            Mute = Bool(st, "mute", false) || Bool(st, "self_mute", false),
            Deaf = Bool(st, "deaf", false) || Bool(st, "self_deaf", false),
        };
    }

    static string AvatarUrl(JsonElement u, string id)
    {
        var hash = Str(u, "avatar");
        if (hash.Length > 0) return $"https://cdn.discordapp.com/avatars/{id}/{hash}.png?size=64";
        int idx = ulong.TryParse(id, out var n) ? (int)((n >> 22) % 6) : 0;
        return $"https://cdn.discordapp.com/embed/avatars/{idx}.png";
    }

    async Task LoadChannelAsync()
    {
        var rpc = _rpc;
        if (rpc == null) return;
        if (_subscribedChannel != null)
        {
            foreach (var evt in ChannelEvents)
                try { await rpc.CommandAsync("UNSUBSCRIBE", new { channel_id = _subscribedChannel }, evt); } catch { }
            _subscribedChannel = null;
        }

        var ch = await rpc.CommandAsync("GET_SELECTED_VOICE_CHANNEL");
        Members.Clear();
        if (ch.ValueKind != JsonValueKind.Object)
        {
            ChannelId = ChannelName = null;
            Changed?.Invoke();
            return;
        }
        ChannelId = Str(ch, "id");
        ChannelName = Str(ch, "name");
        if (ch.TryGetProperty("voice_states", out var states))
            foreach (var vs in states.EnumerateArray())
                if (ParseMember(vs) is { } m) Members.Add(m);

        foreach (var evt in ChannelEvents)
            try { await rpc.CommandAsync("SUBSCRIBE", new { channel_id = ChannelId }, evt); } catch { }
        _subscribedChannel = ChannelId;
        Changed?.Invoke();
    }

    async void OnEvent(DiscordRpc rpc, string evt, JsonElement data)
    {
        if (rpc != _rpc) return;
        try
        {
            switch (evt)
            {
                case "VOICE_SETTINGS_UPDATE":
                    ApplyVoice(data); Changed?.Invoke(); break;
                case "VOICE_CHANNEL_SELECT":
                    await LoadChannelAsync(); break;
                case "VOICE_STATE_CREATE":
                case "VOICE_STATE_UPDATE":
                    if (ParseMember(data) is { } m)
                    {
                        var i = Members.FindIndex(x => x.Id == m.Id);
                        if (i >= 0) { m.Speaking = Members[i].Speaking; Members[i] = m; } else Members.Add(m);
                        Changed?.Invoke();
                    }
                    break;
                case "VOICE_STATE_DELETE":
                    if (data.TryGetProperty("user", out var u))
                    {
                        Members.RemoveAll(x => x.Id == Str(u, "id"));
                        Changed?.Invoke();
                    }
                    break;
                case "SPEAKING_START":
                case "SPEAKING_STOP":
                    var id = Str(data, "user_id");
                    var on = evt == "SPEAKING_START";
                    var mem = Members.Find(x => x.Id == id);
                    if (mem != null && mem.Speaking != on) { mem.Speaking = on; SpeakingChanged?.Invoke(id, on); }
                    break;
            }
        }
        catch (Exception ex) { App.Log(ex); }
    }

    // ── actions ──────────────────────────────────────────────────

    async Task SetVoice(object args)
    {
        if (_rpc == null || State != DiscordState.Ready) return;
        try { ApplyVoice(await _rpc.CommandAsync("SET_VOICE_SETTINGS", args)); Changed?.Invoke(); }
        catch (Exception ex) { App.Log(ex); }
    }

    public Task ToggleMute() => SetVoice(new { mute = !Mute });
    public Task ToggleDeaf() => SetVoice(new { deaf = !Deaf });
    public Task ToggleMode() => SetVoice(new { mode = new { type = Mode == "PUSH_TO_TALK" ? "VOICE_ACTIVITY" : "PUSH_TO_TALK" } });
    public Task SetInputVolume(int v) => SetVoice(new { input = new { volume = Math.Clamp(v, 0, 100) } });
    public Task SetOutputVolume(int v) => SetVoice(new { output = new { volume = Math.Clamp(v, 0, 200) } });
    public Task SetFlag(string key, bool value) => SetVoice(new Dictionary<string, object> { [key] = value });

    public async Task LeaveAsync()
    {
        if (_rpc == null) return;
        try { await _rpc.CommandAsync("SELECT_VOICE_CHANNEL", new { channel_id = (string?)null }); }
        catch (Exception ex) { App.Log(ex); }
    }

    public async Task JoinAsync(string channelId)
    {
        if (_rpc == null) return;
        try { await _rpc.CommandAsync("SELECT_VOICE_CHANNEL", new { channel_id = channelId, force = true }, timeoutMs: 15000); }
        catch (Exception ex) { App.Log(ex); }
    }

    public async Task<List<DiscordItem>> GetGuildsAsync()
    {
        var list = new List<DiscordItem>();
        if (_rpc == null) return list;
        var r = await _rpc.CommandAsync("GET_GUILDS");
        if (r.TryGetProperty("guilds", out var g))
            foreach (var x in g.EnumerateArray()) list.Add(new DiscordItem(Str(x, "id"), Str(x, "name")));
        return list;
    }

    public async Task<List<DiscordItem>> GetVoiceChannelsAsync(string guildId)
    {
        var list = new List<DiscordItem>();
        if (_rpc == null) return list;
        var r = await _rpc.CommandAsync("GET_CHANNELS", new { guild_id = guildId });
        if (r.TryGetProperty("channels", out var c))
            foreach (var x in c.EnumerateArray())
                if (x.TryGetProperty("type", out var t) && t.TryGetInt32(out var type) && type is 2 or 13)
                    list.Add(new DiscordItem(Str(x, "id"), Str(x, "name")));
        return list;
    }
}
