using OpsMonitor.Studio.Models;
using System.IO;
using System.Text.Json;

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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public LocalStudioSettingsSink()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        SettingsPath = Path.Combine(appData, "OPS Monitor", "Studio", "studio-settings.json");
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
            return JsonSerializer.Deserialize<StudioSettingsSnapshot>(json, JsonOptions);
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
    }

    public void Save(StudioSettingsSnapshot snapshot)
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);

        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot, JsonOptions));
        File.Move(temporaryPath, SettingsPath, true);
        LastWarning = null;
        SettingsChanged?.Invoke(this, snapshot);
    }

    public void Dispose()
    {
        // No long-lived handles are held by the local editor store.
    }
}
