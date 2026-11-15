using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Guardrail.Views;

/// <summary>Symbols drawn on placards and GHS pictograms (simplified drawings, not official artwork).</summary>
public enum HazardSymbol { None, Flame, FlameOverCircle, Cylinder, Skull, Trefoil, Biohazard, Burst, Corrosion, Exclamation, HealthHazard, Environment, Battery }

/// <summary>How a placard's background is painted.</summary>
public enum PlacardFill { Solid, TopBottom, RedStripes, Class9, Dangerous }

/// <summary>A U.S. DOT placard or label.</summary>
public sealed record PlacardSpec(
    string Name, string ClassText, string Division, Color Top, Color Bottom, HazardSymbol Symbol, Color Ink,
    string Words, string Guide, string Note, PlacardFill Fill = PlacardFill.Solid);

/// <summary>Draws placards, GHS pictograms and the NFPA 704 diamond as vector shapes, so they stay sharp at any size and in either theme.</summary>
public static class HazardSigns
{
    public static readonly Color Orange = Color.FromRgb(0xF2, 0x84, 0x1D);
    public static readonly Color Red = Color.FromRgb(0xDA, 0x24, 0x32);
    public static readonly Color Green = Color.FromRgb(0x00, 0x8F, 0x5D);
    public static readonly Color Yellow = Color.FromRgb(0xFF, 0xD7, 0x00);
    public static readonly Color Blue = Color.FromRgb(0x1F, 0x6F, 0xC9);
    public static readonly Color White = Colors.White;
    public static readonly Color Black = Color.FromRgb(0x14, 0x14, 0x14);

    private static readonly Geometry DiamondGeometry = Geometry.Parse("M50,0 L100,50 L50,100 L0,50 Z");

    // ----- Symbols (24 × 24 box) -----

    private const string FlameData = "M12,1 C13.2,6 18.2,8.6 18.2,14.6 C18.2,19.2 15.4,23 12,23 C8.6,23 5.8,20 5.8,16.2 C5.8,12.6 8.4,11 8.9,7.4 C10.5,8.9 11.1,10.6 11.1,12.6 C12.9,10.6 13.7,6.2 12,1 Z";

    public static FrameworkElement Symbol(HazardSymbol symbol, Color ink, double size)
    {
        var brush = new SolidColorBrush(ink);
        brush.Freeze();
        FrameworkElement shape;
        switch (symbol)
        {
            case HazardSymbol.Skull: shape = Glyph("☠", brush); break;
            case HazardSymbol.Trefoil: shape = Glyph("☢", brush); break;
            case HazardSymbol.Biohazard: shape = Glyph("☣", brush); break;
            case HazardSymbol.Exclamation:
                shape = new TextBlock { Text = "!", FontSize = 22, FontWeight = FontWeights.Black, Foreground = brush, HorizontalAlignment = HorizontalAlignment.Center, FontFamily = new FontFamily("Segoe UI") };
                break;
            case HazardSymbol.None:
                return new Border { Width = size, Height = size };
            default:
                var canvas = new Canvas { Width = 24, Height = 24 };
                foreach (var p in Paths(symbol, brush)) canvas.Children.Add(p);
                shape = canvas;
                break;
        }
        return new Viewbox { Width = size, Height = size, Child = shape, Stretch = Stretch.Uniform };
    }

    private static TextBlock Glyph(string text, Brush brush) =>
        new() { Text = text, FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 22, Foreground = brush, HorizontalAlignment = HorizontalAlignment.Center };

