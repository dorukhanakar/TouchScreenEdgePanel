using System.Globalization;

namespace EdgePanel.Core;

/// <summary>
/// UI strings in Turkish and English. Chosen once at start-up (Settings → Dil / Language;
/// "auto" follows the Windows display language) — changing it restarts the panel.
/// XAML binds with {x:Static c:L.Name}; code uses L.Name directly.
/// </summary>
public static class L
{
    public static string Lang { get; private set; } = "tr";
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("tr-TR");
    public static bool En => Lang == "en";

    /// <param name="setting">"tr", "en", or anything else = follow Windows.</param>
    public static void Init(string? setting)
    {
        Lang = setting is "tr" or "en" ? setting
             : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en";
        Culture = CultureInfo.GetCultureInfo(En ? "en-US" : "tr-TR");
    }

    static string T(string tr, string en) => En ? en : tr;
    public static string Upper(string s) => s.ToUpper(Culture);

    // ── general ──────────────────────────────────────────────────
    public static string Settings => T("Ayarlar", "Settings");
    public static string Close => T("Kapat", "Close");

    // ── header (clock / weather) ─────────────────────────────────
    public static string HeaderDateFormat => T("d MMMM yyyy, dddd", "dddd, MMMM d, yyyy");
    public static string WeatherFailed => T("Hava durumu alınamadı", "Couldn't load the weather");

    public static string Weather(int code) => code switch
    {
        0 => T("Açık", "Clear"),
        1 => T("Az bulutlu", "Mostly clear"),
        2 => T("Parçalı bulutlu", "Partly cloudy"),
        3 => T("Kapalı", "Overcast"),
        45 or 48 => T("Sisli", "Fog"),
        >= 51 and <= 57 => T("Çisenti", "Drizzle"),
        >= 61 and <= 67 => T("Yağmurlu", "Rain"),
        >= 71 and <= 77 => T("Karlı", "Snow"),
        >= 80 and <= 82 => T("Sağanak", "Showers"),
        85 or 86 => T("Kar sağanağı", "Snow showers"),
        >= 95 => T("Gök gürültülü fırtına", "Thunderstorm"),
        _ => "",
    };

    // ── scores ───────────────────────────────────────────────────
    public static string LiveScores => T("Canlı Skorlar", "Live Scores");
    public static string Loading => T("Yükleniyor…", "Loading…");
    public static string All => T("Tümü", "All");
    public static string LiveCount(int n) => T($"{n} CANLI", $"{n} LIVE");
    public static string Offline => T("BAĞLANTI YOK", "OFFLINE");
    public static string ScoresFailed => T("Skorlar alınamadı", "Couldn't load scores");
    public static string NoMatches => T("Bu ligde yakın tarihte maç yok", "No matches coming up in this league");
    public static string Today => T("Bugün", "Today");
    public static string Tomorrow => T("Yarın", "Tomorrow");
    public static string Yesterday => T("Dün", "Yesterday");
    public static string DayFormat => T("d MMMM dddd", "dddd, MMMM d");
    public static string OpenMatch => T("Maç özetini aç", "Open match summary");
    public static string HalfTime => T("İY", "HT");
    public static string FullTime => T("MS", "FT");
    public static string AfterExtraTime => T("UZ", "AET");
    public static string Penalties => "PEN";
    public static string Postponed => T("ERT", "PPD");

    public static string League(string code) => code switch
    {
        "tur.1" => T("Süper Lig", "Süper Lig"),
        "esp.1" => "La Liga",
        "ita.1" => "Serie A",
        "ger.1" => "Bundesliga",
        "uefa.champions" => T("Şampiyonlar Ligi", "Champions League"),
        "uefa.europa" => T("Avrupa Ligi", "Europa League"),
        "uefa.europa.conf" => T("Konferans Ligi", "Conference League"),
        "uefa.nations" => T("Uluslar Ligi", "Nations League"),
        _ => code,
    };

