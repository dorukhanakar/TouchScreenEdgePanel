using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EdgePanel.Core;

public static class Http
{
    public static readonly HttpClient Client = Create();

    static HttpClient Create()
    {
        var h = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(60),
        };
        var c = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.Name + "/1.0");
        return c;
    }
}

/// <summary>Small memory + disk cache for remote images (team logos, avatars). UI thread only.</summary>
public static class ImageCache
{
    static readonly Dictionary<string, ImageSource?> Mem = new();
    static readonly Dictionary<string, Task<ImageSource?>> Pending = new();

    static string DiskDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name, "cache");

    public static async Task<ImageSource?> GetAsync(string url, int decodePx)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var key = url + "|" + decodePx;
        if (Mem.TryGetValue(key, out var img)) return img;
        if (!Pending.TryGetValue(key, out var task))
        {
            task = LoadAsync(url, decodePx);
            Pending[key] = task;
        }
        img = await task;
        Pending.Remove(key);
        Mem[key] = img;
        return img;
    }

    static async Task<ImageSource?> LoadAsync(string url, int px)
    {
        try
        {
            var file = Path.Combine(DiskDir, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url))) + ".img");
            byte[] bytes;
            if (File.Exists(file) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(file)).TotalDays < 30)
                bytes = await File.ReadAllBytesAsync(file);
            else
            {
                bytes = await Http.Client.GetByteArrayAsync(url);
                Directory.CreateDirectory(DiskDir);
                await File.WriteAllBytesAsync(file, bytes);
            }
            return await Task.Run(() => Decode(bytes, px));
        }
        catch { return null; }
    }

    /// <summary>Crops fully transparent borders (alpha ≤ 8) off an image, leaving a small margin. Opaque images come back unchanged.</summary>
    public static ImageSource? TrimTransparent(BitmapSource? src)
    {
        if (src == null) return null;
        try
        {
            var bgra = src.Format == PixelFormats.Bgra32 || src.Format == PixelFormats.Pbgra32
                ? src : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
            var px = new byte[stride * h];
            bgra.CopyPixels(px, stride, 0);

            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (px[y * stride + x * 4 + 3] > 8)
                    {
                        if (x < x0) x0 = x;
                        if (x > x1) x1 = x;
                        if (y < y0) y0 = y;
                        if (y > y1) y1 = y;
                    }
            if (x1 < 0) return src;                                   // fully transparent
            int pad = Math.Max(2, (Math.Max(x1 - x0, y1 - y0) + 1) / 40);
            x0 = Math.Max(0, x0 - pad); y0 = Math.Max(0, y0 - pad);
            x1 = Math.Min(w - 1, x1 + pad); y1 = Math.Min(h - 1, y1 + pad);
            if (x0 == 0 && y0 == 0 && x1 == w - 1 && y1 == h - 1) return src;   // nothing to trim

            var cropped = new CroppedBitmap(bgra, new System.Windows.Int32Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1));
            cropped.Freeze();
            return cropped;
        }
        catch { return src; }
    }

    public static ImageSource? Decode(byte[] bytes, int px)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            if (px > 0) bi.DecodePixelWidth = px;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }
}
