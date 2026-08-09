using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpsMonitor.Core.Settings;
using OpsMonitor.Widget.Models;
using CoreWidgetDensity = OpsMonitor.Core.Settings.WidgetDensity;
using WidgetDensity = OpsMonitor.Widget.Models.WidgetDensity;

namespace OpsMonitor.Widget.Services;

public static class WidgetSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string SettingsDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OPS Monitor");

    public static string SettingsFilePath => Path.Combine(SettingsDirectory, "widget-state.json");

    public static WidgetSettings Reload() => Load();

    public static bool Save(WidgetSettings settings) => TrySave(settings);

    public static WidgetSettings Load()
    {
        var settings = LoadLocal();
        return ApplyCoreSettings(settings);
    }

    private static WidgetSettings LoadLocal()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new WidgetSettings();
            }

            var json = File.ReadAllText(SettingsFilePath);
            var settings = JsonSerializer.Deserialize<WidgetSettings>(json, SerializerOptions);
            return Normalize(settings ?? new WidgetSettings());
        }
        catch (IOException)
        {
            return new WidgetSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new WidgetSettings();
        }
        catch (JsonException)
        {
            return new WidgetSettings();
        }
    }

    public static bool TrySave(WidgetSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(Normalize(settings), SerializerOptions);
            var temporaryPath = SettingsFilePath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, SettingsFilePath, true);
            SaveCoreSettings(settings);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static WidgetSettings Normalize(WidgetSettings settings)
    {
        settings.SurfaceOpacity = Math.Clamp(settings.SurfaceOpacity, 0.08, 1);
        settings.ContentOpacity = Math.Clamp(settings.ContentOpacity, 0.82, 1);
        settings.UpdateCadenceSeconds = double.IsFinite(settings.UpdateCadenceSeconds)
            ? Math.Clamp(settings.UpdateCadenceSeconds, 0.5, 10)
            : 1;
        settings.ScalePercent = Math.Clamp(settings.ScalePercent, 80, 160);
        settings.WeatherRefreshMinutes = Math.Clamp(settings.WeatherRefreshMinutes, 5, 60);
        settings.WeatherLatitude = double.IsFinite(settings.WeatherLatitude)
            ? Math.Clamp(settings.WeatherLatitude, -90, 90)
            : 46.2366;
        settings.WeatherLongitude = double.IsFinite(settings.WeatherLongitude)
            ? Math.Clamp(settings.WeatherLongitude, -180, 180)
            : 15.2259;
        if (string.IsNullOrWhiteSpace(settings.WeatherLocationName))
        {
            settings.WeatherLocationName = "Celje";
        }

        if (string.IsNullOrWhiteSpace(settings.WeatherCountry))
        {
            settings.WeatherCountry = "Slovenia";
        }

        if (string.IsNullOrWhiteSpace(settings.WeatherTimeZone))
        {
            settings.WeatherTimeZone = "Europe/Ljubljana";
        }

        if (string.IsNullOrWhiteSpace(settings.Theme))
        {
            settings.Theme = "Void";
        }

        settings.Width = NormalizeDimension(settings.Width, 176, 1_600);
        settings.Height = NormalizeDimension(settings.Height, 140, 1_000);
        settings.Left = NormalizeCoordinate(settings.Left);
        settings.Top = NormalizeCoordinate(settings.Top);

        settings.ModuleOrder =
            WidgetModuleCatalog.NormalizeOrder(settings.ModuleOrder);
        settings.EnabledModules =
            WidgetModuleCatalog.NormalizeEnabled(settings.EnabledModules);
        if (settings.ShowWeather &&
            !settings.EnabledModules.Contains(WidgetModuleCatalog.Weather, StringComparer.Ordinal))
        {
            settings.EnabledModules.Add(WidgetModuleCatalog.Weather);
        }
        else if (!settings.ShowWeather)
        {
            settings.EnabledModules.RemoveAll(key =>
                StringComparer.Ordinal.Equals(key, WidgetModuleCatalog.Weather));
        }
        if (settings.ShowBattery)
        {
            if (!settings.EnabledModules.Contains(
                    WidgetModuleCatalog.Battery,
                    StringComparer.Ordinal))
            {
                settings.EnabledModules.Add(WidgetModuleCatalog.Battery);
            }
        }
        else
        {
            settings.EnabledModules.RemoveAll(key =>
                StringComparer.Ordinal.Equals(key, WidgetModuleCatalog.Battery));
        }

        return settings;
    }

    private static double? NormalizeDimension(double? value, double minimum, double maximum)
        => value is { } dimension && double.IsFinite(dimension)
            ? Math.Clamp(dimension, minimum, maximum)
            : null;

    private static double? NormalizeCoordinate(double? value)
        => value is { } coordinate && double.IsFinite(coordinate) ? coordinate : null;

    private static WidgetSettings ApplyCoreSettings(WidgetSettings settings)
    {
        try
        {
            using var repository = new JsonSettingsRepository();
            if (!File.Exists(repository.SettingsPath))
            {
                return settings;
            }

            var document = repository.LoadAsync().GetAwaiter().GetResult();
            return MergeCoreSettings(settings, document);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            JsonException)
        {
            return settings;
        }
    }

    internal static WidgetSettings MergeCoreSettings(
        WidgetSettings settings,
        OpsSettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(document);

        var widget = document.Widgets.FirstOrDefault(candidate => candidate.Enabled);
        if (widget is null)
        {
            return Normalize(settings);
        }

        settings.Layout = widget.Design switch
        {
            WidgetDesign.Pill => WidgetLayout.Pill,
            WidgetDesign.Dock => WidgetLayout.Dock,
            WidgetDesign.Canvas => WidgetLayout.Mini,
            _ => WidgetLayout.Rail
        };
        settings.Density = widget.Density switch
        {
            CoreWidgetDensity.Normal => WidgetDensity.Normal,
            CoreWidgetDensity.Comfortable => WidgetDensity.Detail,
            _ => WidgetDensity.Compact
        };
        settings.InteractionMode = widget.Window.ClickThrough
            ? WidgetInteractionMode.ClickThrough
            : widget.Window.Locked
                ? WidgetInteractionMode.Locked
                : WidgetInteractionMode.Edit;
        settings.Topmost = widget.Window.AlwaysOnTop;
        settings.Draggable = widget.Window.Draggable;
        settings.Resizable = widget.Window.Resizable;

        settings.StartAtSignIn = document.General.LaunchAtSignIn;
        settings.SurfaceOpacity = widget.Window.SurfaceOpacity;
        settings.ContentOpacity = widget.Window.ContentOpacity;
        settings.ScalePercent = widget.Window.ScalePercent;
        settings.Left = widget.Window.Left;
        settings.Top = widget.Window.Top;
        settings.Width = widget.Window.Width;
        settings.Height = widget.Window.Height;

        var theme = document.Themes.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.Id, widget.ThemeId));
        if (theme is not null)
        {
            settings.Theme = theme.Name;
            settings.CoreThemeId = theme.Id;
        }

        settings.RuntimeThemes = document.Themes
            .Select(ToRuntimeTheme)
            .ToArray();
        var moduleConfiguration =
            WidgetModuleCatalog.FromCoreModules(widget.Modules);
        settings.ModuleOrder = [.. moduleConfiguration.Order];
        settings.EnabledModules = [.. moduleConfiguration.Enabled];
        if (settings.ShowWeather)
        {
            settings.EnabledModules.Add(WidgetModuleCatalog.Weather);
        }
        settings.ModulePresentation =
            WidgetModuleCatalog.GetPresentation(widget.Modules);
        settings.ModuleMetricBindings =
            WidgetModuleCatalog.GetMetricBindings(widget.Modules);
        settings.ShowBattery = settings.EnabledModules.Contains(
            WidgetModuleCatalog.Battery,
            StringComparer.Ordinal);
        var profile = document.PerformanceProfiles.FirstOrDefault(candidate =>
                          candidate.Enabled &&
                          StringComparer.Ordinal.Equals(
                              candidate.Id,
                              widget.PerformanceProfileId))
                      ?? document.PerformanceProfiles.FirstOrDefault(candidate =>
                          candidate.Enabled);
        if (profile is not null)
        {
            settings.UpdateCadenceSeconds = profile.UiRefreshCadence.TotalSeconds;
        }

        return Normalize(settings);
    }

    private static void SaveCoreSettings(WidgetSettings settings)
    {
        try
        {
            using var repository = new JsonSettingsRepository();
            repository.UpdateAsync(document => MergeWidgetSettings(document, settings))
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            JsonException)
        {
            // A locked settings file must never make the widget unusable.
        }
    }

    internal static OpsSettingsDocument MergeWidgetSettings(
        OpsSettingsDocument document,
        WidgetSettings settings)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(settings);

        settings = Normalize(settings);
        if (document.Widgets.Count == 0)
        {
            document = OpsSettingsDocument.CreateDefault();
        }

        var widgetIndex = document.Widgets.FindIndex(candidate => candidate.Enabled);
        if (widgetIndex < 0)
        {
            widgetIndex = 0;
        }

        var current = document.Widgets[widgetIndex];
        var themes = document.Themes.ToList();
        if (themes.All(theme =>
                !theme.Name.Equals(settings.Theme, StringComparison.OrdinalIgnoreCase)) &&
            string.IsNullOrWhiteSpace(settings.CoreThemeId) &&
            CreateBuiltInTheme(settings.Theme) is { } builtInTheme)
        {
            themes.Add(builtInTheme);
        }

        var themeId = ResolveThemeId(
            themes,
            settings.Theme,
            settings.CoreThemeId,
            current.ThemeId);
        var batteryAdjustedModules = WidgetModuleCatalog.ApplyBatteryVisibility(
            current.Modules,
            settings.ShowBattery);
        var configuredModules = WidgetModuleCatalog.ApplyConfiguration(
            batteryAdjustedModules,
            settings.ModuleOrder,
            settings.EnabledModules,
            settings.ModulePresentation);
        var mapped = current with
        {
            Design = settings.Layout switch
            {
                WidgetLayout.Pill => WidgetDesign.Pill,
                WidgetLayout.Dock => WidgetDesign.Dock,
                WidgetLayout.Mini => WidgetDesign.Canvas,
                _ => WidgetDesign.Rail
            },
            Density = settings.Density switch
            {
                WidgetDensity.Normal => CoreWidgetDensity.Normal,
                WidgetDensity.Detail => CoreWidgetDensity.Comfortable,
                _ => CoreWidgetDensity.Compact
            },
            ThemeId = themeId,
            Modules = configuredModules,
            Window = current.Window with
            {
                Left = settings.Left,
                Top = settings.Top,
                Width = settings.Width ?? current.Window.Width,
                Height = settings.Height ?? current.Window.Height,
                ScalePercent = settings.ScalePercent,
                AlwaysOnTop = settings.Topmost,
                Locked = settings.InteractionMode == WidgetInteractionMode.Locked,
                Draggable = settings.Draggable,
                Resizable = settings.Resizable,
                ClickThrough =
                    settings.InteractionMode == WidgetInteractionMode.ClickThrough,
                SurfaceOpacity = settings.SurfaceOpacity,
                ContentOpacity = settings.ContentOpacity
            }
        };

        var widgets = document.Widgets.ToList();
        widgets[widgetIndex] = mapped;

        var profiles = document.PerformanceProfiles.ToList();
        var profileIndex = profiles.FindIndex(profile =>
            StringComparer.Ordinal.Equals(profile.Id, current.PerformanceProfileId));
        if (profileIndex < 0)
        {
            profiles.Add(PerformanceProfileSettings.CreateBalanced(
                current.PerformanceProfileId));
            profileIndex = profiles.Count - 1;
        }

        var cadence = TimeSpan.FromSeconds(settings.UpdateCadenceSeconds);
        var profile = profiles[profileIndex];
        var providerCadences = new Dictionary<string, TimeSpan>(
            profile.ProviderCadences,
            StringComparer.Ordinal)
        {
            ["windows.native"] = cadence,
            ["network.connectivity"] = TimeSpan.FromSeconds(
                Math.Clamp(cadence.TotalSeconds * 1.5, 0.5, 15)),
            ["cpu.temperature.bridge"] = TimeSpan.FromSeconds(
                Math.Clamp(cadence.TotalSeconds * 2, 1, 20)),
            ["nvidia"] = cadence
        };
        profiles[profileIndex] = profile with
        {
            UiRefreshCadence = cadence,
            ProviderCadences = providerCadences
        };

        return document with
        {
            General = document.General with
            {
                LaunchAtSignIn = settings.StartAtSignIn
            },
            Widgets = widgets,
            Themes = themes,
            PerformanceProfiles = profiles
        };
    }

    private static ThemeSettings? CreateBuiltInTheme(string name)
    {
        var palette = name.Trim().ToLowerInvariant() switch
        {
            "void" => new ThemePalette
            {
                Background = "#FF080B12",
                Card = "#FF0F1521",
                Border = "#FF364258",
                PrimaryText = "#FFF6F9FF",
                SecondaryText = "#FFB8C4D6",
                CpuAccent = "#FF48DCF9",
                GpuAccent = "#FFFF4FD8",
                NetworkAccent = "#FF48DCF9",
                Warning = "#FFFFC35A",
                Critical = "#FFFF566E",
                Success = "#FF58E6B2"
            },
            "aurora" => new ThemePalette
            {
                Background = "#FF0E0A1B",
                Card = "#FF19132D",
                Border = "#FF4C3E6F",
                PrimaryText = "#FFF9F6FF",
                SecondaryText = "#FFC8BEDE",
                CpuAccent = "#FF56E2FF",
                GpuAccent = "#FFFF5BD7",
                NetworkAccent = "#FF56E2FF",
                Warning = "#FFFFB85B",
                Critical = "#FFFF5874",
                Success = "#FF5BF1BE"
            },
            "slate" => new ThemePalette
            {
                Background = "#FF12181F",
                Card = "#FF1B242F",
                Border = "#FF435365",
                PrimaryText = "#FFF4F9FC",
                SecondaryText = "#FFBECBD7",
                CpuAccent = "#FF48CFEA",
                GpuAccent = "#FFEB63CF",
                NetworkAccent = "#FF48CFEA",
                Warning = "#FFF4B859",
                Critical = "#FFFF566E",
                Success = "#FF56DDA7"
            },
            "ember" => new ThemePalette
            {
                Background = "#FF160E0F",
                Card = "#FF261719",
                Border = "#FF5D383C",
                PrimaryText = "#FFFFF8F4",
                SecondaryText = "#FFDCC4BF",
                CpuAccent = "#FF4DD7EF",
                GpuAccent = "#FFFF5DB3",
                NetworkAccent = "#FF4DD7EF",
                Warning = "#FFFFAC48",
                Critical = "#FFFF5469",
                Success = "#FF5EE1A8"
            },
            "contrast" => new ThemePalette
            {
                Background = "#FF020305",
                Card = "#FF07090D",
                Border = "#FF94AAC6",
                PrimaryText = "#FFFFFFFF",
                SecondaryText = "#FFD6E0EE",
                CpuAccent = "#FF47E5FF",
                GpuAccent = "#FFFF5CE1",
                NetworkAccent = "#FF47E5FF",
                Warning = "#FFFFCC5C",
                Critical = "#FFFF5C74",
                Success = "#FF62F4BB"
            },
            _ => null
        };
        if (palette is null)
        {
            return null;
        }

        var normalizedName = name.Trim();
        return new ThemeSettings
        {
            Id = "widget-theme-" + normalizedName.ToLowerInvariant(),
            Name = normalizedName,
            BuiltIn = true,
            Palette = palette,
            Typography = new ThemeTypography
            {
                FontFamily = "Segoe UI Variable",
                LabelSize = 12,
                ValueSize = 18,
                MinimumReadableSize = 12,
                LabelWeight = 600,
                ValueWeight = 600,
                UseTabularNumbers = true
            }
        };
    }

    private static string ResolveThemeId(
        IEnumerable<ThemeSettings> themes,
        string themeName,
        string? preferredThemeId,
        string fallbackThemeId)
    {
        var materialized = themes.ToArray();
        var named = materialized.FirstOrDefault(theme =>
            theme.Name.Equals(themeName, StringComparison.OrdinalIgnoreCase));
        if (named is not null)
        {
            return named.Id;
        }

        if (!string.IsNullOrWhiteSpace(preferredThemeId) &&
            materialized.Any(theme =>
                StringComparer.Ordinal.Equals(theme.Id, preferredThemeId)))
        {
            return preferredThemeId;
        }

        return fallbackThemeId;
    }

    private static WidgetRuntimeTheme ToRuntimeTheme(ThemeSettings theme) =>
        new()
        {
            Id = theme.Id,
            Name = theme.Name,
            Background = theme.Palette.Background,
            Card = theme.Palette.Card,
            Border = theme.Palette.Border,
            PrimaryText = theme.Palette.PrimaryText,
            SecondaryText = theme.Palette.SecondaryText,
            CpuAccent = theme.Palette.CpuAccent,
            GpuAccent = theme.Palette.GpuAccent,
            NetworkAccent = theme.Palette.NetworkAccent,
            Warning = theme.Palette.Warning,
            Critical = theme.Palette.Critical,
            Success = theme.Palette.Success,
            FontFamily = theme.Typography.FontFamily,
            LabelSize = theme.Typography.LabelSize,
            ValueSize = theme.Typography.ValueSize,
            MinimumReadableSize = theme.Typography.MinimumReadableSize,
            LabelWeight = theme.Typography.LabelWeight,
            ValueWeight = theme.Typography.ValueWeight,
            UseTabularNumbers = theme.Typography.UseTabularNumbers
        };
}