    private static IEnumerable<Shape> Paths(HazardSymbol symbol, Brush ink)
    {
        Path P(string data, bool fill = true, double stroke = 0) => new()
        {
            Data = Geometry.Parse(data), Fill = fill ? ink : null, Stroke = stroke > 0 ? ink : null, StrokeThickness = stroke,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        };
        switch (symbol)
        {
            case HazardSymbol.Flame:
                yield return P(FlameData);
                break;
            case HazardSymbol.FlameOverCircle:
                yield return P("M12,0.5 C12.8,4 16.2,5.6 16.2,9.6 C16.2,12.6 14.3,14.6 12,14.6 C9.7,14.6 7.8,12.8 7.8,10.3 C7.8,7.9 9.6,6.9 9.9,4.5 C11,5.5 11.4,6.6 11.4,8 C12.6,6.6 13.1,3.8 12,0.5 Z");
                yield return P("M5,19.5 A7,4 0 1 0 19,19.5 A7,4 0 1 0 5,19.5 Z");
                break;
            case HazardSymbol.Cylinder:
                var cyl = P("M3.5,17.5 L17.5,8.5 A2.6,2.6 0 0 1 20.3,12.9 L6.3,21.9 A2.6,2.6 0 0 1 3.5,17.5 Z M18.8,7.6 L21.6,5.8 L22.6,7.4 L19.8,9.2 Z");
                yield return cyl;
                break;
            case HazardSymbol.Burst:
                yield return P("M12,3 L13.6,8.2 L18.4,5.4 L15.9,10.3 L21.4,11 L16.4,13.3 L19.8,17.8 L14.4,16 L13.3,21.4 L11,16.4 L6.9,20.2 L8.1,14.8 L2.6,14.4 L7.4,11.6 L3.6,7.6 L9,8.6 Z");
                yield return P("M3,3 L4.6,4.2 L3.4,5.4 Z M20.5,2.5 L21.8,4.4 L19.8,4.6 Z M21.5,20 L22.5,22 L20.5,21.6 Z M2.5,20.6 L4.2,19.4 L4.4,21.6 Z");
                break;
            case HazardSymbol.Corrosion:
                // Liquid dripping from two test tubes onto a hand and a bar of metal (simplified).
                yield return P("M3,2 L8.5,6.5 L7,8.3 L1.5,3.8 Z M15.5,2 L21,6.5 L19.5,8.3 L14,3.8 Z");
                yield return P("M8.2,9.8 C8.2,9.8 6.8,12 6.8,12.9 A1.4,1.4 0 0 0 9.6,12.9 C9.6,12 8.2,9.8 8.2,9.8 Z M20.2,9.8 C20.2,9.8 18.8,12 18.8,12.9 A1.4,1.4 0 0 0 21.6,12.9 C21.6,12 20.2,9.8 20.2,9.8 Z");
                yield return P("M1.5,19.5 L5,16.5 L7,17 L9.5,15.5 L11,17.5 L9,20.5 L1.5,21.5 Z M14,17.5 L23,17.5 L23,21.5 L14,21.5 L15.5,19.5 Z");
                break;
            case HazardSymbol.HealthHazard:
                yield return P("M12,1.5 A3.6,3.6 0 1 0 12,8.7 A3.6,3.6 0 1 0 12,1.5 Z M3.5,23 C3.5,14.5 7.5,10 12,10 C16.5,10 20.5,14.5 20.5,23 Z");
                yield return new Path { Data = Geometry.Parse("M12,12.2 L13.3,15.6 L16.8,16.6 L13.3,17.6 L12,21 L10.7,17.6 L7.2,16.6 L10.7,15.6 Z"), Fill = Brushes.White };
                break;
            case HazardSymbol.Environment:
                yield return P("M6,22 L6,8 M6,12 L2.5,8 M6,14 L10,9.5 M6,10.5 L8.5,6.5 M6,9 L4,4.5", fill: false, stroke: 1.6);
                yield return P("M11,18.5 C13.5,15 18.5,15 20.5,18.5 C18.5,22 13.5,22 11,18.5 Z M20.3,18.5 L23.5,16 L23.5,21 Z");
                yield return P("M1,22.8 L23,22.8", fill: false, stroke: 1.2);
                break;
            case HazardSymbol.Battery:
                yield return P("M4,9 L18,9 L18,19 L4,19 Z M18,12 L20.5,12 L20.5,16 L18,16 Z");
                break;
        }
    }

    // ----- Placards -----

