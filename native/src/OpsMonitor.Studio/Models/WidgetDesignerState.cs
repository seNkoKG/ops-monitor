using System.Windows.Media;
using OpsMonitor.Studio.Infrastructure;

namespace OpsMonitor.Studio.Models;

/// <summary>
/// The single editable design-token surface shared by Studio persistence,
/// import/export and the runtime widget. Values are constrained here so a
/// malformed theme can never create an unreadable or unrenderable widget.
/// </summary>
public sealed class WidgetDesignerState : ObservableObject
{
    private string _surface = "#FF080B12";
    private string _card = "#FF0F1521";
    private string _border = "#FF364258";
    private string _primaryText = "#FFF6F9FF";
    private string _secondaryText = "#FFB8C4D6";
    private string _cpuAccent = "#FF48DCF9";
    private string _gpuAccent = "#FFFF4FD8";
    private string _memoryAccent = "#FF58E6B2";
    private string _networkAccent = "#FF62A7FF";
    private string _latencyAccent = "#FFFFC35A";
    private string _weatherAccent = "#FF62A7FF";
    private string _track = "#55364258";
    private string _warning = "#FFFFC35A";
    private string _critical = "#FFFF566E";
    private string _success = "#FF58E6B2";
    private double _cornerRadius = 24;
    private double _cardCornerRadius = 12;
    private bool _blurEnabled = true;
    private double _blurStrength = 0.7;
    private bool _shadowEnabled = true;
    private double _shadowOpacity = 0.3;
    private bool _glowEnabled = true;
    private double _glowOpacity = 0.12;
    private double _borderWidth = 1;
    private double _cardBorderWidth = 1;
    private double _cardGap = 6;
    private double _contentPadding = 10;
    private double _cardPadding = 10;
    private double _cardOpacity = 0.72;
    private double _accentWidth = 3;
    private double _progressHeight = 4;
    private double _progressCornerRadius = 2;
    private double _sparklineThickness = 1.5;
    private double _sparklineFillOpacity = 0.16;
    private bool _headerVisible = true;
    private bool _statusIndicatorVisible = true;
    private bool _settingsButtonVisible = true;
    private double _headerHeight = 36;
    private string _fontFamily = "Segoe UI Variable";
    private double _headerSize = 11;
    private double _labelSize = 11;
    private double _secondarySize = 10;
    private double _valueSize = 18;
    private double _iconSize = 14;
    private double _minimumReadableSize = 10;
    private int _headerWeight = 650;
    private int _labelWeight = 600;
    private int _secondaryWeight = 450;
    private int _valueWeight = 600;
    private bool _useTabularNumbers = true;
    private bool _motionEnabled = true;
    private int _transitionMilliseconds = 160;
    private bool _animateValueChanges = true;
    private bool _respectReducedMotion = true;
    private bool _pulseStatusIndicator = true;
    private bool _isApplying;

    public event EventHandler? EditorValueChanging;
    public event EventHandler? DesignChanged;

    public string Surface { get => _surface; set => SetColor(ref _surface, value); }
    public string Card { get => _card; set => SetColor(ref _card, value); }
    public string Border { get => _border; set => SetColor(ref _border, value); }
    public string PrimaryText { get => _primaryText; set => SetColor(ref _primaryText, value); }
    public string SecondaryText { get => _secondaryText; set => SetColor(ref _secondaryText, value); }
    public string CpuAccent { get => _cpuAccent; set => SetColor(ref _cpuAccent, value); }
    public string GpuAccent { get => _gpuAccent; set => SetColor(ref _gpuAccent, value); }
    public string MemoryAccent { get => _memoryAccent; set => SetColor(ref _memoryAccent, value); }
    public string NetworkAccent { get => _networkAccent; set => SetColor(ref _networkAccent, value); }
    public string LatencyAccent { get => _latencyAccent; set => SetColor(ref _latencyAccent, value); }
    public string WeatherAccent { get => _weatherAccent; set => SetColor(ref _weatherAccent, value); }
    public string Track { get => _track; set => SetColor(ref _track, value); }
    public string Warning { get => _warning; set => SetColor(ref _warning, value); }
    public string Critical { get => _critical; set => SetColor(ref _critical, value); }
    public string Success { get => _success; set => SetColor(ref _success, value); }