    // ── apps ─────────────────────────────────────────────────────
    public static string Apps => T("UYGULAMALAR", "APPS");
    public static string Edit => T("Düzenle", "Edit");
    public static string EditHint => T("Basılı tutup sürükle: sırala · ✕ kaldır · simgeye dokun: resmi değiştir",
                                       "Press and drag: reorder · ✕ remove · tap an icon: change picture");
    public static string AppsEmpty => T("Uygulama ya da klasör eklemek için buraya sürükle-bırak\nya da kalem simgesine dokunup + ile seç.",
                                        "Drag and drop apps or folders here\nor tap the pencil and use +.");
    public static string AddSystemItem => T("Sistem öğesi ekle", "Add a system item");
    public static string TileApp => T("Uygulama", "App");
    public static string TileFolder => T("Klasör", "Folder");
    public static string TileSystem => T("Sistem", "System");
    public static string AddThing(string what) => T($"{what} ekle", $"Add {what.ToLower(Culture)}");
    public static string AlreadyAdded => T("Zaten ekli", "Already added");
    public static string AddFolderTitle => T("Klasör ekle", "Add folder");
    public static string AddAppTitle => T("Uygulama ekle", "Add app");
    public static string AppFileFilter => T("Uygulamalar ve kısayollar|*.exe;*.lnk;*.url;*.bat;*.cmd|Tüm dosyalar|*.*",
                                            "Apps and shortcuts|*.exe;*.lnk;*.url;*.bat;*.cmd|All files|*.*");
    public static string ChooseIcon(string app) => T($"{app} için simge seç", $"Choose a picture for {app}");
    public static string ImageFilter => T("Resimler|*.png;*.jpg;*.jpeg;*.ico;*.bmp;*.webp|Tüm dosyalar|*.*",
                                          "Images|*.png;*.jpg;*.jpeg;*.ico;*.bmp;*.webp|All files|*.*");
    public static string RecycleBin => T("Geri Dönüşüm Kutusu", "Recycle Bin");
    public static string ThisPC => T("Bu Bilgisayar", "This PC");
    public static string Desktop => T("Masaüstü", "Desktop");
    public static string Documents => T("Belgeler", "Documents");
    public static string Downloads => T("İndirilenler", "Downloads");
    public static string Pictures => T("Resimler", "Pictures");
    public static string ControlPanel => T("Denetim Masası", "Control Panel");
    public static string Network => T("Ağ", "Network");

    // ── app picker window ────────────────────────────────────────
    public static string PickerTitle => T("Uygulama ekle", "Add app");
    public static string PickerHint => T("Başlat menüsündeki uygulamalar — dokununca panele eklenir.",
                                         "Apps from the Start menu — tap one to add it to the panel.");
    public static string SearchHint => T("Ara… (örn. Claude)", "Search… (e.g. Claude)");
    public static string Added => T("✓ Eklendi", "✓ Added");
    public static string NoResults => T("Sonuç yok", "No results");
    public static string BrowseFile => T("Dosyadan seç (.exe / kısayol)…", "Pick a file (.exe / shortcut)…");
    public static string Done => T("Bitti", "Done");

    // ── discord widget ───────────────────────────────────────────
    public static string Controls => T("Kontroller", "Controls");
    public static string Channels => T("Kanallar", "Channels");
    public static string Mute => T("Sustur", "Mute");
    public static string Unmute => T("Sesi aç", "Unmute");
    public static string Deafen => T("Sağırlaştır", "Deafen");
    public static string Undeafen => T("Duyulabilir yap", "Undeafen");
    public static string VoiceActivity => T("Ses etkinliği", "Voice activity");
    public static string PushToTalk => T("Bas-konuş", "Push to talk");
    public static string Leave => T("Ayrıl", "Leave");
    public static string NotInVoice => T("Bir ses kanalında değilsin", "You're not in a voice channel");
    public static string MicVolume => T("Mikrofon sesi", "Mic volume");
    public static string OutputVolume => T("Çıkış sesi", "Output volume");
    public static string AudioProcessing => T("SES İŞLEME", "AUDIO");
    public static string NoiseSuppression => T("Gürültü engelleme", "Noise suppression");
    public static string EchoCancellation => T("Yankı giderme", "Echo cancellation");
    public static string AutoGain => T("Otomatik kazanç", "Automatic gain");
    public static string QosPriority => T("QoS önceliği", "QoS priority");

