using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Drawing = System.Drawing;
using MediaColor = System.Windows.Media.Color;
using MediaPoint = System.Windows.Point;

namespace OpsMonitor.Widget.Controls;

/// <summary>
/// Renders a weather condition as colorful, animated vector art. The icon is
/// resolution-independent (the scene is drawn in a 100×100 unit space and
/// scaled to whatever size the host gives it) and uses no external assets, so
/// it stays free, offline-capable, and dependency-light. Subtle looping motion
/// is used only when Windows animations are enabled and the host allows motion.
/// </summary>
public sealed class WeatherIcon : ContentControl
{
    private const double Scene = 100;
    private const double CloudY = 46;

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

    /// <summary>
    /// Global gate for looping animation. Set from the owning window based on
    /// the user's theme motion preference and Windows animation settings.
    /// </summary>
    public static bool MotionEnabled { get; set; } = true;

    private readonly Canvas _scene = new() { Width = Scene, Height = Scene };
    private Storyboard? _storyboard;
    private bool _loaded;
    private bool _wasVisible;

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
        Loaded += (_, _) => { _loaded = true; RefreshAnimation(); };
        Unloaded += (_, _) => { _loaded = false; StopAnimation(); };
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

    /// <summary>
    /// Subtle mode reduces motion to a single slow element for small cards,
    /// keeping dozens of forecast cards light on the render thread.
    /// </summary>
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
        _storyboard = BuildScene();
        _wasVisible = false;
        RefreshAnimation();
    }

    private static bool ShouldAnimate =>
        MotionEnabled && SystemParameters.ClientAreaAnimation;

    private void RefreshAnimation()
    {
        bool visible = _loaded && IsVisible;
        if (visible == _wasVisible)
        {
            return;
        }

        _wasVisible = visible;
        if (visible && ShouldAnimate)
        {
            StartAnimation();
        }
        else
        {
            StopAnimation();
        }
    }

    private void StartAnimation()
    {
        if (_storyboard is null || !ShouldAnimate || !IsVisible)
        {
            return;
        }

        _storyboard.Begin(this, true);
    }

    private void StopAnimation()
    {
        if (_storyboard is not null)
        {
            _storyboard.Stop(this);
        }
    }

    private Storyboard? BuildScene()
    {
        var storyboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        var condition = Classify(WeatherCode);
        var isDay = IsDay;

        switch (condition)
        {
            case WeatherKind.Clear:
                if (isDay)
                {
                    BuildSun(_scene, storyboard, animateRays: !Subtle, animateGlow: true);
                }
                else
                {
                    BuildMoon(_scene, storyboard, animateStars: true);
                }

                break;

            case WeatherKind.PartlyCloudy:
                if (isDay)
                {
                    BuildSun(_scene, storyboard, animateRays: !Subtle, animateGlow: true);
                    BuildCloud(_scene, storyboard, CloudY, scale: 0.62, driftSeconds: 9, animate: true);
                }
                else
                {
                    BuildMoon(_scene, storyboard, animateStars: !Subtle);
                    BuildCloud(_scene, storyboard, CloudY, scale: 0.62, driftSeconds: 9, animate: true);
                }

                break;

            case WeatherKind.Overcast:
                BuildCloud(_scene, storyboard, CloudY, scale: 0.72, driftSeconds: 11, animate: true);
                BuildCloud(_scene, storyboard, CloudY + 10, scale: 0.55, driftSeconds: 15, animate: true, offset: 18);
                break;

            case WeatherKind.Fog:
                BuildFog(_scene, storyboard);
                break;

            case WeatherKind.Drizzle:
                BuildCloud(_scene, storyboard, CloudY, scale: 0.72, driftSeconds: 11, animate: true);
                BuildPrecipitation(_scene, storyboard, rain: true, heavy: false, snow: false, dropScale: 0.7);
                break;

            case WeatherKind.Rain:
            case WeatherKind.Showers:
                BuildCloud(_scene, storyboard, CloudY, scale: 0.78, driftSeconds: 11, animate: true, darker: true);
                BuildPrecipitation(_scene, storyboard, rain: true, heavy: condition == WeatherKind.Showers, snow: false, dropScale: 1);
                break;

            case WeatherKind.Snow:
                BuildCloud(_scene, storyboard, CloudY, scale: 0.78, driftSeconds: 11, animate: true, darker: true);
                BuildPrecipitation(_scene, storyboard, rain: false, heavy: false, snow: true, dropScale: 1);
                break;

            case WeatherKind.Thunder:
                BuildCloud(_scene, storyboard, CloudY, scale: 0.82, driftSeconds: 13, animate: true, darker: true);
                BuildLightning(_scene, storyboard);
                BuildPrecipitation(_scene, storyboard, rain: true, heavy: true, snow: false, dropScale: 0.9);
                break;

            default:
                BuildSun(_scene, storyboard, animateRays: !Subtle, animateGlow: true);
                break;
        }

        return storyboard;
    }

    private static WeatherKind Classify(int code) => code switch
    {
        0 => WeatherKind.Clear,
        1 or 2 => WeatherKind.PartlyCloudy,
        3 => WeatherKind.Overcast,
        45 or 48 => WeatherKind.Fog,
        51 or 53 or 55 or 56 or 57 => WeatherKind.Drizzle,
        61 or 63 or 65 or 66 or 67 => WeatherKind.Rain,
        71 or 73 or 75 or 77 or 85 or 86 => WeatherKind.Snow,
        80 or 81 or 82 => WeatherKind.Showers,
        95 or 96 or 99 => WeatherKind.Thunder,
        _ => WeatherKind.Clear
    };

    private static void BuildSun(
        Canvas scene,
        Storyboard storyboard,
        bool animateRays,
        bool animateGlow)
    {
        const double center = 42;
        var glow = new Ellipse
        {
            Width = 66,
            Height = 66,
            Fill = new RadialGradientBrush(MediaColor.FromRgb(0xFF, 0xFF, 0xFF), MediaColor.FromArgb(0x00, 0xFF, 0xD7, 0x7A))
            {
                GradientOrigin = new MediaPoint(0.5, 0.5),
                Center = new MediaPoint(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5
            }
        };
        Canvas.SetLeft(glow, center - 33);
        Canvas.SetTop(glow, center - 33);
        scene.Children.Add(glow);

        var rays = new Canvas { Opacity = 0.95 };
        for (var i = 0; i < 8; i++)
        {
            var ray = new System.Windows.Shapes.Rectangle
            {
                Width = 5,
                Height = 15,
                RadiusX = 2.5,
                RadiusY = 2.5,
                Fill = new LinearGradientBrush(
                    MediaColor.FromArgb(0xF2, 0xFF, 0xC9, 0x5C),
                    MediaColor.FromArgb(0xB0, 0xF0, 0x9E, 0x2E),
                    90)
            };
            Canvas.SetLeft(ray, center - 2.5);
            Canvas.SetTop(ray, center - 22 - 15);
            ray.RenderTransform = new RotateTransform(i * 45, center, center);
            rays.Children.Add(ray);
        }

        var rotationGroup = new RotateTransform(0, center, center);
        rays.RenderTransform = rotationGroup;
        Canvas.SetLeft(rays, 0);
        Canvas.SetTop(rays, 0);
        scene.Children.Add(rays);
        if (animateRays)
        {
            var rotation = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(46))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTarget(rotation, rotationGroup);
            Storyboard.SetTargetProperty(rotation, new PropertyPath(RotateTransform.AngleProperty));
            storyboard.Children.Add(rotation);
        }

        var core = new Ellipse
        {
            Width = 40,
            Height = 40,
            Fill = new RadialGradientBrush(MediaColor.FromRgb(0xFF, 0xE9, 0x9B), MediaColor.FromRgb(0xF7, 0xA6, 0x2B))
            {
                GradientOrigin = new MediaPoint(0.35, 0.35)
            }
        };
        Canvas.SetLeft(core, center - 20);
        Canvas.SetTop(core, center - 20);
        scene.Children.Add(core);

        if (animateGlow)
        {
            var pulse = new DoubleAnimation(0.55, 0.95, TimeSpan.FromSeconds(3.4))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTarget(pulse, glow);
            Storyboard.SetTargetProperty(pulse, new PropertyPath(OpacityProperty));
            storyboard.Children.Add(pulse);
        }
    }

    private static void BuildMoon(Canvas scene, Storyboard storyboard, bool animateStars)
    {
        const double center = 46;
        var glow = new Ellipse
        {
            Width = 58,
            Height = 58,
            Fill = new RadialGradientBrush(MediaColor.FromArgb(0x2E, 0xFF, 0xFA, 0xE0), MediaColor.FromArgb(0x00, 0xFF, 0xFA, 0xE0))
            {
                GradientOrigin = new MediaPoint(0.5, 0.5),
                Center = new MediaPoint(0.5, 0.5)
            }
        };
        Canvas.SetLeft(glow, center - 29);
        Canvas.SetTop(glow, center - 29);
        scene.Children.Add(glow);

        var moon = new Path
        {
            Fill = new RadialGradientBrush(MediaColor.FromRgb(0xFF, 0xF6, 0xDE), MediaColor.FromRgb(0xE2, 0xCE, 0x9E))
            {
                GradientOrigin = new MediaPoint(0.4, 0.4)
            },
            Data = new CombinedGeometry(
                GeometryCombineMode.Exclude,
                new EllipseGeometry(new MediaPoint(center + 8, center), 22, 22),
                new EllipseGeometry(new MediaPoint(center - 2, center - 4), 19, 19))
        };
        Canvas.SetLeft(moon, 0);
        Canvas.SetTop(moon, 0);
        scene.Children.Add(moon);

        AddStar(scene, storyboard, x: 18, y: 22, size: 3, animateStars);
        AddStar(scene, storyboard, x: 72, y: 16, size: 2.2, animateStars, phaseSeconds: 1.1);
        AddStar(scene, storyboard, x: 78, y: 64, size: 2.6, animateStars, phaseSeconds: 0.6);
        AddStar(scene, storyboard, x: 20, y: 70, size: 2, animateStars, phaseSeconds: 1.8);
    }

    private static void AddStar(
        Canvas scene,
        Storyboard storyboard,
        double x,
        double y,
        double size,
        bool animate,
        double phaseSeconds = 0)
    {
        var star = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = new SolidColorBrush(MediaColor.FromRgb(0xFF, 0xFF, 0xFF)),
            Opacity = 0.85
        };
        Canvas.SetLeft(star, x);
        Canvas.SetTop(star, y);
        scene.Children.Add(star);

        if (animate)
        {
            var twinkle = new DoubleAnimation(0.35, 1, TimeSpan.FromSeconds(2.6))
            {
                AutoReverse = true,
                BeginTime = TimeSpan.FromSeconds(phaseSeconds),
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTarget(twinkle, star);
            Storyboard.SetTargetProperty(twinkle, new PropertyPath(OpacityProperty));
            storyboard.Children.Add(twinkle);
        }
    }

    private static void BuildCloud(
        Canvas scene,
        Storyboard storyboard,
        double y,
        double scale,
        double driftSeconds,
        bool animate,
        bool darker = false,
        double offset = 0)
    {
        var cloud = new Canvas();
        var fill = darker
            ? new LinearGradientBrush(MediaColor.FromRgb(0x8F, 0x9C, 0xB3), MediaColor.FromRgb(0x56, 0x63, 0x7A), 90)
            : new LinearGradientBrush(MediaColor.FromRgb(0xFF, 0xFF, 0xFF), MediaColor.FromRgb(0xB8, 0xC6, 0xDC), 90);

        var bumps = new[]
        {
            (X: 28, Y: 30, R: 16),
            (X: 47, Y: 24, R: 21),
            (X: 66, Y: 30, R: 15)
        };
        foreach ((double x, double bumpY, double radius) in bumps)
        {
            var bump = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = fill
            };
            Canvas.SetLeft(bump, x - radius);
            Canvas.SetTop(bump, bumpY - radius);
            cloud.Children.Add(bump);
        }

        var baseRect = new System.Windows.Shapes.Rectangle
        {
            Width = 62,
            Height = 18,
            RadiusX = 9,
            RadiusY = 9,
            Fill = fill
        };
        Canvas.SetLeft(baseRect, 19);
        Canvas.SetTop(baseRect, 34);
        cloud.Children.Add(baseRect);

        var translate = new TranslateTransform(offset, 0);
        var scaleTransform = new ScaleTransform(scale, scale, Scene / 2, y + 20);
        var cloudTransform = new TransformGroup();
        cloudTransform.Children.Add(scaleTransform);
        cloudTransform.Children.Add(translate);
        cloud.RenderTransform = cloudTransform;
        Canvas.SetLeft(cloud, 0);
        Canvas.SetTop(cloud, y);
        scene.Children.Add(cloud);

        if (animate)
        {
            var drift = new DoubleAnimation(offset - 3, offset + 3, TimeSpan.FromSeconds(driftSeconds))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTarget(drift, translate);
            Storyboard.SetTargetProperty(drift, new PropertyPath(TranslateTransform.XProperty));
            storyboard.Children.Add(drift);
        }
    }

    private static void BuildFog(Canvas scene, Storyboard storyboard)
    {
        double[] rows = [62, 74, 86];
        for (var index = 0; index < rows.Length; index++)
        {
            double y = rows[index];
            var band = new System.Windows.Shapes.Rectangle
            {
                Width = index == 1 ? 74 : 62,
                Height = 7,
                RadiusX = 3.5,
                RadiusY = 3.5,
                Fill = new LinearGradientBrush(
                    MediaColor.FromArgb(0xE6, 0xC6, 0xD3, 0xE4),
                    MediaColor.FromArgb(0x99, 0x8E, 0xA0, 0xB8),
                    0),
                Opacity = 0.9 - (index * 0.12)
            };
            Canvas.SetLeft(band, index == 1 ? 13 : 19);
            Canvas.SetTop(band, y);
            var translate = new TranslateTransform(0, 0);
            band.RenderTransform = translate;
            scene.Children.Add(band);

            var drift = new DoubleAnimation(-6, 6, TimeSpan.FromSeconds(12 + index * 4))
            {
                AutoReverse = true,
                BeginTime = TimeSpan.FromSeconds(index * 1.4),
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTarget(drift, translate);
            Storyboard.SetTargetProperty(drift, new PropertyPath(TranslateTransform.XProperty));
            storyboard.Children.Add(drift);
        }
    }

    private static void BuildPrecipitation(
        Canvas scene,
        Storyboard storyboard,
        bool rain,
        bool heavy,
        bool snow,
        double dropScale)
    {
        int count = heavy ? 5 : 3;
        var layer = new Canvas();
        Canvas.SetLeft(layer, 0);
        Canvas.SetTop(layer, 0);
        scene.Children.Add(layer);

        var fall = new TranslateTransform(0, 0);
        layer.RenderTransform = fall;

        for (var i = 0; i < count; i++)
        {
            double x = 26 + (i * 13);
            if (rain)
            {
                var drop = new System.Windows.Shapes.Rectangle
                {
                    Width = 3.4 * dropScale,
                    Height = 11 * dropScale,
                    RadiusX = 1.7 * dropScale,
                    RadiusY = 1.7 * dropScale,
                    Fill = new LinearGradientBrush(MediaColor.FromRgb(0x6F, 0xD3, 0xFF), MediaColor.FromRgb(0x2E, 0x8F, 0xFF), 90)
                };
                Canvas.SetLeft(drop, x);
                Canvas.SetTop(drop, 64 + (i % 3) * 3);
                layer.Children.Add(drop);
            }
            else
            {
                var flake = new Ellipse
                {
                    Width = 5 * dropScale,
                    Height = 5 * dropScale,
                    Fill = new SolidColorBrush(MediaColor.FromRgb(0xFF, 0xFF, 0xFF)),
                    Opacity = 0.95
                };
                Canvas.SetLeft(flake, x);
                Canvas.SetTop(flake, 64 + (i % 3) * 3);
                layer.Children.Add(flake);
            }
        }

        var fallSeconds = rain ? (heavy ? 0.8 : 1.1) : 2.4;
        var fallAnimation = new DoubleAnimation(0, 22, TimeSpan.FromSeconds(fallSeconds))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        Storyboard.SetTarget(fallAnimation, fall);
        Storyboard.SetTargetProperty(fallAnimation, new PropertyPath(TranslateTransform.YProperty));
        storyboard.Children.Add(fallAnimation);

        if (snow)
        {
            var sway = new DoubleAnimation(-2.5, 2.5, TimeSpan.FromSeconds(1.6))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTarget(sway, fall);
            Storyboard.SetTargetProperty(sway, new PropertyPath(TranslateTransform.XProperty));
            storyboard.Children.Add(sway);
        }
    }

    private static void BuildLightning(Canvas scene, Storyboard storyboard)
    {
        var bolt = new Path
        {
            Fill = new LinearGradientBrush(MediaColor.FromRgb(0xFF, 0xE9, 0x7A), MediaColor.FromRgb(0xF5, 0xA8, 0x2A), 90),
            Data = Geometry.Parse(
                "M 47 52 L 37 68 L 45 68 L 41 82 L 57 63 L 48 63 L 54 52 Z")
        };
        Canvas.SetLeft(bolt, 0);
        Canvas.SetTop(bolt, 0);
        scene.Children.Add(bolt);

        var flash = new DoubleAnimation(0.2, 1, TimeSpan.FromSeconds(1.6))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        Storyboard.SetTarget(flash, bolt);
        Storyboard.SetTargetProperty(flash, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(flash);
    }

    private enum WeatherKind
    {
        Clear,
        PartlyCloudy,
        Overcast,
        Fog,
        Drizzle,
        Rain,
        Showers,
        Snow,
        Thunder
    }
}