    public double CornerRadius { get => _cornerRadius; set => SetNumber(ref _cornerRadius, value, 0, 48); }
    public double CardCornerRadius { get => _cardCornerRadius; set => SetNumber(ref _cardCornerRadius, value, 0, 40); }
    public bool BlurEnabled { get => _blurEnabled; set => SetValue(ref _blurEnabled, value); }
    public double BlurStrength { get => _blurStrength; set => SetNumber(ref _blurStrength, value, 0, 1); }
    public bool ShadowEnabled { get => _shadowEnabled; set => SetValue(ref _shadowEnabled, value); }
    public double ShadowOpacity { get => _shadowOpacity; set => SetNumber(ref _shadowOpacity, value, 0, 0.8); }
    public bool GlowEnabled { get => _glowEnabled; set => SetValue(ref _glowEnabled, value); }
    public double GlowOpacity { get => _glowOpacity; set => SetNumber(ref _glowOpacity, value, 0, 0.5); }
    public double BorderWidth { get => _borderWidth; set => SetNumber(ref _borderWidth, value, 0, 4); }
    public double CardBorderWidth { get => _cardBorderWidth; set => SetNumber(ref _cardBorderWidth, value, 0, 4); }
    public double CardGap { get => _cardGap; set => SetNumber(ref _cardGap, value, 0, 20); }
    public double ContentPadding { get => _contentPadding; set => SetNumber(ref _contentPadding, value, 0, 28); }
    public double CardPadding { get => _cardPadding; set => SetNumber(ref _cardPadding, value, 0, 28); }
    public double CardOpacity { get => _cardOpacity; set => SetNumber(ref _cardOpacity, value, 0, 1); }
    public double AccentWidth { get => _accentWidth; set => SetNumber(ref _accentWidth, value, 0, 10); }
    public double ProgressHeight { get => _progressHeight; set => SetNumber(ref _progressHeight, value, 1, 12); }
    public double ProgressCornerRadius { get => _progressCornerRadius; set => SetNumber(ref _progressCornerRadius, value, 0, 6); }
    public double SparklineThickness { get => _sparklineThickness; set => SetNumber(ref _sparklineThickness, value, 0.5, 5); }
    public double SparklineFillOpacity { get => _sparklineFillOpacity; set => SetNumber(ref _sparklineFillOpacity, value, 0, 0.5); }
    public bool HeaderVisible { get => _headerVisible; set => SetValue(ref _headerVisible, value); }
    public bool StatusIndicatorVisible { get => _statusIndicatorVisible; set => SetValue(ref _statusIndicatorVisible, value); }
    public bool SettingsButtonVisible { get => _settingsButtonVisible; set => SetValue(ref _settingsButtonVisible, value); }
    public double HeaderHeight { get => _headerHeight; set => SetNumber(ref _headerHeight, value, 18, 64); }

    public string FontFamily
    {
        get => _fontFamily;
        set => SetValue(ref _fontFamily, string.IsNullOrWhiteSpace(value) ? "Segoe UI Variable" : value.Trim());
    }

    public double HeaderSize { get => _headerSize; set => SetNumber(ref _headerSize, value, 8, 24); }
    public double LabelSize { get => _labelSize; set => SetNumber(ref _labelSize, value, 8, 26); }
    public double SecondarySize { get => _secondarySize; set => SetNumber(ref _secondarySize, value, 8, 24); }
    public double ValueSize { get => _valueSize; set => SetNumber(ref _valueSize, value, 10, 42); }
    public double IconSize { get => _iconSize; set => SetNumber(ref _iconSize, value, 8, 32); }
    public double MinimumReadableSize { get => _minimumReadableSize; set => SetNumber(ref _minimumReadableSize, value, 8, 18); }
    public int HeaderWeight { get => _headerWeight; set => SetNumber(ref _headerWeight, value, 100, 900); }
    public int LabelWeight { get => _labelWeight; set => SetNumber(ref _labelWeight, value, 100, 900); }
    public int SecondaryWeight { get => _secondaryWeight; set => SetNumber(ref _secondaryWeight, value, 100, 900); }
    public int ValueWeight { get => _valueWeight; set => SetNumber(ref _valueWeight, value, 100, 900); }
    public bool UseTabularNumbers { get => _useTabularNumbers; set => SetValue(ref _useTabularNumbers, value); }
    public bool MotionEnabled { get => _motionEnabled; set => SetValue(ref _motionEnabled, value); }
    public int TransitionMilliseconds { get => _transitionMilliseconds; set => SetNumber(ref _transitionMilliseconds, value, 0, 600); }
    public bool AnimateValueChanges { get => _animateValueChanges; set => SetValue(ref _animateValueChanges, value); }
    public bool RespectReducedMotion { get => _respectReducedMotion; set => SetValue(ref _respectReducedMotion, value); }
    public bool PulseStatusIndicator { get => _pulseStatusIndicator; set => SetValue(ref _pulseStatusIndicator, value); }