    public static string PillNotConnected => T("BAĞLI DEĞİL", "NOT CONNECTED");
    public static string PillNotInChannel => T("KANALDA DEĞİL", "NOT IN A CHANNEL");
    public static string PillDiscordClosed => T("DISCORD KAPALI", "DISCORD CLOSED");
    public static string PillConnecting => T("BAĞLANIYOR…", "CONNECTING…");
    public static string PillError => T("HATA", "ERROR");

    public static string OvConnectTitle => T("Discord'u bağla", "Connect Discord");
    public static string OvConnectText => T("Ayarlar'a kendi Discord uygulamanın Client ID ve Client Secret bilgisini gir (discord.com/developers üzerinden ücretsiz oluşturulur).",
                                            "Enter your own Discord application's Client ID and Client Secret in Settings (free to create at discord.com/developers).");
    public static string OpenSettings => T("Ayarları aç", "Open settings");
    public static string OvClosedTitle => T("Discord kapalı", "Discord is closed");
    public static string OvClosedNeedAuthText => T("Önce Discord'u aç; ardından bir kez \"Bağlan\" ile yetkilendirmen gerekecek.",
                                                   "Open Discord first; then authorize once with \"Connect\".");
    public static string OvClosedText => T("Discord'u buradan açabilirsin; açılınca otomatik bağlanılacak.",
                                           "You can open Discord from here; the panel connects as soon as it's up.");
    public static string OpenDiscord => T("Discord'u Aç", "Open Discord");
    public static string OvNeedAuthTitle => T("Yetkilendirme gerekli", "Authorization needed");
    public static string OvNeedAuthText => T("Bağlan'a dokun, ardından Discord'da açılan pencerede \"Yetkilendir\"e bas. Bu yalnızca bir kez gerekir.",
                                             "Tap Connect, then click \"Authorize\" in the window Discord opens. You only need to do this once.");
    public static string Connect => T("Bağlan", "Connect");
    public static string OvLaunchingTitle => T("Discord açılıyor…", "Opening Discord…");
    public static string OvLaunchingText => T("Discord hazır olunca otomatik bağlanılacak.", "The panel connects as soon as Discord is ready.");
    public static string Connecting => T("Bağlanıyor…", "Connecting…");
    public static string OvAuthorizingTitle => T("Discord'a bak", "Check Discord");
    public static string OvErrorTitle => T("Bağlanamadı", "Couldn't connect");
    public static string Retry => T("Tekrar dene", "Try again");

    public static string ConnectFirst => T("Önce Discord'a bağlan.", "Connect to Discord first.");
    public static string LoadingServers => T("Sunucular yükleniyor…", "Loading servers…");
    public static string NoServers => T("Sunucu bulunamadı.", "No servers found.");
    public static string ServersFailed => T("Sunucular alınamadı: ", "Couldn't load servers: ");
    public static string NoVoiceChannels => T("Bu sunucuda ses kanalı yok.", "This server has no voice channels.");

