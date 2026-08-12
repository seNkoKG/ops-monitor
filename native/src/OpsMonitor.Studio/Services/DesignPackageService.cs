using System.IO;
using System.Text.Json;
using OpsMonitor.Studio.Models;

namespace OpsMonitor.Studio.Services;

internal static class DesignPackageService
{
    private const long MaximumPackageBytes = 1_048_576;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static void Save(string path, StudioDesignPackage package)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(package);
        var normalized = Normalize(package);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(normalized, JsonOptions));
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static StudioDesignPackage Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("The OPS design package was not found.", path);
        }

        if (info.Length > MaximumPackageBytes)
        {
            throw new InvalidDataException("The OPS design package is larger than 1 MB.");
        }

        var package = JsonSerializer.Deserialize<StudioDesignPackage>(
            File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("The OPS design package is empty.");
        if (package.SchemaVersion > LocalStudioSettingsSink.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Design schema {package.SchemaVersion} requires a newer OPS Monitor build.");
        }

        return Normalize(package);
    }

    private static StudioDesignPackage Normalize(StudioDesignPackage package)
    {
        if (package.Theme is null)
        {
            throw new InvalidDataException("The design package has no theme.");
        }

        var modules = package.Modules?.OfType<StudioModuleSnapshot>().ToArray() ?? [];
        var shell = StudioSettingsMigration.Normalize(new StudioSettingsSnapshot(
            "Imported design",
            package.Layout,
            package.Theme.Id,
            0.82,
            1,
            24,
            package.Density,
            1,
            true,
            false,
            false,
            true,
            true,
            modules.Where(module => module.Enabled).Select(module => module.Id).ToArray(),
            Modules: modules,
            ThemeDetails: package.Theme));

        return package with
        {
            SchemaVersion = LocalStudioSettingsSink.CurrentSchemaVersion,
            Name = string.IsNullOrWhiteSpace(package.Name) ? "Imported design" : package.Name.Trim(),
            Layout = shell.Layout,
            Density = shell.Density,
            Theme = shell.ThemeDetails ?? package.Theme,
            Modules = shell.Modules ?? []
        };
    }
}