    public Brush SurfaceBrush => FrozenBrush(Surface);
    public Brush CardBrush => FrozenBrush(Card);
    public Brush BorderBrush => FrozenBrush(Border);
    public Brush PrimaryTextBrush => FrozenBrush(PrimaryText);
    public Brush SecondaryTextBrush => FrozenBrush(SecondaryText);
    public Brush CpuAccentBrush => FrozenBrush(CpuAccent);
    public Brush GpuAccentBrush => FrozenBrush(GpuAccent);
    public Brush MemoryAccentBrush => FrozenBrush(MemoryAccent);
    public Brush NetworkAccentBrush => FrozenBrush(NetworkAccent);
    public Brush LatencyAccentBrush => FrozenBrush(LatencyAccent);
    public Brush WeatherAccentBrush => FrozenBrush(WeatherAccent);
    public Brush TrackBrush => FrozenBrush(Track);
    public Brush WarningBrush => FrozenBrush(Warning);
    public Brush CriticalBrush => FrozenBrush(Critical);
    public Brush SuccessBrush => FrozenBrush(Success);

    public double PrimaryContrastRatio => Contrast(ColorText.Parse(PrimaryText, Colors.White), EffectiveCardColor());
    public double SecondaryContrastRatio => Contrast(ColorText.Parse(SecondaryText, Colors.White), EffectiveCardColor());
    public bool HasReadableContrast => PrimaryContrastRatio >= 4.5 && SecondaryContrastRatio >= 4.5;
    public string ContrastSummary => HasReadableContrast
        ? $"Readable · {PrimaryContrastRatio:0.0}:1 primary · {SecondaryContrastRatio:0.0}:1 secondary"
        : $"Contrast warning · {PrimaryContrastRatio:0.0}:1 primary · {SecondaryContrastRatio:0.0}:1 secondary";

    public StudioThemeSnapshot Capture(string id, string name) =>
        new(id, name, Surface, Card, Border, CpuAccent)
        {
            PrimaryText = PrimaryText,
            SecondaryText = SecondaryText,
            CpuAccent = CpuAccent,
            GpuAccent = GpuAccent,
            MemoryAccent = MemoryAccent,
            NetworkAccent = NetworkAccent,
            LatencyAccent = LatencyAccent,
            WeatherAccent = WeatherAccent,
            Track = Track,
            Warning = Warning,
            Critical = Critical,
            Success = Success,
            CornerRadius = CornerRadius,
            CardCornerRadius = CardCornerRadius,
            BlurEnabled = BlurEnabled,
            BlurStrength = BlurStrength,
            ShadowEnabled = ShadowEnabled,
            ShadowOpacity = ShadowOpacity,
            GlowEnabled = GlowEnabled,
            GlowOpacity = GlowOpacity,
            BorderWidth = BorderWidth,
            CardBorderWidth = CardBorderWidth,
            CardGap = CardGap,
            ContentPadding = ContentPadding,
            CardPadding = CardPadding,
            CardOpacity = CardOpacity,
            AccentWidth = AccentWidth,
            ProgressHeight = ProgressHeight,
            ProgressCornerRadius = ProgressCornerRadius,
            SparklineThickness = SparklineThickness,
            SparklineFillOpacity = SparklineFillOpacity,
            HeaderVisible = HeaderVisible,
            StatusIndicatorVisible = StatusIndicatorVisible,
            SettingsButtonVisible = SettingsButtonVisible,
            HeaderHeight = HeaderHeight,
            FontFamily = FontFamily,
            HeaderSize = HeaderSize,
            LabelSize = LabelSize,
            SecondarySize = SecondarySize,
            ValueSize = ValueSize,
            IconSize = IconSize,
            MinimumReadableSize = MinimumReadableSize,
            HeaderWeight = HeaderWeight,
            LabelWeight = LabelWeight,
            SecondaryWeight = SecondaryWeight,
            ValueWeight = ValueWeight,
            UseTabularNumbers = UseTabularNumbers,
            MotionEnabled = MotionEnabled,
            TransitionMilliseconds = TransitionMilliseconds,
            AnimateValueChanges = AnimateValueChanges,
            RespectReducedMotion = RespectReducedMotion,
            PulseStatusIndicator = PulseStatusIndicator
        };