    // ── discord service messages ─────────────────────────────────
    public static string DsNotRunning => T("Discord masaüstü uygulaması açık değil.", "The Discord desktop app isn't running.");
    public static string DsRejected(string err) => T($"Discord bağlantıyı reddetti: {err} (Client ID doğru mu?)",
                                                     $"Discord refused the connection: {err} (is the Client ID right?)");
    public static string DsAuthorize => T("Discord'da açılan pencerede \"Yetkilendir\"e bas.", "Click \"Authorize\" in the window Discord opened.");
    public static string DsAuthFailed => T("Discord kimlik doğrulaması başarısız.", "Discord authentication failed.");
    public static string DsNotFound => T("Discord bulunamadı. Kurulu olduğundan emin ol.", "Discord wasn't found. Make sure it's installed.");
    public static string DsLaunching => T("Discord açılıyor…", "Opening Discord…");
    public static string DsClosedReconnect => T("Discord kapandı, yeniden bağlanılacak…", "Discord closed; will reconnect…");
    public static string DsTokenFailed(string err) => T(
        $"Discord token alınamadı: {err}. Client Secret'ı ve OAuth2 Redirect olarak http://localhost eklendiğini kontrol et.",
        $"Couldn't get a Discord token: {err}. Check the Client Secret and that http://localhost is an OAuth2 redirect.");
    public static string RpcNoConnection => T("Discord bağlantısı yok", "Not connected to Discord");
    public static string RpcTimeout(string cmd) => T($"{cmd} zaman aşımı", $"{cmd} timed out");
    public static string RpcError => T("Discord hatası", "Discord error");
    public static string RpcClosed => T("Discord bağlantısı kapandı", "Discord connection closed");

    // ── settings window ──────────────────────────────────────────
    public static string SettingsTitle => T($"{AppInfo.Name} Ayarları", $"{AppInfo.Name} Settings");
    public static string Version => T("sürüm", "version");
    public static string SecLanguage => T("DİL", "LANGUAGE");
    public static string LangAuto => T("Otomatik (Windows)", "Automatic (Windows)");
    public static string SecDisplay => T("EKRAN", "DISPLAY");
    public static string MonitorAuto => T("Otomatik (Xeneon Edge)", "Automatic (Xeneon Edge)");
    public static string MonitorName(int n, int w, int h, bool primary) =>
        T($"Ekran {n}: {w}×{h}{(primary ? " (ana)" : "")}", $"Display {n}: {w}×{h}{(primary ? " (primary)" : "")}");
    public static string NoActivate => T("Dokununca odak çalmasın (oyundayken önerilir)", "Don't steal focus on touch (recommended while gaming)");
    public static string StartWithWindows => T("Windows ile başlat", "Start with Windows");
    public static string SecWeather => T("HAVA DURUMU", "WEATHER");
    public static string CurrentCity(string c) => T($"Şu anki şehir: {c}", $"Current city: {c}");
    public static string PickedCity(string name, string detail) => T($"Seçilen şehir: {name} ({detail}) — kaydetmeyi unutma",
                                                                     $"Selected: {name} ({detail}) — don't forget to save");
    public static string Search => T("Ara", "Search");
    public static string SearchFailed => T("Arama başarısız: ", "Search failed: ");
    public static string DiscordHelpBefore => T("discord.com/developers/applications → New Application (ya da var olan uygulaman) → OAuth2 sayfasında Client ID ve Client Secret. Redirects listesinde ",
                                                "discord.com/developers/applications → New Application (or an existing one) → Client ID and Client Secret on the OAuth2 page. The Redirects list must contain ");
    public static string DiscordHelpAfter => T(" olmalı.", ".");
    public static string SecretHint => T("Kayıtlı bir secret var; değiştirmek istemiyorsan boş bırak.", "A secret is saved; leave this empty to keep it.");
    public static string SaveAndConnect => T("Kaydet ve bağlan", "Save and connect");
    public static string Disconnect => T("Bağlantıyı kaldır", "Disconnect");
    public static string SecLeagues => T("LİGLER", "LEAGUES");
    public static string Quit => T("Paneli kapat", "Quit the panel");
    public static string Cancel => T("Vazgeç", "Cancel");
    public static string Save => T("Kaydet", "Save");
    public static string StConnected(string user) => T($"Bağlı: {user}", $"Connected: {user}");
    public static string StAuthorizing => T("Discord'da \"Yetkilendir\"e bas…", "Click \"Authorize\" in Discord…");
    public static string StOffline => T("Discord açık değil", "Discord isn't running");
    public static string StNeedAuth => T("Yetkilendirme bekliyor", "Waiting for authorization");
    public static string StNotSetUp => T("Ayarlanmadı", "Not set up");
}
