using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EdgePanel.Core;

namespace EdgePanel.Widgets;

public partial class AppsWidget : UserControl
{
    const double Gap = 12;
    bool _edit;
    double _tile = 120;
    int _cols = 1;

    // drag-to-reorder state
    int _dragFrom = -1, _dragTo = -1;
    bool _dragging;
    Point _downAt, _grabOffset;
    FrameworkElement? _dragHost;
    Border? _ghost;
    const double DragThreshold = 10;
    readonly Dictionary<string, ImageSource?> _icons = new(StringComparer.OrdinalIgnoreCase);

    static List<AppEntry> Apps => AppSettings.Current.Apps;

    public AppsWidget()
    {
        InitializeComponent();
        Loaded += (_, _) => Build();
    }

    // ── layout ───────────────────────────────────────────────────

    void Area_SizeChanged(object sender, SizeChangedEventArgs e) => Build();

    /// <summary>
    /// Auto-fit: try every column count and keep the one that gives the biggest square tiles that still
    /// fit the area, so any number of apps fills the panel. AppColumns &gt; 0 pins the column count instead.
    /// </summary>
    void ComputeTileSize()
    {
        int n = Math.Max(1, Apps.Count + (_edit ? 3 : 0));
        double w = Area.ActualWidth - 24, h = Area.ActualHeight - 24;
        if (w <= 0 || h <= 0) return;

        int bestCols = 1;
        double bestSlot = 0;
        int fixedCols = AppSettings.Current.AppColumns;
        foreach (var cols in fixedCols > 0 ? new[] { fixedCols } : Enumerable.Range(1, n).ToArray())
        {
            int rows = (int)Math.Ceiling(n / (double)cols);
            double slot = Math.Min(w / cols, h / rows);
            if (slot > bestSlot + 0.5) { bestSlot = slot; bestCols = cols; }   // ties → fewer columns (fuller rows)
        }

        _tile = Math.Floor(Math.Clamp(bestSlot - Gap, 40, 260));
        _cols = bestCols;
        Tiles.MaxWidth = (_tile + Gap) * bestCols + 1;
    }

    void Build()
    {
        if (!IsLoaded) return;
        ComputeTileSize();
        Tiles.Children.Clear();
        for (int i = 0; i < Apps.Count; i++) Tiles.Children.Add(MakeTile(Apps[i], i));
        if (_edit)
        {
            Tiles.Children.Add(MakeAddTile("", L.TileApp, ShowAppPicker));
            Tiles.Children.Add(MakeAddTile("", L.TileFolder, AddFolderViaDialog));
            Tiles.Children.Add(MakeAddTile("", L.TileSystem, ShowSystemPicker));
        }
        EmptyHint.Visibility = Apps.Count == 0 && !_edit ? Visibility.Visible : Visibility.Collapsed;
    }

    FrameworkElement MakeTile(AppEntry a, int index)
    {
        var img = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(_tile * 0.12), Source = Icon(a) };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

        var btn = new Button { Style = (Style)FindResource("TileButton"), Content = img, ToolTip = a.Name };
        btn.Click += (_, _) => { if (_edit) ChangeIcon(a); else Launch(a); };

