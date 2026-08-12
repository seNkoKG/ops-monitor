using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace OpsMonitor.Widget.ViewModels;

public sealed record ThemeDefinition(
    string Name,
    string? CoreThemeId,
    Color Surface,
    Color Card,
    Color Border,
    Color TextPrimary,
    Color TextSecondary,
    Color CpuAccent,
    Color GpuAccent,
    Color MemoryAccent,
    Color NetworkAccent,
    Color Warning,
    Color Critical,
    string FontFamily,
    double LabelSize,
    double ValueSize,
    double MinimumReadableSize,
    int LabelWeight,
    int ValueWeight,
    bool UseTabularNumbers)
{
    public Color LatencyAccent { get; init; } = Color.FromRgb(255, 195, 90);
    public Color WeatherAccent { get; init; } = Color.FromRgb(98, 167, 255);
    public Color Success { get; init; } = Color.FromRgb(88, 230, 178);
    public Color Track { get; init; } = Color.FromArgb(85, 54, 66, 88);
    public double CornerRadius { get; init; } = 24;
    public double CardCornerRadius { get; init; } = 12;
    public bool BlurEnabled { get; init; } = true;
    public double BlurStrength { get; init; } = 0.7;
    public bool ShadowEnabled { get; init; } = true;
    public double ShadowOpacity { get; init; } = 0.3;
    public bool GlowEnabled { get; init; } = true;
    public double GlowOpacity { get; init; } = 0.12;
    public double BorderWidth { get; init; } = 1;
    public double CardBorderWidth { get; init; } = 1;
    public double CardGap { get; init; } = 6;
    public double ContentPadding { get; init; } = 10;
    public double CardPadding { get; init; } = 10;
    public double CardOpacity { get; init; } = 0.72;
    public double AccentWidth { get; init; } = 3;
    public double ProgressHeight { get; init; } = 4;
    public double SparklineThickness { get; init; } = 1.5;
    public bool HeaderVisible { get; init; } = true;
    public bool StatusIndicatorVisible { get; init; } = true;
    public bool SettingsButtonVisible { get; init; } = true;
    public double HeaderHeight { get; init; } = 36;
    public double HeaderSize { get; init; } = 11;
    public double SecondarySize { get; init; } = 10;
    public int HeaderWeight { get; init; } = 650;
    public int SecondaryWeight { get; init; } = 450;
    public bool MotionEnabled { get; init; } = true;
    public int TransitionMilliseconds { get; init; } = 160;
    public bool AnimateValueChanges { get; init; } = true;
    public bool RespectReducedMotion { get; init; } = true;
    public bool PulseStatusIndicator { get; init; } = true;
}