    /// <summary>A diamond placard roughly <paramref name="size"/> pixels square.</summary>
    public static FrameworkElement Placard(PlacardSpec spec, double size)
    {
        var canvas = new Canvas { Width = 100, Height = 100, ClipToBounds = false };
        var inner = new Canvas { Width = 100, Height = 100, Clip = DiamondGeometry };
        Brush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        void Rect(double x, double y, double w, double h, Color c)
        {
            var r = new Rectangle { Width = w, Height = h, Fill = B(c) };
            Canvas.SetLeft(r, x); Canvas.SetTop(r, y);
            inner.Children.Add(r);
        }

        switch (spec.Fill)
        {
            case PlacardFill.TopBottom:
                Rect(0, 0, 100, 50, spec.Top); Rect(0, 50, 100, 50, spec.Bottom);
                break;
            case PlacardFill.RedStripes:
                Rect(0, 0, 100, 100, White);
                for (int i = 0; i < 7; i++) Rect(8 + i * 12.6, 0, 6.3, 100, Red);
                break;
            case PlacardFill.Class9:
                Rect(0, 0, 100, 100, White);
                for (int i = 0; i < 7; i++) Rect(24 + i * 7.6, 0, 3.8, 50, Black);
                break;
            case PlacardFill.Dangerous:
                Rect(0, 0, 100, 100, White);
                inner.Children.Add(new Path { Data = Geometry.Parse("M50,0 L86,36 L14,36 Z"), Fill = B(Red) });
                inner.Children.Add(new Path { Data = Geometry.Parse("M14,64 L86,64 L50,100 Z"), Fill = B(Red) });
                break;
            default:
                Rect(0, 0, 100, 100, spec.Top);
                break;
        }
        canvas.Children.Add(inner);

        // Thin inner border line, then the outer edge.
        canvas.Children.Add(new Path { Data = Geometry.Parse("M50,5 L95,50 L50,95 L5,50 Z"), Stroke = B(spec.Ink == White ? White : Black), StrokeThickness = 1.2 });
        canvas.Children.Add(new Path { Data = DiamondGeometry, Stroke = B(Color.FromRgb(0x90, 0x90, 0x90)), StrokeThickness = 0.8 });

        if (spec.Symbol != HazardSymbol.None)
        {
            var sym = Symbol(spec.Symbol, spec.Ink, 30);
            Canvas.SetLeft(sym, 35); Canvas.SetTop(sym, spec.Fill == PlacardFill.Class9 ? 54 : 12);
            canvas.Children.Add(sym);
        }
        if (spec.Words.Length > 0)
        {
            var words = new TextBlock
            {
                Text = spec.Words, FontSize = spec.Words.Length > 12 ? 6.2 : 8, FontWeight = FontWeights.Black, Width = 70, TextAlignment = TextAlignment.Center,
                Foreground = B(spec.Fill == PlacardFill.Dangerous ? Black : spec.Fill == PlacardFill.TopBottom && IsDark(spec.Bottom) ? White : spec.Ink), TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Arial Narrow, Arial"), LineHeight = 7, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            };
            Canvas.SetLeft(words, 15); Canvas.SetTop(words, spec.Fill == PlacardFill.Dangerous ? 45 : spec.Fill == PlacardFill.Class9 ? 44 : 52);
            canvas.Children.Add(words);
        }
        var cls = new TextBlock
        {
            Text = spec.Division, FontSize = 11, FontWeight = FontWeights.Bold, Width = 30, TextAlignment = TextAlignment.Center,
            Foreground = B(spec.Fill is PlacardFill.TopBottom ? (IsDark(spec.Bottom) ? White : Black) : spec.Fill == PlacardFill.Dangerous ? White : spec.Ink),
        };
        if (spec.Fill == PlacardFill.RedStripes) cls.Background = B(White);
        Canvas.SetLeft(cls, 35); Canvas.SetTop(cls, 79);
        canvas.Children.Add(cls);

        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    private static bool IsDark(Color c) => c.R * 0.3 + c.G * 0.59 + c.B * 0.11 < 128;

    // ----- GHS pictograms -----

    /// <summary>A GHS pictogram: black symbol on white inside a red-bordered diamond.</summary>
    public static FrameworkElement Pictogram(HazardSymbol symbol, double size)
    {
        var canvas = new Canvas { Width = 100, Height = 100 };
        canvas.Children.Add(new Path { Data = Geometry.Parse("M50,4 L96,50 L50,96 L4,50 Z"), Fill = Brushes.White, Stroke = new SolidColorBrush(Red), StrokeThickness = 8, StrokeLineJoin = PenLineJoin.Miter });
        var sym = Symbol(symbol, Black, 46);
        Canvas.SetLeft(sym, 27); Canvas.SetTop(sym, 27);
        canvas.Children.Add(sym);
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    // ----- NFPA 704 -----

    /// <summary>The NFPA 704 "fire diamond" with the given ratings.</summary>
    public static FrameworkElement Nfpa704(int health, int fire, int instability, string special, double size)
    {
        var canvas = new Canvas { Width = 100, Height = 100 };
        void Quarter(string data, Color fill, string text, double x, double y, Color ink)
        {
            canvas.Children.Add(new Path { Data = Geometry.Parse(data), Fill = new SolidColorBrush(fill), Stroke = Brushes.Black, StrokeThickness = 1.2 });
            var t = new TextBlock { Text = text, FontSize = text.Length > 1 ? 13 : 20, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(ink), Width = 40, TextAlignment = TextAlignment.Center };
            Canvas.SetLeft(t, x - 20); Canvas.SetTop(t, y - (text.Length > 1 ? 9 : 14));
            canvas.Children.Add(t);
        }
        Quarter("M50,1 L74.5,25.5 L50,50 L25.5,25.5 Z", Red, fire.ToString(), 50, 25.5, White);
        Quarter("M25.5,25.5 L50,50 L25.5,74.5 L1,50 Z", Blue, health.ToString(), 25.5, 50, White);
        Quarter("M74.5,25.5 L99,50 L74.5,74.5 L50,50 Z", Yellow, instability.ToString(), 74.5, 50, Black);
        Quarter("M50,50 L74.5,74.5 L50,99 L25.5,74.5 Z", White, special, 50, 74.5, Black);
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }
}