        var host = new Grid { Width = _tile, Height = _tile, Margin = new Thickness(Gap / 2), Tag = index };
        host.Children.Add(btn);
        host.PreviewMouseLeftButtonDown += Tile_PreviewDown;
        if (_edit)
        {
            var x = Mini("", () => Remove(index), true);
            x.HorizontalAlignment = HorizontalAlignment.Right;
            x.VerticalAlignment = VerticalAlignment.Top;
            x.Margin = new Thickness(0, 5, 5, 0);
            host.Children.Add(x);
        }
        return host;
    }

    FrameworkElement MakeAddTile(string glyph, string label, Action onClick)
    {
        var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new TextBlock { Text = glyph, Style = (Style)FindResource("Glyph"), FontSize = _tile * 0.22, Foreground = (Brush)FindResource("Text2") });
        content.Children.Add(new TextBlock
        {
            Text = "+ " + label, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Text2"),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0),
        });
        var btn = new Button { Style = (Style)FindResource("TileButton"), Content = content, ToolTip = L.AddThing(label) };
        btn.Click += (_, _) => onClick();
        return new Grid { Width = _tile, Height = _tile, Margin = new Thickness(Gap / 2), Children = { btn } };
    }

    Button Mini(string glyph, Action click, bool danger)
    {
        var b = new Button
        {
            Style = (Style)FindResource("SquareButton"), Content = glyph, Width = 28, Height = 28, Margin = new Thickness(2, 0, 2, 0),
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x10, 0x11, 0x14)),
        };
        if (danger) { b.Foreground = (Brush)FindResource("Danger"); b.BorderBrush = (Brush)FindResource("Danger"); }
        b.Click += (_, e) => { e.Handled = true; click(); };
        return b;
    }

    // ── icons ────────────────────────────────────────────────────

    ImageSource? Icon(AppEntry a)
    {
        var key = a.Icon ?? a.Path;
        if (_icons.TryGetValue(key, out var cached)) return cached;
        ImageSource? img = null;
        if (!string.IsNullOrEmpty(a.Icon) && File.Exists(a.Icon))
            // custom pictures often have wide empty (transparent) margins: trim them so the picture fills the tile
            img = ImageCache.TrimTransparent(ImageCache.Decode(File.ReadAllBytes(a.Icon), 512) as System.Windows.Media.Imaging.BitmapSource);
        img ??= Native.GetShellImage(a.Path, 256);   // tiles can get big when there are few apps
        _icons[key] = img;
        return img;
    }

    // ── actions ──────────────────────────────────────────────────

    static void Launch(AppEntry a)
    {
        try
        {
            Native.AllowSetForegroundWindow(-1);   // let the launched app come to the front
            if (a.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                var shellArg = a.Path.Contains(' ') ? $"\"{a.Path}\"" : a.Path;   // e.g. "shell:My Pictures"
                Process.Start(new ProcessStartInfo("explorer.exe", shellArg) { UseShellExecute = true })?.Dispose();
                return;
            }
            if (Directory.Exists(a.Path))
            {
                // folders always open in File Explorer (not whatever app took over the folder "open" verb)
                // no trailing backslash inside quotes (it would escape the quote); a drive root stays "D:\" unquoted
                var target = a.Path.TrimEnd('\\');
                if (target.EndsWith(':')) target += "\\";
                var arg = target.Contains(' ') ? $"\"{target}\"" : target;
                Process.Start(new ProcessStartInfo("explorer.exe", arg) { UseShellExecute = true })?.Dispose();
                return;
            }
            var psi = new ProcessStartInfo(a.Path) { UseShellExecute = true };
            if (!string.IsNullOrWhiteSpace(a.Args)) psi.Arguments = a.Args;
            if (a.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                psi.WorkingDirectory = System.IO.Path.GetDirectoryName(a.Path) ?? "";
            Process.Start(psi)?.Dispose();
        }
        catch (Exception ex) { App.Log(ex); }
    }

    void Edit_Click(object sender, RoutedEventArgs e)
    {
        _edit = !_edit;
        if (!_edit) SystemPicker.Visibility = Visibility.Collapsed;
        EditBtn.Content = _edit ? "" : "";
        EditBtn.Foreground = (Brush)FindResource(_edit ? "Accent" : "Text2");
        EditHint.Visibility = _edit ? Visibility.Visible : Visibility.Collapsed;
        Build();
    }

    // ── drag to reorder (mouse or touch, edit mode only) ──

    void Tile_PreviewDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // reordering only in edit mode, so a tap-and-slide in normal use never moves a tile by accident
        if (!_edit || SystemPicker.Visibility == Visibility.Visible) return;
        _dragHost = (FrameworkElement)sender;
        _dragFrom = _dragTo = (int)_dragHost.Tag;
        _downAt = e.GetPosition(Area);
        _grabOffset = e.GetPosition(_dragHost);
        // not handled: a plain tap still clicks the tile
    }

    void Area_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragFrom < 0 || _dragHost == null) return;
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) { EndDrag(commit: _dragging); return; }

        if (!_dragging)
        {
            if ((e.GetPosition(Area) - _downAt).Length < DragThreshold) return;
            StartDrag();
        }

        var p = e.GetPosition(DragLayer);
        Canvas.SetLeft(_ghost!, p.X - _grabOffset.X);
        Canvas.SetTop(_ghost!, p.Y - _grabOffset.Y);

        int target = IndexAt(e.GetPosition(Tiles));
        if (target != _dragTo)
        {
            Tiles.Children.Remove(_dragHost);
            Tiles.Children.Insert(target, _dragHost);   // live preview: the others shift around the gap
            _dragTo = target;
        }
        e.Handled = true;
    }

    void Area_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_dragging) { EndDrag(commit: true); e.Handled = true; }
        else _dragFrom = -1;
    }

    void Area_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // LostMouseCapture bubbles: when the drag starts, the tile's button losing capture to Area arrives
        // here too. Only Area itself losing capture (e.g. Alt+Tab mid-drag) ends the drag.
        if (_dragging && ReferenceEquals(e.OriginalSource, Area) && !Area.IsMouseCaptured) EndDrag(commit: true);
    }

    void StartDrag()
    {
        var host = _dragHost!;
        Area.CaptureMouse();          // also cancels the tile's pending click
        _dragging = true;
        host.Opacity = 0.3;
        var img = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(_tile * 0.12), Source = Icon(Apps[_dragFrom]) };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        _ghost = new Border
        {
            Width = _tile, Height = _tile, CornerRadius = new CornerRadius(12), Child = img,
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x28, 0x2D)),
            BorderBrush = (Brush)FindResource("Accent"), BorderThickness = new Thickness(2),
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1.08, 1.08),
        };
        DragLayer.Children.Add(_ghost);
    }

    void EndDrag(bool commit)
    {
        int from = _dragFrom, to = _dragTo;
        _dragFrom = _dragTo = -1;
        bool wasDragging = _dragging;
        _dragging = false;
        if (Area.IsMouseCaptured) Area.ReleaseMouseCapture();   // may re-enter via LostMouseCapture; state is already reset
        DragLayer.Children.Clear();
        _ghost = null;
        _dragHost = null;
        if (!wasDragging) return;

        if (commit && from >= 0 && to >= 0 && from != to && from < Apps.Count)
        {
            var item = Apps[from];
            Apps.RemoveAt(from);
            Apps.Insert(Math.Clamp(to, 0, Apps.Count), item);
            AppSettings.Save();
        }
        Build();
    }

    /// <summary>Grid slot under a point (relative to the tile panel), clamped to the app tiles.</summary>
    int IndexAt(Point p)
    {
        double slot = _tile + Gap;
        int col = Math.Clamp((int)(p.X / slot), 0, _cols - 1);
        int row = Math.Max(0, (int)(p.Y / slot));
        return Math.Clamp(row * _cols + col, 0, Apps.Count - 1);
    }

    void Remove(int i)
    {
        if (i < 0 || i >= Apps.Count) return;
        Apps.RemoveAt(i);
        SaveAndBuild();
    }

    void SaveAndBuild() { AppSettings.Save(); Build(); }

    void AddFiles(IEnumerable<string> files)
    {
        foreach (var raw in files)
        {
            string f;
            try { f = System.IO.Path.GetFullPath(raw); } catch { continue; }   // "C:\\" → "C:\", relative → absolute
            if (Apps.Any(a => string.Equals(a.Path, f, StringComparison.OrdinalIgnoreCase))) continue;
            var name = Directory.Exists(f)
                ? System.IO.Path.GetFileName(f.TrimEnd('\\')) is { Length: > 0 } n ? n : f   // a drive root has no folder name
                : System.IO.Path.GetFileNameWithoutExtension(f);
            Apps.Add(new AppEntry { Name = name, Path = f });
        }
        SaveAndBuild();
    }

    // ── Windows virtual locations ────────────────────────────────

    static readonly (string Name, string Path)[] SystemItems =
    {
        (L.RecycleBin, "shell:RecycleBinFolder"),
        (L.ThisPC, "shell:MyComputerFolder"),
        (L.Desktop, "shell:Desktop"),
        (L.Documents, "shell:Personal"),
        (L.Downloads, "shell:Downloads"),
        (L.Pictures, "shell:My Pictures"),
        (L.ControlPanel, "shell:ControlPanelFolder"),
        (L.Network, "shell:NetworkPlacesFolder"),
    };

    void ShowSystemPicker()
    {
        SystemList.Children.Clear();
        foreach (var (name, path) in SystemItems)
        {
            var icon = new Image { Width = 48, Height = 48, Source = Native.GetShellImage(path, 96) };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            var content = new StackPanel { Width = 120 };
            content.Children.Add(icon);
            content.Children.Add(new TextBlock
            {
                Text = name, FontSize = 12, Margin = new Thickness(0, 8, 0, 0), TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center,
            });
            bool added = Apps.Any(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase));
            var b = new Button
            {
                Style = (Style)FindResource("FlatButton"), Content = content, Margin = new Thickness(5),
                Padding = new Thickness(6, 12, 6, 12), IsEnabled = !added, ToolTip = added ? L.AlreadyAdded : L.AddThing(name),
            };
            b.Click += (_, _) =>
            {
                Apps.Add(new AppEntry { Name = name, Path = path });
                SystemPicker.Visibility = Visibility.Collapsed;
                SaveAndBuild();
            };
            SystemList.Children.Add(b);
        }
        SystemPicker.Visibility = Visibility.Visible;
    }

    void SystemPickerClose_Click(object sender, RoutedEventArgs e) => SystemPicker.Visibility = Visibility.Collapsed;

    /// <summary>Start-menu app list (incl. Store apps such as Claude) with a "pick a file" fallback.</summary>
    void ShowAppPicker() => AppPickerWindow.ShowSingle(
        add: (name, path) => { Apps.Add(new AppEntry { Name = name, Path = path }); SaveAndBuild(); },
        exists: path => Apps.Any(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase)),
        browse: AddViaDialog);

    void AddFolderViaDialog()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = L.AddFolderTitle, Multiselect = true };
        if (dlg.ShowDialog() == true) AddFiles(dlg.FolderNames);
    }

    void AddViaDialog()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = L.AddAppTitle,
            Multiselect = true,
            DereferenceLinks = false,
            Filter = L.AppFileFilter,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) + @"\Programs",
        };
        if (dlg.ShowDialog() == true) AddFiles(dlg.FileNames);
    }

    void ChangeIcon(AppEntry a)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = L.ChooseIcon(a.Name),
            Filter = L.ImageFilter,
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            // copy into our own folder so the tile survives the original image being moved
            var dir = System.IO.Path.Combine(AppSettings.Dir, "icons");
            Directory.CreateDirectory(dir);
            var dest = System.IO.Path.Combine(dir, Guid.NewGuid().ToString("N")[..8] + System.IO.Path.GetExtension(dlg.FileName));
            File.Copy(dlg.FileName, dest, true);
            a.Icon = dest;
            SaveAndBuild();
        }
        catch (Exception ex) { App.Log(ex); }
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) AddFiles(files);
    }
}
