using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Ritmo;
public static class Icons
{
    public static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(43, 227, 124));
    public static readonly Brush Text = new SolidColorBrush(Color.FromRgb(243, 248, 245));
    public static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(147, 166, 159));
    static readonly Dictionary<string, Geometry> shapes = [];
    static Geometry Shape(string name)
    {
        if (shapes.TryGetValue(name, out var shape)) return shape;
        shape = Geometry.Parse(Data(name)); shape.Freeze(); shapes[name] = shape; return shape;
    }
    public static string Data(string name) => name switch
    {
        "home" => "M3,10 L12,3 21,10 M5,9 L5,21 19,21 19,9 M9,21 L9,14 15,14 15,21",
        "search" => "M17,10 A7,7 0 1 1 3,10 A7,7 0 1 1 17,10 M15,15 L22,22",
        "heart" => "M12,21 C8,18 2,14 2,8 C2,2 9,2 12,6 C15,2 22,2 22,8 C22,14 16,18 12,21 Z",
        "folder" => "M2,6 L9,6 11,9 22,9 22,21 2,21 Z M2,6 L2,3 9,3 11,6 21,6 21,9",
        "download" => "M12,2 L12,16 M6,10 L12,16 18,10 M3,17 L3,22 21,22 21,17",
        "check" => "M4,12 L9,17 20,5",
        "play" => "M8,4 L21,12 8,20 Z",
        "pause" => "M7,4 L7,20 M17,4 L17,20",
        "previous" => "M5,4 L5,20 M20,4 L8,12 20,20 Z",
        "next" => "M19,4 L19,20 M4,4 L16,12 4,20 Z",
        "shuffle" => "M2,6 L5,6 C10,6 14,18 19,18 L22,18 M18,14 L22,18 18,22 M2,18 L5,18 C10,18 14,6 19,6 L22,6 M18,2 L22,6 18,10",
        "repeat" => "M18,2 L22,6 18,10 M22,6 L6,6 Q2,6 2,11 M6,14 L2,18 6,22 M2,18 L18,18 Q22,18 22,13",
        "lyrics" => "M9,4 A3,3 0 0 1 15,4 L15,11 A3,3 0 0 1 9,11 Z M5,10 L5,11 A7,7 0 0 0 19,11 L19,10 M12,18 L12,23 M8,23 L16,23",
        "queue" => "M3,5 L22,5 M3,12 L22,12 M3,19 L16,19",
        "close" => "M5,5 L19,19 M19,5 L5,19",
        "plus" => "M12,3 L12,21 M3,12 L21,12",
        "back" => "M16,4 L8,12 16,20",
        _ => "M12,2 L12,22 M2,12 L22,12"
    };
    public static FrameworkElement View(string name, Brush? brush = null, double size = 21) => new Path {
        Data = Shape(name), Stroke = brush ?? Text, StrokeThickness = name == "pause" ? 4 : 1.8,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        Fill = name == "play" ? brush ?? Text : Brushes.Transparent, Stretch = Stretch.Uniform, Width = size, Height = size,
        IsHitTestVisible = false
    };
    public static ImageSource Image(string name, Brush? brush = null)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
        group.Children.Add(new GeometryDrawing(name == "play" ? brush ?? Text : Brushes.Transparent, new Pen(brush ?? Text, name == "pause" ? 4 : 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, Shape(name)));
        var image = new DrawingImage(group); image.Freeze(); return image;
    }
}