    public void Apply(StudioThemeSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _isApplying = true;
        try
        {
            Surface = source.Surface;
            Card = source.Card;
            Border = source.Border;
            PrimaryText = source.PrimaryText;
            SecondaryText = source.SecondaryText;
            CpuAccent = source.CpuAccent;
            GpuAccent = source.GpuAccent;
            MemoryAccent = source.MemoryAccent;
            NetworkAccent = source.NetworkAccent;
            LatencyAccent = source.LatencyAccent;
            WeatherAccent = source.WeatherAccent;
            Track = source.Track;
            Warning = source.Warning;
            Critical = source.Critical;
            Success = source.Success;
            CornerRadius = source.CornerRadius;
            CardCornerRadius = source.CardCornerRadius;
            BlurEnabled = source.BlurEnabled;
            BlurStrength = source.BlurStrength;
            ShadowEnabled = source.ShadowEnabled;
            ShadowOpacity = source.ShadowOpacity;
            GlowEnabled = source.GlowEnabled;
            GlowOpacity = source.GlowOpacity;
            BorderWidth = source.BorderWidth;
            CardBorderWidth = source.CardBorderWidth;
            CardGap = source.CardGap;
            ContentPadding = source.ContentPadding;
            CardPadding = source.CardPadding;
            CardOpacity = source.CardOpacity;
            AccentWidth = source.AccentWidth;
            ProgressHeight = source.ProgressHeight;
            ProgressCornerRadius = source.ProgressCornerRadius;
            SparklineThickness = source.SparklineThickness;
            SparklineFillOpacity = source.SparklineFillOpacity;
            HeaderVisible = source.HeaderVisible;
            StatusIndicatorVisible = source.StatusIndicatorVisible;
            SettingsButtonVisible = source.SettingsButtonVisible;
            HeaderHeight = source.HeaderHeight;
            FontFamily = source.FontFamily;
            HeaderSize = source.HeaderSize;
            LabelSize = source.LabelSize;
            SecondarySize = source.SecondarySize;
            ValueSize = source.ValueSize;
            IconSize = source.IconSize;
            MinimumReadableSize = source.MinimumReadableSize;
            HeaderWeight = source.HeaderWeight;
            LabelWeight = source.LabelWeight;
            SecondaryWeight = source.SecondaryWeight;
            ValueWeight = source.ValueWeight;
            UseTabularNumbers = source.UseTabularNumbers;
            MotionEnabled = source.MotionEnabled;
            TransitionMilliseconds = source.TransitionMilliseconds;
            AnimateValueChanges = source.AnimateValueChanges;
            RespectReducedMotion = source.RespectReducedMotion;
            PulseStatusIndicator = source.PulseStatusIndicator;
        }
        finally
        {
            _isApplying = false;
        }

        RaiseDerivedProperties();
        DesignChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyPreset(ThemePreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var snapshot = Capture(preset.Id, preset.Name) with
        {
            Surface = ColorText.ToHex(preset.Surface),
            Card = ColorText.ToHex(preset.Card),
            Border = ColorText.ToHex(preset.Border),
            Accent = ColorText.ToHex(preset.Accent),
            CpuAccent = ColorText.ToHex(preset.Accent),
            NetworkAccent = ColorText.ToHex(preset.Accent)
        };
        snapshot = preset.Id switch
        {
            "ghost" => snapshot with
            {
                PrimaryText = "#FF0C1722",
                SecondaryText = "#FF425466",
                GpuAccent = "#FF8E4EC6",
                MemoryAccent = "#FF008F74",
                LatencyAccent = "#FF9A6300",
                WeatherAccent = "#FF007E9A",
                Track = "#334E647A",
                CornerRadius = 28,
                CardCornerRadius = 14,
                CardOpacity = 0.82,
                GlowOpacity = 0.05,
                ShadowOpacity = 0.2
            },
            "terminal" => snapshot with
            {
                PrimaryText = "#FFE8FFF0",
                SecondaryText = "#FF8AC9A0",
                GpuAccent = "#FFFF7BDD",
                MemoryAccent = "#FF5CFF9D",
                NetworkAccent = "#FF70EFFF",
                LatencyAccent = "#FFFFD166",
                WeatherAccent = "#FF70EFFF",
                Track = "#551D5E37",
                CornerRadius = 8,
                CardCornerRadius = 4,
                CardOpacity = 0.66,
                BorderWidth = 1,
                CardBorderWidth = 1,
                CardGap = 3,
                CardPadding = 7,
                GlowOpacity = 0.18,
                FontFamily = "Cascadia Mono",
                HeaderWeight = 600,
                LabelWeight = 600,
                ValueWeight = 600
            },
            "frameless" => snapshot with
            {
                PrimaryText = "#FFF5F9FF",
                SecondaryText = "#FF9AABC0",
                GpuAccent = "#FFFF60D7",
                MemoryAccent = "#FF5BE6B2",
                NetworkAccent = "#FF62D7FF",
                LatencyAccent = "#FFFFCA63",
                WeatherAccent = "#FF62D7FF",
                Track = "#242E3B4E",
                BorderWidth = 0,
                CardBorderWidth = 0,
                CardOpacity = 0.2,
                ShadowEnabled = false,
                GlowEnabled = false,
                CornerRadius = 18,
                CardCornerRadius = 8,
                CardGap = 2,
                CardPadding = 5
            },
            "contrast" => snapshot with
            {
                PrimaryText = "#FFFFFFFF",
                SecondaryText = "#FFD6E0EE",
                CardOpacity = 0.95,
                BorderWidth = 1.5,
                CardBorderWidth = 1.5,
                CornerRadius = 12,
                CardCornerRadius = 7,
                MinimumReadableSize = 11,
                GlowEnabled = false
            },
            "slate" => snapshot with
            {
                CardOpacity = 0.82,
                CornerRadius = 18,
                CardCornerRadius = 9,
                GlowOpacity = 0.06,
                ShadowOpacity = 0.2
            },
            "aurora" => snapshot with
            {
                GpuAccent = "#FFFF5BD7",
                MemoryAccent = "#FF5BF1BE",
                NetworkAccent = "#FF56E2FF",
                CornerRadius = 30,
                CardCornerRadius = 16,
                GlowOpacity = 0.24,
                CardOpacity = 0.68
            },
            "ember" => snapshot with
            {
                GpuAccent = "#FFFF5DB3",
                MemoryAccent = "#FF5EE1A8",
                NetworkAccent = "#FF4DD7EF",
                LatencyAccent = "#FFFFAC48",
                CornerRadius = 20,
                CardCornerRadius = 10,
                CardOpacity = 0.78
            },
            _ => snapshot with
            {
                PrimaryText = "#FFF6F9FF",
                SecondaryText = "#FFB8C4D6",
                GpuAccent = "#FFFF4FD8",
                MemoryAccent = "#FF58E6B2",
                NetworkAccent = "#FF48DCF9",
                LatencyAccent = "#FFFFC35A",
                WeatherAccent = "#FF62A7FF",
                Track = "#55364258",
                CornerRadius = 24,
                CardCornerRadius = 12,
                CardOpacity = 0.72
            }
        };
        Apply(snapshot);
    }

    public void FixContrast()
    {
        var card = EffectiveCardColor();
        var whiteRatio = Contrast(Colors.White, card);
        var blackRatio = Contrast(Colors.Black, card);
        var preferred = whiteRatio >= blackRatio ? Colors.White : Colors.Black;
        PrimaryText = ColorText.ToHex(preferred);
        var secondary = preferred == Colors.White
            ? Color.FromRgb(205, 216, 232)
            : Color.FromRgb(40, 46, 54);
        SecondaryText = ColorText.ToHex(secondary);
    }

    private void SetColor(ref string field, string? value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        SetValue(ref field, ColorText.Normalize(value, field), propertyName);
    }

    private void SetNumber(ref double field, double value, double minimum, double maximum, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        SetValue(ref field, double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : field, propertyName);
    }

    private void SetNumber(ref int field, int value, int minimum, int maximum, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        SetValue(ref field, Math.Clamp(value, minimum, maximum), propertyName);
    }

    private void SetValue<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        if (!_isApplying)
        {
            EditorValueChanging?.Invoke(this, EventArgs.Empty);
        }

        _ = SetProperty(ref field, value, propertyName);
        if (propertyName is nameof(Surface) or nameof(Card) or nameof(Border) or
            nameof(PrimaryText) or nameof(SecondaryText) or nameof(CpuAccent) or
            nameof(GpuAccent) or nameof(MemoryAccent) or nameof(NetworkAccent) or
            nameof(LatencyAccent) or nameof(WeatherAccent) or nameof(Track) or
            nameof(Warning) or nameof(Critical) or nameof(Success) or
            nameof(CardOpacity))
        {
            RaiseDerivedProperties();
        }

        if (!_isApplying)
        {
            DesignChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RaiseDerivedProperties()
    {
        OnPropertyChanged(nameof(SurfaceBrush));
        OnPropertyChanged(nameof(CardBrush));
        OnPropertyChanged(nameof(BorderBrush));
        OnPropertyChanged(nameof(PrimaryTextBrush));
        OnPropertyChanged(nameof(SecondaryTextBrush));
        OnPropertyChanged(nameof(CpuAccentBrush));
        OnPropertyChanged(nameof(GpuAccentBrush));
        OnPropertyChanged(nameof(MemoryAccentBrush));
        OnPropertyChanged(nameof(NetworkAccentBrush));
        OnPropertyChanged(nameof(LatencyAccentBrush));
        OnPropertyChanged(nameof(WeatherAccentBrush));
        OnPropertyChanged(nameof(TrackBrush));
        OnPropertyChanged(nameof(WarningBrush));
        OnPropertyChanged(nameof(CriticalBrush));
        OnPropertyChanged(nameof(SuccessBrush));
        OnPropertyChanged(nameof(PrimaryContrastRatio));
        OnPropertyChanged(nameof(SecondaryContrastRatio));
        OnPropertyChanged(nameof(HasReadableContrast));
        OnPropertyChanged(nameof(ContrastSummary));
    }

    private static SolidColorBrush FrozenBrush(string value)
    {
        var brush = new SolidColorBrush(ColorText.Parse(value, Colors.Transparent));
        brush.Freeze();
        return brush;
    }

    private static double Contrast(string foreground, string background) =>
        Contrast(ColorText.Parse(foreground, Colors.White), ColorText.Parse(background, Colors.Black));

    private Color EffectiveCardColor()
    {
        var card = ColorText.Parse(Card, Colors.Black);
        var surface = ColorText.Parse(Surface, Colors.Black);
        var opacity = Math.Clamp((card.A / 255d) * CardOpacity, 0, 1);
        static byte Blend(byte foreground, byte background, double opacity) =>
            (byte)Math.Round(foreground * opacity + background * (1 - opacity));
        return Color.FromRgb(
            Blend(card.R, surface.R, opacity),
            Blend(card.G, surface.G, opacity),
            Blend(card.B, surface.B, opacity));
    }

    private static double Contrast(Color first, Color second)
    {
        var light = Math.Max(Luminance(first), Luminance(second));
        var dark = Math.Min(Luminance(first), Luminance(second));
        return (light + 0.05) / (dark + 0.05);
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }
}
