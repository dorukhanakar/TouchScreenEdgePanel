using System.ComponentModel;
using System.Windows.Media;

namespace EdgePanel.Core;

/// <summary>One entry of the Start menu's "All apps" list (shell:AppsFolder): Win32 and Store/MSIX apps alike.</summary>
public sealed class StartMenuApp : INotifyPropertyChanged
{
    public string Name { get; init; } = "";
    /// <summary>"shell:AppsFolder\&lt;AppUserModelID&gt;" — stable across app updates, launchable through Explorer.</summary>
    public string Path { get; init; } = "";

    ImageSource? _icon;
    bool _iconLoaded;
    /// <summary>Loaded on first use, i.e. only for rows the virtualised list actually shows.</summary>
    public ImageSource? Icon
    {
        get
        {
            if (!_iconLoaded) { _iconLoaded = true; _icon = Native.GetShellImage(Path, 48); }
            return _icon;
        }
    }

    bool _added;
    public bool Added
    {
        get => _added;
        set { _added = value; PropertyChanged?.Invoke(this, new(nameof(Added))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public static class StartMenuApps
{
    /// <summary>Enumerates shell:AppsFolder. Must run on an STA thread (the UI thread).</summary>
    public static List<StartMenuApp> Load()
    {
        var list = new List<StartMenuApp>();
        try
        {
            var t = Type.GetTypeFromProgID("Shell.Application");
            if (t == null) return list;
            dynamic shell = Activator.CreateInstance(t)!;
            dynamic folder = shell.NameSpace("shell:AppsFolder");
            foreach (dynamic item in folder.Items())
            {
                string name = item.Name;
                string id = item.Path;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id)) continue;
                if (name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase) || name.Contains("Kaldır", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new StartMenuApp { Name = name, Path = @"shell:AppsFolder\" + id });
            }
        }
        catch (Exception ex) { App.Log(ex); }
        var tr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
        return list.GroupBy(a => a.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                   .OrderBy(a => a.Name, StringComparer.Create(tr, true)).ToList();
    }
}
