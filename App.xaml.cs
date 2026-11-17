using System.Windows;
using EdgePanel.Core;

namespace EdgePanel;

public partial class App : Application
{
    Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, @"Local\TouchScreenEdgePanel.SingleInstance", out bool created);
        if (!created)
        {
            // a restart (language change) waits for the old instance to exit; any other second copy just quits
            bool acquired = false;
            if (e.Args.Contains("--restart"))
                try { acquired = _mutex.WaitOne(TimeSpan.FromSeconds(15)); }
                catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) { Shutdown(); return; }
        }

        DispatcherUnhandledException += (_, a) => { Log(a.Exception); a.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Log(a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) => { Log(a.Exception); a.SetObserved(); };

        AppSettings.Load();
        L.Init(AppSettings.Current.Language);
        SettingsWindow.MigrateAutostart();
        // The panel is mostly static: CPU rendering skips the GPU driver's ~70 MB per-process overhead.
        if (!AppSettings.Current.GpuRendering)
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        base.OnStartup(e);

        var w = new MainWindow();
        MainWindow = w;
        w.Show();
        if (e.Args.Contains("--settings")) SettingsWindow.ShowSingle();
    }

    /// <summary>Starts a fresh copy (which waits for this one to exit) and quits.</summary>
    public static void Restart()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--restart") { UseShellExecute = false })?.Dispose();
            Current.Shutdown();
        }
        catch (Exception ex) { Log(ex); }
    }

    public static void Log(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Dir);
            var file = Path.Combine(AppSettings.Dir, "error.log");
            if (File.Exists(file) && new FileInfo(file).Length > 512 * 1024) File.Delete(file);
            File.AppendAllText(file, $"{DateTime.Now:u} {ex}\n\n");
        }
        catch { }
    }
}
