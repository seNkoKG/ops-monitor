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

    private static WidgetSettings Normalize(WidgetSettings settings)
    {
        settings.SurfaceOpacity = Math.Clamp(settings.SurfaceOpacity, 0.28, 0.98);
        settings.ContentOpacity = Math.Clamp(settings.ContentOpacity, 0.65, 1);
        settings.UpdateCadenceSeconds = double.IsFinite(settings.UpdateCadenceSeconds)
            ? Math.Clamp(settings.UpdateCadenceSeconds, 0.5, 10)
            : 1;

        if (string.IsNullOrWhiteSpace(settings.Theme))
        {
            settings.Theme = "Void";
        }

        settings.Width = NormalizeDimension(settings.Width, 180, 1_600);
        settings.Height = NormalizeDimension(settings.Height, 140, 1_000);
        settings.Left = NormalizeCoordinate(settings.Left);
        settings.Top = NormalizeCoordinate(settings.Top);

        settings.ModuleOrder =
            WidgetModuleCatalog.NormalizeOrder(settings.ModuleOrder);
        settings.EnabledModules =
            WidgetModuleCatalog.NormalizeEnabled(settings.EnabledModules);
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
            var widget = document.Widgets.FirstOrDefault(candidate => candidate.Enabled);
            if (widget is null)
            {
                return settings;
            }

            settings.Layout = widget.Design switch
            {
                WidgetDesign.Pill => WidgetLayout.Pill,
                WidgetDesign.Dock => WidgetLayout.Dock,
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
            settings.Left = widget.Window.Left;
            settings.Top = widget.Window.Top;
            settings.Width = widget.Window.Width;
            settings.Height = widget.Window.Height;

            var theme = document.Themes.FirstOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.Id, widget.ThemeId));
            if (theme is not null)
            {
                settings.Theme = MapThemeName(theme.Name);
            }

            var moduleConfiguration =
                WidgetModuleCatalog.FromCoreModules(widget.Modules);
            settings.ModuleOrder = [.. moduleConfiguration.Order];
            settings.EnabledModules = [.. moduleConfiguration.Enabled];
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
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            JsonException)
        {
            return settings;
        }
    }

    private static void SaveCoreSettings(WidgetSettings settings)
    {
        try
        {
            using var repository = new JsonSettingsRepository();
            var document = repository.LoadAsync().GetAwaiter().GetResult();
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
            var themeId = document.Themes.FirstOrDefault(theme =>
                    theme.Name.Equals(settings.Theme, StringComparison.OrdinalIgnoreCase))
                ?.Id ?? current.ThemeId;
            var mapped = current with
            {
                Design = settings.Layout switch
                {
                    WidgetLayout.Pill => WidgetDesign.Pill,
                    WidgetLayout.Dock => WidgetDesign.Dock,
                    _ => WidgetDesign.Rail
                },
                Density = settings.Density switch
                {
                    WidgetDensity.Normal => CoreWidgetDensity.Normal,
                    WidgetDensity.Detail => CoreWidgetDensity.Comfortable,
                    _ => CoreWidgetDensity.Compact
                },
                ThemeId = themeId,
                Modules = WidgetModuleCatalog.ApplyBatteryVisibility(
                    current.Modules,
                    settings.ShowBattery),
                Window = current.Window with
                {
                    Left = settings.Left,
                    Top = settings.Top,
                    Width = settings.Width ?? current.Window.Width,
                    Height = settings.Height ?? current.Window.Height,
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

            repository.SaveAsync(document with
            {
                General = document.General with
                {
                    LaunchAtSignIn = settings.StartAtSignIn
                },
                Widgets = widgets,
                PerformanceProfiles = profiles
            }).GetAwaiter().GetResult();
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

    private static string MapThemeName(string coreThemeName)
    {
        if (coreThemeName.Contains("aurora", StringComparison.OrdinalIgnoreCase))
        {
            return "Aurora";
        }

        if (coreThemeName.Contains("slate", StringComparison.OrdinalIgnoreCase))
        {
            return "Slate";
        }

        if (coreThemeName.Contains("ember", StringComparison.OrdinalIgnoreCase))
        {
            return "Ember";
        }

        return "Void";
    }
}
