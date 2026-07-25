using System.Text.Json;
using System.Text.Json.Serialization;
using OpsMonitor.Core.Alerts;
using OpsMonitor.Core.Metrics;

namespace OpsMonitor.Core.Settings;

public sealed class SettingsSavedEventArgs : EventArgs
{
    public SettingsSavedEventArgs(OpsSettingsDocument settings, string path)
    {
        Settings = settings;
        Path = path;
    }

    public OpsSettingsDocument Settings { get; }
    public string Path { get; }
}

public interface ISettingsRepository
{
    string SettingsPath { get; }
    string? LastLoadWarning { get; }
    Task<OpsSettingsDocument> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(
        OpsSettingsDocument settings,
        CancellationToken cancellationToken = default);
}

public sealed class JsonSettingsRepository : ISettingsRepository, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    public JsonSettingsRepository(string? settingsPath = null)
    {
        SettingsPath = settingsPath ?? GetDefaultSettingsPath();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public event EventHandler<SettingsSavedEventArgs>? Saved;

    public string SettingsPath { get; }
    public string? LastLoadWarning { get; private set; }

    public async Task<OpsSettingsDocument> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LastLoadWarning = null;
            if (!File.Exists(SettingsPath))
            {
                return OpsSettingsDocument.CreateDefault();
            }

            try
            {
                await using var stream = new FileStream(
                    SettingsPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    16 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                using var json = await JsonDocument.ParseAsync(
                        stream,
                        new JsonDocumentOptions
                        {
                            AllowTrailingCommas = true,
                            CommentHandling = JsonCommentHandling.Skip
                        },
                        cancellationToken)
                    .ConfigureAwait(false);

                var schemaVersion = ReadSchemaVersion(json.RootElement);
                if (schemaVersion > OpsSettingsDocument.CurrentSchemaVersion)
                {
                    throw new InvalidDataException(
                        $"Settings schema {schemaVersion} is newer than the supported " +
                        $"schema {OpsSettingsDocument.CurrentSchemaVersion}.");
                }

                var settings = json.RootElement.Deserialize<OpsSettingsDocument>(_jsonOptions);
                if (settings is null)
                {
                    throw new InvalidDataException("The settings file contained no document.");
                }

                return NormalizeAndValidate(settings);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is JsonException or IOException or UnauthorizedAccessException)
            {
                LastLoadWarning =
                    $"Settings could not be loaded; defaults are active. {exception.Message}";
                return OpsSettingsDocument.CreateDefault();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        OpsSettingsDocument settings,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        settings = NormalizeAndValidate(settings) with
        {
            SchemaVersion = OpsSettingsDocument.CurrentSchemaVersion,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("The settings path has no parent directory.");
            }

            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(SettingsPath)}.{Guid.NewGuid():N}.tmp");

            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        settings,
                        _jsonOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(SettingsPath))
            {
                var backupPath = SettingsPath + ".bak";
                try
                {
                    File.Replace(temporaryPath, SettingsPath, backupPath, ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Move(temporaryPath, SettingsPath, overwrite: true);
                }
                catch (IOException)
                {
                    File.Move(temporaryPath, SettingsPath, overwrite: true);
                }
            }
            else
            {
                File.Move(temporaryPath, SettingsPath);
            }

            temporaryPath = null;
            Saved?.Invoke(this, new SettingsSavedEventArgs(settings, SettingsPath));
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // A failed best-effort cleanup must not hide the original save error.
                }
            }

            _gate.Release();
        }
    }

    public static string GetDefaultSettingsPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OPS Monitor",
            "settings.json");

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private static int ReadSchemaVersion(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The settings root must be a JSON object.");
        }

        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals("schemaVersion", StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.GetInt32();
            }
        }

        return 0;
    }

    private static OpsSettingsDocument NormalizeAndValidate(OpsSettingsDocument settings)
    {
        if (settings.SchemaVersion is < 0 or > OpsSettingsDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported settings schema version {settings.SchemaVersion}.");
        }

        if (settings.DataRetention.MaximumSamplesPerMetric < 2)
        {
            throw new InvalidDataException(
                "Data retention must allow at least two samples per metric.");
        }

        if (settings.DataRetention.Retention <= TimeSpan.Zero)
        {
            throw new InvalidDataException("Data retention duration must be positive.");
        }

        EnsureUnique(settings.Widgets.Select(item => item.Id), "widget");
        EnsureUnique(settings.Themes.Select(item => item.Id), "theme");
        EnsureUnique(settings.Scenes.Select(item => item.Id), "scene");
        EnsureUnique(settings.PerformanceProfiles.Select(item => item.Id), "profile");
        EnsureUnique(settings.Hotkeys.Select(item => item.Id), "hotkey");
        EnsureUnique(settings.AlertRules.Select(item => item.Id), "alert rule");

        var widgets = settings.Widgets
            .Select(widget => widget with
            {
                Modules = widget.Modules
                    .Select(RepairModuleMetricIds)
                    .ToList()
            })
            .ToList();
        var alerts = settings.AlertRules
            .Select(RepairAlertMetricId)
            .ToList();

        return settings with
        {
            SchemaVersion = OpsSettingsDocument.CurrentSchemaVersion,
            Widgets = widgets,
            AlertRules = alerts
        };
    }

    private static ModuleSettings RepairModuleMetricIds(ModuleSettings module)
    {
        var known = KnownModuleMetrics(module.Id, module.Title);
        var primary = string.IsNullOrWhiteSpace(module.PrimaryMetric.Value)
            ? known.Primary ??
              new MetricId("custom." + MetricSegment(module.Id, "module"))
            : module.PrimaryMetric;
        var secondary = module.SecondaryMetric is { } configured &&
                        !string.IsNullOrWhiteSpace(configured.Value)
            ? configured
            : known.Secondary;
        var additional = module.AdditionalMetrics
            .Where(metric => !string.IsNullOrWhiteSpace(metric.Value))
            .Distinct()
            .ToList();

        return module with
        {
            PrimaryMetric = primary,
            SecondaryMetric = secondary,
            AdditionalMetrics = additional
        };
    }

    private static AlertRule RepairAlertMetricId(AlertRule rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.MetricId.Value))
        {
            return rule;
        }

        var normalized = $"{rule.Id} {rule.Name}".ToLowerInvariant();
        var metric = normalized switch
        {
            var value when value.Contains("cpu", StringComparison.Ordinal) =>
                WellKnownMetrics.CpuTemperature,
            var value when value.Contains("gpu", StringComparison.Ordinal) &&
                           value.Contains("temp", StringComparison.Ordinal) =>
                WellKnownMetrics.GpuTemperature,
            var value when value.Contains("gpu", StringComparison.Ordinal) =>
                WellKnownMetrics.GpuUtilization,
            var value when value.Contains("packet", StringComparison.Ordinal) ||
                           value.Contains("loss", StringComparison.Ordinal) ||
                           value.Contains("network", StringComparison.Ordinal) =>
                WellKnownMetrics.NetworkPacketLoss,
            var value when value.Contains("memory", StringComparison.Ordinal) ||
                           value.Contains("ram", StringComparison.Ordinal) =>
                WellKnownMetrics.MemoryUtilization,
            _ => new MetricId(
                "custom.alert." + MetricSegment(rule.Id, "unresolved"))
        };
        var isKnown = !metric.Value.StartsWith(
            "custom.alert.",
            StringComparison.Ordinal);

        return rule with
        {
            MetricId = metric,
            Enabled = isKnown && rule.Enabled
        };
    }

    private static (MetricId? Primary, MetricId? Secondary) KnownModuleMetrics(
        string id,
        string title)
    {
        var normalized = $"{id} {title}".ToLowerInvariant();
        if (normalized.Contains("latency", StringComparison.Ordinal) ||
            normalized.Contains("ping", StringComparison.Ordinal))
        {
            return (
                WellKnownMetrics.NetworkPing,
                WellKnownMetrics.NetworkPacketLoss);
        }

        if (normalized.Contains("cpu", StringComparison.Ordinal))
        {
            return (
                WellKnownMetrics.CpuTotalUtilization,
                WellKnownMetrics.CpuTemperature);
        }

        if (normalized.Contains("gpu", StringComparison.Ordinal))
        {
            return (
                WellKnownMetrics.GpuUtilization,
                WellKnownMetrics.GpuTemperature);
        }

        if (normalized.Contains("memory", StringComparison.Ordinal) ||
            normalized.Contains("ram", StringComparison.Ordinal))
        {
            return (
                WellKnownMetrics.MemoryUsedBytes,
                WellKnownMetrics.MemoryUtilization);
        }

        if (normalized.Contains("network", StringComparison.Ordinal) ||
            normalized.Contains("net", StringComparison.Ordinal))
        {
            return (
                WellKnownMetrics.NetworkDownloadRate,
                WellKnownMetrics.NetworkUploadRate);
        }

        if (normalized.Contains("battery", StringComparison.Ordinal) ||
            normalized.Contains("power", StringComparison.Ordinal))
        {
            return (
                WellKnownMetrics.BatteryCharge,
                WellKnownMetrics.BatteryRemaining);
        }

        if (normalized.Contains("storage", StringComparison.Ordinal) ||
            normalized.Contains("disk", StringComparison.Ordinal))
        {
            return (
                new MetricId("storage.disk.activity"),
                new MetricId("storage.disk.free"));
        }

        if (normalized.Contains("fps", StringComparison.Ordinal) ||
            normalized.Contains("frame", StringComparison.Ordinal))
        {
            return (
                new MetricId("gaming.fps"),
                new MetricId("gaming.frame_time"));
        }

        return (null, null);
    }

    private static string MetricSegment(string value, string fallback)
    {
        var characters = value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character)
                ? character
                : '.')
            .ToArray();
        var segment = new string(characters).Trim('.');
        while (segment.Contains("..", StringComparison.Ordinal))
        {
            segment = segment.Replace("..", ".", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(segment) ? fallback : segment;
    }

    private static void EnsureUnique(IEnumerable<string> values, string noun)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException($"A {noun} id is empty.");
            }

            if (!seen.Add(value))
            {
                throw new InvalidDataException($"Duplicate {noun} id '{value}'.");
            }
        }
    }
}
