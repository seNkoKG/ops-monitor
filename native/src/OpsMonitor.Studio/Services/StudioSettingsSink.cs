using System.IO;
using System.Text.Json;
using OpsMonitor.Studio.Models;

namespace OpsMonitor.Studio.Services;

/// <summary>
/// The one boundary the production app needs to replace when Core gains a
/// persisted settings contract. The Studio remains fully interactive today.
/// </summary>
public interface IStudioSettingsSink : IDisposable
{
    string SettingsPath { get; }
    string RuntimeSettingsPath { get; }
    string? LastWarning { get; }
    event EventHandler<StudioSettingsSnapshot>? SettingsChanged;
    StudioSettingsSnapshot? Reload();
    void Save(StudioSettingsSnapshot snapshot);
}

public sealed class LocalStudioSettingsSink : IStudioSettingsSink
{
    internal const int CurrentSchemaVersion = 5;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public LocalStudioSettingsSink(string? settingsPath = null)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        SettingsPath = settingsPath ??
            Path.Combine(appData, "OPS Monitor", "Studio", "studio-settings.json");
    }

    public string SettingsPath { get; }
    public string RuntimeSettingsPath => string.Empty;
    public string? LastWarning { get; private set; }
    public event EventHandler<StudioSettingsSnapshot>? SettingsChanged;

    public StudioSettingsSnapshot? Reload()
    {
        LastWarning = null;
        if (!File.Exists(SettingsPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var snapshot = JsonSerializer.Deserialize<StudioSettingsSnapshot>(json, JsonOptions);
            if (snapshot is null)
            {
                LastWarning = "The Studio editor settings file was empty; defaults are active.";
                return null;
            }

            if (snapshot.SchemaVersion > CurrentSchemaVersion)
            {
                LastWarning =
                    $"Studio settings schema {snapshot.SchemaVersion} is newer than this build; defaults are active.";
                return null;
            }

            return StudioSettingsMigration.Normalize(snapshot);
        }
        catch (JsonException)
        {
            LastWarning = "The Studio editor settings file is invalid; defaults are active.";
            return null;
        }
        catch (IOException)
        {
            LastWarning = "The Studio editor settings file could not be read.";
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            LastWarning = "The Studio editor settings file is not accessible; defaults are active.";
            return null;
        }
    }

    public void Save(StudioSettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot = StudioSettingsMigration.Normalize(snapshot);
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(SettingsPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot, JsonOptions));
            if (File.Exists(SettingsPath))
            {
                var backupPath = SettingsPath + ".bak";
                try
                {
                    File.Replace(temporaryPath, SettingsPath, backupPath, ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Move(temporaryPath, SettingsPath, true);
                }
                catch (IOException)
                {
                    File.Move(temporaryPath, SettingsPath, true);
                }
            }
            else
            {
                File.Move(temporaryPath, SettingsPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        LastWarning = null;
        SettingsChanged?.Invoke(this, snapshot);
    }

    public void Dispose()
    {
        // No long-lived handles are held by the local editor store.
    }
}

internal static class StudioSettingsMigration
{
    private static readonly HashSet<string> Layouts =
        new(["Pill", "Rail", "Dock", "Mini"], StringComparer.Ordinal);
    private static readonly HashSet<string> Densities =
        new(["Compact", "Comfortable", "Airy"], StringComparer.Ordinal);
    private static readonly HashSet<string> PerformanceModes =
        new(["Performance", "Balanced", "Efficiency"], StringComparer.Ordinal);
    public static StudioSettingsSnapshot Normalize(StudioSettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var layout = NormalizeLayout(snapshot.Layout);
        var theme = NormalizeTheme(snapshot.Theme);
        var density = Densities.Contains(snapshot.Density)
            ? snapshot.Density
            : "Compact";
        var performanceMode = PerformanceModes.Contains(snapshot.PerformanceMode)
            ? snapshot.PerformanceMode
            : "Balanced";
        return snapshot with
        {
            SchemaVersion = LocalStudioSettingsSink.CurrentSchemaVersion,
            Scene = string.IsNullOrWhiteSpace(snapshot.Scene)
                ? "Daily driver"
                : snapshot.Scene.Trim(),
            Layout = layout,
            Theme = theme,
            BackgroundOpacity = ClampFinite(snapshot.BackgroundOpacity, 0.08, 1, 0.82),
            ContentOpacity = ClampFinite(snapshot.ContentOpacity, 0.82, 1, 1),
            BlurStrength = ClampFinite(snapshot.BlurStrength, 0, 40, 0),
            Density = density,
            FontScale = ClampFinite(snapshot.FontScale, 0.9, 1.35, 1),
            WidgetWidth = ClampFinite(snapshot.WidgetWidth, 176, 1_600, 290),
            WidgetHeight = ClampFinite(snapshot.WidgetHeight, 140, 1_000, 660),
            WidgetScalePercent = Math.Clamp(snapshot.WidgetScalePercent, 80, 160),
            UpdateCadenceSeconds = ClampFinite(
                snapshot.UpdateCadenceSeconds,
                0.5,
                10,
                2),
            PerformanceMode = performanceMode,
            VisibleModules = snapshot.VisibleModules?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray() ?? [],
            Modules = NormalizeModules(snapshot.Modules),
            ThemeDetails = NormalizeThemeDetails(snapshot.ThemeDetails, theme),
            Scenes = snapshot.Scenes?
                .Select(scene => scene with
                {
                    Layout = NormalizeLayout(scene.Layout),
                })
                .ToArray(),
            SensorPins = snapshot.SensorPins?
                .Where(pin =>
                    !string.IsNullOrWhiteSpace(pin.MetricId) &&
                    pin.MetricId.StartsWith("hardware.", StringComparison.Ordinal) &&
                    pin.ModuleId is "cpu" or "gpu" or "ram" or "disk")
                .DistinctBy(pin => pin.MetricId, StringComparer.Ordinal)
                .Take(12)
                .ToArray(),
        };
    }

    private static string NormalizeLayout(string? layout)
    {
        var candidate = layout?.Trim() ?? string.Empty;
        if (Layouts.Contains(candidate))
        {
            return candidate;
        }

        return candidate.Equals("Canvas", StringComparison.OrdinalIgnoreCase)
            ? "Mini"
            : "Pill";
    }

    private static List<StudioModuleSnapshot>? NormalizeModules(
        IReadOnlyList<StudioModuleSnapshot>? modules)
    {
        if (modules is null)
        {
            return null;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<StudioModuleSnapshot>(modules.Count);
        foreach (var module in modules.OfType<StudioModuleSnapshot>().OrderBy(item => item.Order))
        {
            if (string.IsNullOrWhiteSpace(module.Id) || !seen.Add(module.Id))
            {
                continue;
            }

            normalized.Add(module with
            {
                Order = normalized.Count,
                Name = string.IsNullOrWhiteSpace(module.Name) ? module.Id : module.Name.Trim(),
                Size = module.Size is "Small" or "Medium" or "Large" or "Wide"
                    ? module.Size
                    : "Medium",
                Visualization = NormalizeVisualization(module.Visualization),
                Precision = module.Precision is
                    "Whole numbers" or "1 decimal" or "2 decimals" or "Adaptive"
                    ? module.Precision
                    : "Adaptive",
                Icon = (module.Icon ?? string.Empty).Trim()[..Math.Min((module.Icon ?? string.Empty).Trim().Length, 8)],
                Accent = ColorText.Normalize(module.Accent, "#FF48DCF9"),
                CardColor = NormalizeOptionalColor(module.CardColor),
                BorderColor = NormalizeOptionalColor(module.BorderColor),
                PrimaryTextColor = NormalizeOptionalColor(module.PrimaryTextColor),
                SecondaryTextColor = NormalizeOptionalColor(module.SecondaryTextColor),
                TrackColor = NormalizeOptionalColor(module.TrackColor),
                CardOpacity = ClampFinite(module.CardOpacity, 0.2, 1, 1),
                BorderOpacity = ClampFinite(module.BorderOpacity, 0, 1, 1),
                CardCornerRadiusOverride = NormalizeOverride(module.CardCornerRadiusOverride, 0, 40),
                CardBorderWidthOverride = NormalizeOverride(module.CardBorderWidthOverride, 0, 4),
                CardGapOverride = NormalizeOverride(module.CardGapOverride, 0, 20),
                CardPaddingOverride = NormalizeOverride(module.CardPaddingOverride, 0, 28),
                AccentWidthOverride = NormalizeOverride(module.AccentWidthOverride, 0, 10),
                ProgressHeightOverride = NormalizeOverride(module.ProgressHeightOverride, 1, 12),
                ProgressCornerRadiusOverride = NormalizeOverride(module.ProgressCornerRadiusOverride, 0, 6),
                SparklineThicknessOverride = NormalizeOverride(module.SparklineThicknessOverride, 0.5, 5),
                SparklineFillOpacityOverride = NormalizeOverride(module.SparklineFillOpacityOverride, 0, 0.5),
                LabelSizeOverride = NormalizeOverride(module.LabelSizeOverride, 8, 26),
                SecondarySizeOverride = NormalizeOverride(module.SecondarySizeOverride, 8, 24),
                ValueSizeOverride = NormalizeOverride(module.ValueSizeOverride, 10, 42),
                IconSizeOverride = NormalizeOverride(module.IconSizeOverride, 8, 32),
                LabelWeightOverride = NormalizeOverride(module.LabelWeightOverride, 100, 900),
                ValueWeightOverride = NormalizeOverride(module.ValueWeightOverride, 100, 900),
            });
        }

        return normalized;
    }

    private static string NormalizeTheme(string theme)
    {
        var candidate = (theme ?? string.Empty).Trim().ToLowerInvariant();
        var builtIn = candidate switch
        {
            "abyss" or "void" => "void",
            "violet" or "ultraviolet" or "aurora" => "aurora",
            "graphite" or "frost" or "slate" or "slate / high contrast" => "slate",
            "ember" => "ember",
            "contrast" => "contrast",
            _ => string.Empty,
        };
        if (!string.IsNullOrEmpty(builtIn))
        {
            return builtIn;
        }

        var safe = new string(candidate
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .Take(48)
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "void" : safe;
    }

    private static StudioThemeSnapshot? NormalizeThemeDetails(
        StudioThemeSnapshot? theme,
        string fallbackId)
    {
        if (theme is null)
        {
            return null;
        }

        return theme with
        {
            Id = string.IsNullOrWhiteSpace(theme.Id) ? fallbackId : NormalizeTheme(theme.Id),
            Name = string.IsNullOrWhiteSpace(theme.Name) ? "Custom design" : theme.Name.Trim(),
            Surface = ColorText.Normalize(theme.Surface, "#FF080B12"),
            Card = ColorText.Normalize(theme.Card, "#FF0F1521"),
            Border = ColorText.Normalize(theme.Border, "#FF364258"),
            Accent = ColorText.Normalize(theme.Accent, "#FF48DCF9"),
            PrimaryText = ColorText.Normalize(theme.PrimaryText, "#FFF6F9FF"),
            SecondaryText = ColorText.Normalize(theme.SecondaryText, "#FFB8C4D6"),
            CpuAccent = ColorText.Normalize(theme.CpuAccent, "#FF48DCF9"),
            GpuAccent = ColorText.Normalize(theme.GpuAccent, "#FFFF4FD8"),
            MemoryAccent = ColorText.Normalize(theme.MemoryAccent, "#FF58E6B2"),
            NetworkAccent = ColorText.Normalize(theme.NetworkAccent, "#FF62A7FF"),
            LatencyAccent = ColorText.Normalize(theme.LatencyAccent, "#FFFFC35A"),
            WeatherAccent = ColorText.Normalize(theme.WeatherAccent, "#FF62A7FF"),
            Track = ColorText.Normalize(theme.Track, "#55364258"),
            Warning = ColorText.Normalize(theme.Warning, "#FFFFC35A"),
            Critical = ColorText.Normalize(theme.Critical, "#FFFF566E"),
            Success = ColorText.Normalize(theme.Success, "#FF58E6B2"),
            CornerRadius = ClampFinite(theme.CornerRadius, 0, 48, 24),
            CardCornerRadius = ClampFinite(theme.CardCornerRadius, 0, 40, 12),
            BlurStrength = ClampFinite(theme.BlurStrength, 0, 1, 0.7),
            ShadowOpacity = ClampFinite(theme.ShadowOpacity, 0, 0.8, 0.3),
            GlowOpacity = ClampFinite(theme.GlowOpacity, 0, 0.5, 0.12),
            BorderWidth = ClampFinite(theme.BorderWidth, 0, 4, 1),
            CardBorderWidth = ClampFinite(theme.CardBorderWidth, 0, 4, 1),
            CardGap = ClampFinite(theme.CardGap, 0, 20, 6),
            ContentPadding = ClampFinite(theme.ContentPadding, 0, 28, 10),
            CardPadding = ClampFinite(theme.CardPadding, 0, 28, 10),
            CardOpacity = ClampFinite(theme.CardOpacity, 0, 1, 0.72),
            AccentWidth = ClampFinite(theme.AccentWidth, 0, 10, 3),
            ProgressHeight = ClampFinite(theme.ProgressHeight, 1, 12, 4),
            ProgressCornerRadius = ClampFinite(theme.ProgressCornerRadius, 0, 6, 2),
            SparklineThickness = ClampFinite(theme.SparklineThickness, 0.5, 5, 1.5),
            SparklineFillOpacity = ClampFinite(theme.SparklineFillOpacity, 0, 0.5, 0.16),
            HeaderHeight = ClampFinite(theme.HeaderHeight, 18, 64, 36),
            FontFamily = string.IsNullOrWhiteSpace(theme.FontFamily) ? "Segoe UI Variable" : theme.FontFamily.Trim(),
            HeaderSize = ClampFinite(theme.HeaderSize, 8, 24, 11),
            LabelSize = ClampFinite(theme.LabelSize, 8, 26, 11),
            SecondarySize = ClampFinite(theme.SecondarySize, 8, 24, 10),
            ValueSize = ClampFinite(theme.ValueSize, 10, 42, 18),
            IconSize = ClampFinite(theme.IconSize, 8, 32, 14),
            MinimumReadableSize = ClampFinite(theme.MinimumReadableSize, 8, 18, 10),
            HeaderWeight = Math.Clamp(theme.HeaderWeight, 100, 900),
            LabelWeight = Math.Clamp(theme.LabelWeight, 100, 900),
            SecondaryWeight = Math.Clamp(theme.SecondaryWeight, 100, 900),
            ValueWeight = Math.Clamp(theme.ValueWeight, 100, 900),
            TransitionMilliseconds = Math.Clamp(theme.TransitionMilliseconds, 0, 600),
        };
    }

    private static double? NormalizeOverride(double? value, double minimum, double maximum) =>
        value is { } candidate && double.IsFinite(candidate)
            ? Math.Clamp(candidate, minimum, maximum)
            : null;

    private static int? NormalizeOverride(int? value, int minimum, int maximum) =>
        value is { } candidate
            ? Math.Clamp(candidate, minimum, maximum)
            : null;

    private static string NormalizeOptionalColor(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : ColorText.Normalize(value, string.Empty);

    private static string NormalizeVisualization(string? value) =>
        value switch
        {
            "Number only" or "Value only" => "Value only",
            "Bar" or "Bar only" => "Bar only",
            "Dial" or "Value + bar" => "Value + bar",
            "Sparkline" or "Sparkline only" => "Sparkline only",
            "Bar + sparkline" or "Value + sparkline" => "Value + sparkline",
            _ => "Value + sparkline",
        };

    private static double ClampFinite(
        double value,
        double minimum,
        double maximum,
        double fallback)
        => double.IsFinite(value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;
}
