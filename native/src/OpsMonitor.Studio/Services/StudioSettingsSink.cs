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
    internal const int CurrentSchemaVersion = 2;

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
            Scenes = snapshot.Scenes?
                .Select(scene => scene with
                {
                    Layout = NormalizeLayout(scene.Layout),
                })
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
        foreach (var module in modules.OrderBy(item => item.Order))
        {
            if (string.IsNullOrWhiteSpace(module.Id) || !seen.Add(module.Id))
            {
                continue;
            }

            normalized.Add(module with
            {
                Order = normalized.Count,
                Name = string.IsNullOrWhiteSpace(module.Name) ? module.Id : module.Name.Trim(),
                Size = module.Size is "Small" or "Medium" or "Large"
                    ? module.Size
                    : "Medium",
                Visualization = module.Visualization is
                    "Number only" or "Bar" or "Sparkline" or "Bar + sparkline"
                    ? module.Visualization
                    : "Bar + sparkline",
                Precision = module.Precision is
                    "Whole numbers" or "1 decimal" or "2 decimals" or "Adaptive"
                    ? module.Precision
                    : "Adaptive",
            });
        }

        return normalized;
    }

    private static string NormalizeTheme(string theme)
        => (theme ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "abyss" or "void" => "void",
            "violet" or "ultraviolet" or "aurora" => "aurora",
            "graphite" or "frost" or "slate" or "slate / high contrast" => "slate",
            "ember" => "ember",
            "contrast" => "contrast",
            _ => "void",
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
