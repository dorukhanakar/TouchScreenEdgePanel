using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EdgePanel.Core;

public sealed class AppEntry
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Args { get; set; }
    /// <summary>Optional custom image (png/jpg/ico). Falls back to the shell icon of Path.</summary>
    public string? Icon { get; set; }
}

public sealed class AppSettings
{
    /// <summary>-1 = auto (the ultra-wide 2560x720 Xeneon Edge screen).</summary>
    public int MonitorIndex { get; set; } = -1;
    /// <summary>"tr", "en", or "" = follow the Windows display language.</summary>
    public string Language { get; set; } = "";
    public bool NoActivate { get; set; } = true;
    /// <summary>false = CPU rendering (much lower memory; plenty for a static dashboard).</summary>
    public bool GpuRendering { get; set; } = false;

    public string City { get; set; } = "İstanbul";
    public double Latitude { get; set; } = 41.0138;
    public double Longitude { get; set; } = 28.9497;

    public string DiscordClientId { get; set; } = "";
    /// <summary>DPAPI-protected (current Windows user only).</summary>
    public string DiscordSecret { get; set; } = "";
    /// <summary>DPAPI-protected OAuth token JSON.</summary>
    public string DiscordToken { get; set; } = "";

    public List<string> Leagues { get; set; } = new() { "tur.1", "uefa.champions", "uefa.europa", "uefa.nations" };
    /// <summary>Score feed window: this many days before today …</summary>
    public int ScoreDaysBack { get; set; } = 1;
    /// <summary>… and this many after (1 + 1 + 3 = a 5-day feed).</summary>
    public int ScoreDaysAhead { get; set; } = 3;

    /// <summary>0 = auto-fit (biggest tiles that fit); &gt; 0 pins the column count.</summary>
    public int AppColumns { get; set; } = 0;
    public List<AppEntry> Apps { get; set; } = new();

    // ── persistence ──────────────────────────────────────────────
    static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public static string Dir => System.IO.Path.Combine(AppData, AppInfo.Name);
    static string LegacyDir => System.IO.Path.Combine(AppData, AppInfo.LegacyName);
    static string FilePath => System.IO.Path.Combine(Dir, "settings.json");

    static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static AppSettings Current { get; private set; } = new();

    /// <summary>Raised (UI thread) after the settings window saves.</summary>
    public static event Action? Changed;
    public static void NotifyChanged() => Changed?.Invoke();

    static string BackupPath => FilePath + ".bak";

    /// <summary>
    /// Reads settings.json; if it is missing or unreadable, falls back to the last good backup instead of
    /// silently starting empty (an empty start would then be saved over the user's real settings).
    /// An unreadable file is kept aside as settings.broken-*.json for inspection.
    /// </summary>
    public static void Load()
    {
        MigrateLegacyFolder();
        LoadFile();
        // custom pictures stored under the old folder name now live in the new one
        bool fixedPaths = false;
        foreach (var a in Current.Apps)
            if (a.Icon != null && a.Icon.StartsWith(LegacyDir + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                a.Icon = Dir + a.Icon[LegacyDir.Length..];
                fixedPaths = true;
            }
        if (fixedPaths) Save();
    }

    /// <summary>First start after the rename: move %APPDATA%\EdgePanel (settings, pictures, log) to the new name.</summary>
    static void MigrateLegacyFolder()
    {
        if (Directory.Exists(Dir) || !Directory.Exists(LegacyDir)) return;
        try { Directory.Move(LegacyDir, Dir); }
        catch
        {
            try
            {
                foreach (var f in Directory.GetFiles(LegacyDir, "*", SearchOption.AllDirectories))
                {
                    var dest = Dir + f[LegacyDir.Length..];
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
                    File.Copy(f, dest, true);
                }
            }
            catch { }
        }
    }

    static void LoadFile()
    {
        if (TryRead(FilePath, out var s)) { Current = s; return; }
        if (File.Exists(FilePath))
        {
            try { File.Copy(FilePath, Path.Combine(Dir, $"settings.broken-{DateTime.Now:yyyyMMdd-HHmmss}.json"), true); } catch { }
            App.Log(new InvalidDataException("settings.json okunamadı; yedekten yükleniyor."));
        }
        Current = TryRead(BackupPath, out var b) ? b : new();
    }

    static bool TryRead(string path, out AppSettings settings)
    {
        settings = new();
        try
        {
            if (!File.Exists(path)) return false;
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Opts);
            if (s == null) return false;
            settings = s;
            return true;
        }
        catch { return false; }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            // keep the previous good file as a backup before replacing it
            if (TryRead(FilePath, out _)) File.Copy(FilePath, BackupPath, true);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Opts));
            File.Move(tmp, FilePath, true);
        }
        catch (Exception ex) { App.Log(ex); }
    }

    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));
    }

    public static string Unprotect(string b64)
    {
        if (string.IsNullOrEmpty(b64)) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(b64), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }
}
