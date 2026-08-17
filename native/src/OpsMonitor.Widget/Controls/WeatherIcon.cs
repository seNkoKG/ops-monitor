using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Xml.Linq;
using MediaColor = System.Windows.Media.Color;
using MediaPoint = System.Windows.Point;

namespace OpsMonitor.Widget.Controls;

/// <summary>
/// Native WPF renderer for bundled Meteocons artwork. SVG is parsed once into
/// WPF shapes, then each control instance receives its own animated scene.
/// </summary>
public sealed class WeatherIcon : ContentControl
{
    private const double SceneSize = 128;
    private static readonly ConcurrentDictionary<string, XDocument?> Documents = new();

    public static readonly DependencyProperty WeatherCodeProperty =
        DependencyProperty.Register(
            nameof(WeatherCode),
            typeof(int),
            typeof(WeatherIcon),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender, OnIconChanged));

    public static readonly DependencyProperty IsDayProperty =
        DependencyProperty.Register(
            nameof(IsDay),
            typeof(bool),
            typeof(WeatherIcon),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender, OnIconChanged));

    public static readonly DependencyProperty SubtleProperty =
        DependencyProperty.Register(
            nameof(Subtle),
            typeof(bool),
            typeof(WeatherIcon),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnIconChanged));

    public static bool MotionEnabled { get; set; } = true;

    private readonly Canvas _scene = new() { Width = SceneSize, Height = SceneSize };
    private Storyboard? _storyboard;
    private bool _loaded;
    private bool _visible;

    internal bool HasBundledAsset { get; private set; }

    public WeatherIcon()
    {
        IsHitTestVisible = false;
        Focusable = false;
        HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        VerticalAlignment = System.Windows.VerticalAlignment.Center;
        Content = new Viewbox
        {
            Stretch = Stretch.Uniform,
            Child = _scene
        };
        Loaded += (_, _) =>
        {
            _loaded = true;
            RefreshAnimation();
        };
        Unloaded += (_, _) =>
        {
            _loaded = false;
            StopAnimation();
        };
        IsVisibleChanged += (_, _) => RefreshAnimation();
        RebuildScene();
    }

    public int WeatherCode
    {
        get => (int)GetValue(WeatherCodeProperty);
        set => SetValue(WeatherCodeProperty, value);
    }

    public bool IsDay
    {
        get => (bool)GetValue(IsDayProperty);
        set => SetValue(IsDayProperty, value);
    }

    public bool Subtle
    {
        get => (bool)GetValue(SubtleProperty);
        set => SetValue(SubtleProperty, value);
    }

    private static void OnIconChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is WeatherIcon icon)
        {
            icon.RebuildScene();
        }
    }

    private void RebuildScene()
    {
        StopAnimation();
        _scene.Children.Clear();
        string asset = SelectAsset(WeatherCode, IsDay);
        XDocument? document = Documents.GetOrAdd(asset, LoadDocument);
        HasBundledAsset = document is not null;
        if (document is not null)
        {
            RenderSvg(document, _scene);
        }
        else
        {
            RenderFallback(_scene);
        }

        _storyboard = BuildMotion(asset);
        _visible = false;
        RefreshAnimation();
    }

    private static XDocument? LoadDocument(string asset)
    {
        string resourceName = $"{typeof(WeatherIcon).Assembly.GetName().Name}.Assets.Weather.{asset}";
        using System.IO.Stream? stream = typeof(WeatherIcon).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
    }

    private static bool ShouldAnimate => MotionEnabled && SystemParameters.ClientAreaAnimation;

    private void RefreshAnimation()
    {
        bool visible = _loaded && IsVisible;
        if (visible == _visible)
        {
            return;
        }

        _visible = visible;
        if (visible && ShouldAnimate)
        {
            _storyboard?.Begin(this, true);
        }
        else
        {
            StopAnimation();
        }
    }

    private void StopAnimation()
    {
        _storyboard?.Stop(this);
    }

    private Storyboard BuildMotion(string asset)
    {
        var storyboard = new Storyboard();
        var transform = new TransformGroup();
        var scale = new ScaleTransform(1, 1, SceneSize / 2, SceneSize / 2);
        var drift = new TranslateTransform();
        transform.Children.Add(scale);
        transform.Children.Add(drift);
        _scene.RenderTransform = transform;

        double duration = Subtle ? 5.5 : 3.8;
        var floatAnimation = new DoubleAnimation(-1.4, 1.4, TimeSpan.FromSeconds(duration))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(floatAnimation, drift);
        Storyboard.SetTargetProperty(floatAnimation, new PropertyPath(TranslateTransform.YProperty));
        storyboard.Children.Add(floatAnimation);

        var breathe = new DoubleAnimation(1, Subtle ? 1.015 : 1.035, TimeSpan.FromSeconds(duration + 1.2))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(breathe, scale);
        Storyboard.SetTargetProperty(breathe, new PropertyPath(ScaleTransform.ScaleXProperty));
        storyboard.Children.Add(breathe);

        var breatheY = breathe.Clone();
        Storyboard.SetTarget(breatheY, scale);
        Storyboard.SetTargetProperty(breatheY, new PropertyPath(ScaleTransform.ScaleYProperty));
        storyboard.Children.Add(breatheY);

        if (!Subtle && asset.Contains("clear-day", StringComparison.Ordinal))
        {
            var rotation = new RotateTransform(0, SceneSize / 2, SceneSize / 2);
            _scene.RenderTransform = rotation;
            var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(48))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = null
            };
            Storyboard.SetTarget(spin, rotation);
            Storyboard.SetTargetProperty(spin, new PropertyPath(RotateTransform.AngleProperty));
            storyboard.Children.Add(spin);
        }

        Canvas overlay = new() { Width = SceneSize, Height = SceneSize, IsHitTestVisible = false };
        if (asset.Contains("rain", StringComparison.Ordinal) || asset.Contains("drizzle", StringComparison.Ordinal))
        {
            AddRainOverlay(overlay, storyboard);
        }
        else if (asset.Contains("snow", StringComparison.Ordinal))
        {
            AddSnowOverlay(overlay, storyboard);
        }
        else if (asset.Contains("thunderstorms", StringComparison.Ordinal))
        {
            AddLightningOverlay(overlay, storyboard);
        }

        if (overlay.Children.Count > 0)
        {
            _scene.Children.Add(overlay);
        }

        return storyboard;
    }

    private static void AddRainOverlay(Canvas overlay, Storyboard storyboard)
    {
        var layer = new Canvas();
        for (var index = 0; index < 4; index++)
        {
            var drop = new Line
            {
                X1 = 34 + index * 17,
                Y1 = 86 + (index % 2) * 4,
                X2 = 31 + index * 17,
                Y2 = 96 + (index % 2) * 4,
                Stroke = new SolidColorBrush(MediaColor.FromRgb(0x45, 0xC7, 0xFF)),
                StrokeThickness = 2.2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Opacity = 0.8
            };
            layer.Children.Add(drop);
        }

        overlay.Children.Add(layer);
        var transform = new TranslateTransform(0, -6);
        layer.RenderTransform = transform;
        var fall = new DoubleAnimation(-4, 6, TimeSpan.FromSeconds(0.9))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        Storyboard.SetTarget(fall, transform);
        Storyboard.SetTargetProperty(fall, new PropertyPath(TranslateTransform.YProperty));
        storyboard.Children.Add(fall);
    }

    private static void AddSnowOverlay(Canvas overlay, Storyboard storyboard)
    {
        var layer = new Canvas();
        for (var index = 0; index < 5; index++)
        {
            var flake = new Ellipse
            {
                Width = 4,
                Height = 4,
                Fill = new SolidColorBrush(MediaColor.FromRgb(0xF1, 0xF7, 0xFF)),
                Opacity = 0.9
            };
            Canvas.SetLeft(flake, 28 + index * 14);
            Canvas.SetTop(flake, 82 + (index % 2) * 5);
            layer.Children.Add(flake);
        }

        overlay.Children.Add(layer);
        var transform = new TranslateTransform(0, -5);
        layer.RenderTransform = transform;
        var fall = new DoubleAnimation(-2, 8, TimeSpan.FromSeconds(2.1))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(fall, transform);
        Storyboard.SetTargetProperty(fall, new PropertyPath(TranslateTransform.YProperty));
        storyboard.Children.Add(fall);
    }

    private static void AddLightningOverlay(Canvas overlay, Storyboard storyboard)
    {
        var flash = new System.Windows.Shapes.Rectangle
        {
            Width = 76,
            Height = 76,
            RadiusX = 38,
            RadiusY = 38,
            Fill = new RadialGradientBrush(
                MediaColor.FromArgb(0x80, 0xFF, 0xD4, 0x6A),
                MediaColor.FromArgb(0x00, 0xFF, 0xD4, 0x6A)),
            Opacity = 0
        };
        Canvas.SetLeft(flash, 26);
        Canvas.SetTop(flash, 20);
        overlay.Children.Add(flash);
        var flicker = new DoubleAnimationUsingKeyFrames
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        flicker.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0))));
        flicker.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.7))));
        flicker.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.82))));
        Storyboard.SetTarget(flicker, flash);
        Storyboard.SetTargetProperty(flicker, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(flicker);
    }

    private static void RenderSvg(XDocument document, Canvas target)
    {
        XElement root = document.Root!;
        var gradients = root.Descendants().Where(element => element.Name.LocalName is "linearGradient" or "radialGradient")
            .ToDictionary(element => element.Attribute("id")?.Value ?? string.Empty, StringComparer.Ordinal);
        foreach (XElement element in root.Elements())
        {
            RenderElement(element, target, gradients, 1);
        }
    }

    private static void RenderElement(
        XElement element,
        System.Windows.Controls.Panel target,
        IReadOnlyDictionary<string, XElement> gradients,
        double inheritedOpacity)
    {
        string name = element.Name.LocalName;
        if (name is "defs" or "clipPath" or "linearGradient" or "radialGradient" or "stop")
        {
            return;
        }

        double opacity = inheritedOpacity * ReadDouble(element, "opacity", 1) * ReadDouble(element, "fill-opacity", 1);
        if (name == "g")
        {
            var group = new Canvas { Width = SceneSize, Height = SceneSize, Opacity = opacity };
            if (TryParseTransform(element.Attribute("transform")?.Value, out Transform? groupTransform))
            {
                group.RenderTransform = groupTransform;
            }

            foreach (XElement child in element.Elements())
            {
                RenderElement(child, group, gradients, 1);
            }

            target.Children.Add(group);
            return;
        }

        Shape? shape = name switch
        {
            "path" => CreatePath(element),
            "circle" => CreateCircle(element),
            "ellipse" => CreateEllipse(element),
            "rect" => CreateRectangle(element),
            "line" => CreateLine(element),
            "polygon" => CreatePolygon(element, closed: true),
            "polyline" => CreatePolygon(element, closed: false),
            _ => null
        };
        if (shape is null)
        {
            foreach (XElement child in element.Elements())
            {
                RenderElement(child, target, gradients, opacity);
            }

            return;
        }

        string? fillValue = element.Attribute("fill")?.Value;
        if (fillValue is not "none")
        {
            shape.Fill = ParseBrush(fillValue, gradients) ?? new SolidColorBrush(MediaColor.FromRgb(0xE8, 0xF0, 0xFA));
        }

        string? strokeValue = element.Attribute("stroke")?.Value;
        if (strokeValue is not null and not "none")
        {
            shape.Stroke = ParseBrush(strokeValue, gradients);
            shape.StrokeThickness = ReadDouble(element, "stroke-width", 1);
        }

        shape.Opacity = opacity;
        if (TryParseTransform(element.Attribute("transform")?.Value, out Transform? transform))
        {
            shape.RenderTransform = transform;
        }

        target.Children.Add(shape);
    }

    private static Path CreatePath(XElement element) =>
        new Path { Data = Geometry.Parse(element.Attribute("d")?.Value ?? string.Empty) };

    private static Ellipse CreateCircle(XElement element)
    {
        double radius = ReadDouble(element, "r", 0);
        var ellipse = new Ellipse { Width = radius * 2, Height = radius * 2 };
        Canvas.SetLeft(ellipse, ReadDouble(element, "cx", 0) - radius);
        Canvas.SetTop(ellipse, ReadDouble(element, "cy", 0) - radius);
        return ellipse;
    }

    private static Ellipse CreateEllipse(XElement element)
    {
        double radiusX = ReadDouble(element, "rx", 0);
        double radiusY = ReadDouble(element, "ry", 0);
        var ellipse = new Ellipse { Width = radiusX * 2, Height = radiusY * 2 };
        Canvas.SetLeft(ellipse, ReadDouble(element, "cx", 0) - radiusX);
        Canvas.SetTop(ellipse, ReadDouble(element, "cy", 0) - radiusY);
        return ellipse;
    }

    private static System.Windows.Shapes.Rectangle CreateRectangle(XElement element) =>
        new System.Windows.Shapes.Rectangle
        {
            Width = ReadDouble(element, "width", 0),
            Height = ReadDouble(element, "height", 0),
            RadiusX = ReadDouble(element, "rx", 0),
            RadiusY = ReadDouble(element, "ry", 0),
            Margin = new Thickness(ReadDouble(element, "x", 0), ReadDouble(element, "y", 0), 0, 0)
        };

    private static Line CreateLine(XElement element) =>
        new Line
        {
            X1 = ReadDouble(element, "x1", 0),
            Y1 = ReadDouble(element, "y1", 0),
            X2 = ReadDouble(element, "x2", 0),
            Y2 = ReadDouble(element, "y2", 0)
        };

    private static Shape CreatePolygon(XElement element, bool closed)
    {
        string[] points = (element.Attribute("points")?.Value ?? string.Empty)
            .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        var pointCollection = new PointCollection();
        for (var index = 0; index + 1 < points.Length; index += 2)
        {
            pointCollection.Add(new MediaPoint(
                double.Parse(points[index], CultureInfo.InvariantCulture),
                double.Parse(points[index + 1], CultureInfo.InvariantCulture)));
        }

        if (closed)
        {
            return new Polygon { Points = pointCollection };
        }

        return new Polyline { Points = pointCollection };
    }

    private static System.Windows.Media.Brush? ParseBrush(string? value, IReadOnlyDictionary<string, XElement> gradients)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "none")
        {
            return null;
        }

        if (value.StartsWith("url(#", StringComparison.Ordinal) && value.EndsWith(')'))
        {
            string id = value[5..^1];
            if (gradients.TryGetValue(id, out XElement? gradient))
            {
                GradientBrush brush = gradient.Name.LocalName == "radialGradient"
                    ? new RadialGradientBrush()
                    : new LinearGradientBrush();
                foreach (XElement stop in gradient.Elements().Where(child => child.Name.LocalName == "stop"))
                {
                    MediaColor color = ParseColor(stop.Attribute("stop-color")?.Value) ?? MediaColor.FromRgb(0xE8, 0xF0, 0xFA);
                    double offset = ReadOffset(stop.Attribute("offset")?.Value);
                    brush.GradientStops.Add(new GradientStop(color, offset));
                }

                return brush;
            }
        }

        return ParseColor(value) is { } colorValue
            ? new SolidColorBrush(colorValue)
            : null;
    }

    private static MediaColor? ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "none")
        {
            return null;
        }

        try
        {
            return (MediaColor?)System.Windows.Media.ColorConverter.ConvertFromString(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static double ReadOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        return value.EndsWith('%')
            ? double.Parse(value[..^1], CultureInfo.InvariantCulture) / 100
            : double.Parse(value, CultureInfo.InvariantCulture);
    }

    private static double ReadDouble(XElement element, string name, double fallback) =>
        double.TryParse(element.Attribute(name)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : fallback;

    private static bool TryParseTransform(string? value, out Transform? transform)
    {
        transform = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.StartsWith("translate(", StringComparison.Ordinal) && value.EndsWith(')'))
        {
            double[] values = ParseNumbers(value[10..^1]);
            transform = new TranslateTransform(values.ElementAtOrDefault(0), values.ElementAtOrDefault(1));
            return true;
        }

        if (value.StartsWith("rotate(", StringComparison.Ordinal) && value.EndsWith(')'))
        {
            double[] values = ParseNumbers(value[7..^1]);
            transform = new RotateTransform(values.ElementAtOrDefault(0), values.ElementAtOrDefault(1), values.ElementAtOrDefault(2));
            return true;
        }

        return false;
    }

    private static double[] ParseNumbers(string value) =>
        value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(number => double.Parse(number, CultureInfo.InvariantCulture))
            .ToArray();

    private static void RenderFallback(Canvas target)
    {
        var ring = new Ellipse
        {
            Width = 66,
            Height = 66,
            Stroke = new SolidColorBrush(MediaColor.FromRgb(0x5B, 0xE1, 0xFF)),
            StrokeThickness = 5,
            Opacity = 0.8
        };
        Canvas.SetLeft(ring, 31);
        Canvas.SetTop(ring, 31);
        target.Children.Add(ring);
    }

    private static string SelectAsset(int code, bool isDay)
    {
        string period = isDay ? "day" : "night";
        return code switch
        {
            0 => $"clear-{period}.svg",
            1 or 2 => $"partly-cloudy-{period}.svg",
            3 => "cloudy.svg",
            45 or 48 => "fog.svg",
            51 or 53 or 55 or 56 or 57 => $"overcast-{period}-drizzle.svg",
            61 or 63 or 65 or 66 or 67 => $"overcast-{period}-rain.svg",
            71 or 73 or 75 or 77 or 85 or 86 => $"overcast-{period}-snow.svg",
            80 or 81 or 82 => $"partly-cloudy-{period}-rain.svg",
            95 or 96 or 99 => $"thunderstorms-{period}.svg",
            _ => $"clear-{period}.svg"
        };
    }
}
