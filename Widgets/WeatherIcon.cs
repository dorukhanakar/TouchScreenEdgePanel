using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace EdgePanel.Widgets;

/// <summary>Tiny vector weather icons on a 24x24 canvas (WMO weather codes from Open-Meteo).</summary>
public static class WeatherIcon
{
    static SolidColorBrush B(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    static readonly Brush SunB = B("#FFFFC83D"), MoonB = B("#FFC7D2FE"), CloudB = B("#FFE5E7EB"),
        CloudDarkB = B("#FF9CA3AF"), RainB = B("#FF60A5FA"), BoltB = B("#FFFACC15");

    static readonly Geometry CloudGeo = Geometry.Parse(
        "M6.5,19 L17.5,19 C20,19 22,17.2 22,14.8 C22,12.5 20.2,10.7 18,10.6 C17.3,7.9 14.9,6 12,6 " +
        "C9,6 6.6,8.1 6.1,10.9 C3.8,11.2 2,13.1 2,15.2 C2,17.3 3.9,19 6.5,19 Z");

    public static UIElement Create(int code, bool day)
    {
        var c = new Canvas { Width = 24, Height = 24 };
        switch (code)
        {
            case 0:
                Sky(c, 12, 12, 5, day); break;
            case 1 or 2:
                Sky(c, 8.5, 8, 3.6, day); Cloud(c, CloudB, 1.5); break;
            case 3:
                Cloud(c, CloudB, 0); break;
            case 45 or 48:
                Cloud(c, CloudDarkB, -3);
                for (int i = 0; i < 2; i++) Line(c, 4, 19 + i * 3, 20, 19 + i * 3, CloudDarkB, 1.6);
                break;
            case >= 71 and <= 77 or 85 or 86:
                Cloud(c, CloudB, -4);
                foreach (var x in new[] { 7.0, 12, 17 }) Dot(c, x, 20, 1.4, CloudB);
                break;
            case >= 95:
                Cloud(c, CloudDarkB, -4);
                c.Children.Add(new Path { Fill = BoltB, Data = Geometry.Parse("M12.5,13 L9,19 L12,19 L10.5,24 L15.5,17 L12.5,17 L14,13 Z") });
                break;
            case >= 51 and <= 67 or >= 80 and <= 82:
                Cloud(c, CloudB, -4);
                foreach (var x in new[] { 8.0, 12, 16 }) Line(c, x, 18, x - 1.5, 22, RainB, 1.6);
                break;
            default:
                Cloud(c, CloudB, 0); break;
        }
        return new Viewbox { Child = c };
    }

    static void Sky(Canvas c, double cx, double cy, double r, bool day)
    {
        if (!day)
        {
            var moon = new CombinedGeometry(GeometryCombineMode.Exclude,
                new EllipseGeometry(new Point(cx, cy), r * 1.15, r * 1.15),
                new EllipseGeometry(new Point(cx + r * 0.7, cy - r * 0.45), r, r));
            c.Children.Add(new Path { Fill = MoonB, Data = moon });
            return;
        }
        c.Children.Add(new Path { Fill = SunB, Data = new EllipseGeometry(new Point(cx, cy), r, r) });
        for (int i = 0; i < 8; i++)
        {
            var a = i * Math.PI / 4;
            double r1 = r * 1.45, r2 = r * 1.95;
            Line(c, cx + Math.Cos(a) * r1, cy + Math.Sin(a) * r1, cx + Math.Cos(a) * r2, cy + Math.Sin(a) * r2, SunB, r * 0.3);
        }
    }

    static void Cloud(Canvas c, Brush fill, double dy)
    {
        var p = new Path { Fill = fill, Data = CloudGeo };
        if (dy != 0) p.RenderTransform = new TranslateTransform(0, dy);
        c.Children.Add(p);
    }

    static void Line(Canvas c, double x1, double y1, double x2, double y2, Brush b, double t) =>
        c.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = b, StrokeThickness = t,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });

    static void Dot(Canvas c, double x, double y, double r, Brush b) =>
        c.Children.Add(new Path { Fill = b, Data = new EllipseGeometry(new Point(x, y), r, r) });
}
