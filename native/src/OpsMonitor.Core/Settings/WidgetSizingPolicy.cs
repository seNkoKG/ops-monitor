namespace OpsMonitor.Core.Settings;

public readonly record struct WidgetSizeRecommendation(
    double MinimumWidth,
    double MinimumHeight,
    double SuggestedWidth,
    double SuggestedHeight);

/// <summary>
/// One shared sizing contract for the live widget and Studio. Keeping the
/// recommendation in Core prevents the editor preview from drifting away from
/// the actual window whenever a layout is refined.
/// </summary>
public static class WidgetSizingPolicy
{
    public const double MaximumWindowWidth = 1600;

    public static WidgetSizeRecommendation Calculate(
        WidgetDesign design,
        WidgetDensity density,
        int visibleModuleCount,
        int scalePercent)
    {
        var modules = Math.Clamp(visibleModuleCount, 1, 12);
        var scale = Math.Clamp(scalePercent, 80, 160) / 100d;
        var effectiveDensity = design == WidgetDesign.Dock
            ? WidgetDensity.Compact
            : density;
        var footprintScale = scale < 1
            ? design is WidgetDesign.Pill or WidgetDesign.Canvas &&
              effectiveDensity == WidgetDensity.Compact
                ? scale
                : 1
            : scale;

        var baseline = (design, effectiveDensity) switch
        {
            (WidgetDesign.Dock, _) =>
                new SizePair(176 + (modules * 132), 84),
            (WidgetDesign.Pill, WidgetDensity.Compact) =>
                new SizePair(184, 68 + (modules * 60)),
            (WidgetDesign.Pill, WidgetDensity.Normal) =>
                new SizePair(230, 64 + (modules * 104)),
            (WidgetDesign.Pill, WidgetDensity.Comfortable) =>
                new SizePair(260, 64 + (modules * 144)),
            (WidgetDesign.Canvas, WidgetDensity.Compact) =>
                new SizePair(176, 38 + (modules * 30)),
            (WidgetDesign.Canvas, WidgetDensity.Normal) =>
                new SizePair(196, 50 + (modules * 38)),
            (WidgetDesign.Canvas, WidgetDensity.Comfortable) =>
                new SizePair(218, 50 + (modules * 46)),
            (WidgetDesign.Rail, WidgetDensity.Normal) =>
                new SizePair(230, 64 + (modules * 104)),
            (WidgetDesign.Rail, WidgetDensity.Comfortable) =>
                new SizePair(260, 64 + (modules * 144)),
            _ => new SizePair(184, 63 + (modules * 39))
        };

        var scaledWidth = Math.Clamp(
            Math.Round(baseline.Width * footprintScale),
            176,
            MaximumWindowWidth);
        var scaledHeight = Math.Round(baseline.Height * footprintScale);
        if (design == WidgetDesign.Canvas &&
            effectiveDensity == WidgetDensity.Compact &&
            scale < 1)
        {
            // Mini swaps to 28px rows and a reduced header below 100%. Keep
            // the shell at the exact readable floor instead of clipping the
            // fifth row as the percentage footprint becomes smaller.
            scaledHeight = Math.Max(176, scaledHeight);
        }

        return new WidgetSizeRecommendation(
            scaledWidth,
            scaledHeight,
            scaledWidth,
            scaledHeight);
    }

    private readonly record struct SizePair(double Width, double Height);
}
