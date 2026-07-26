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

public sealed class UnsupportedSettingsSchemaException : InvalidOperationException
{
    public UnsupportedSettingsSchemaException(
        long schemaVersion,
        int supportedSchemaVersion)
        : base(
            $"Settings schema {schemaVersion} is newer than the supported schema " +
            $"{supportedSchemaVersion}. The file is read-only in this version to prevent data loss.")
    {
        SchemaVersion = schemaVersion;
        SupportedSchemaVersion = supportedSchemaVersion;
    }

    public long SchemaVersion { get; }

    public int SupportedSchemaVersion { get; }
}

public sealed class JsonSettingsRepository : ISettingsRepository, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CrossProcessFileLock _writeLock;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    public JsonSettingsRepository(string? settingsPath = null)
    {
        SettingsPath = settingsPath ?? GetDefaultSettingsPath();
        _writeLock = new CrossProcessFileLock(SettingsPath);
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
            return await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
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
        try
        {
            await using var writeLease = await _writeLock
                .AcquireAsync(cancellationToken)
                .ConfigureAwait(false);
            _ = await LoadCoreAsync(
                    cancellationToken,
                    rejectFutureSchema: true)
                .ConfigureAwait(false);
            await SaveCoreAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OpsSettingsDocument> UpdateAsync(
        Func<OpsSettingsDocument, OpsSettingsDocument> update,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(update);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var writeLease = await _writeLock
                .AcquireAsync(cancellationToken)
                .ConfigureAwait(false);
            var current = await LoadCoreAsync(
                    cancellationToken,
                    rejectFutureSchema: true)
                .ConfigureAwait(false);
            var updated = NormalizeAndValidate(update(current)) with
            {
                SchemaVersion = OpsSettingsDocument.CurrentSchemaVersion,
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            await SaveCoreAsync(updated, cancellationToken).ConfigureAwait(false);
            return updated;
        }
        finally
        {
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

    private async Task<OpsSettingsDocument> LoadCoreAsync(
        CancellationToken cancellationToken,
        bool rejectFutureSchema = false)
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
                throw new UnsupportedSettingsSchemaException(
                    schemaVersion,
                    OpsSettingsDocument.CurrentSchemaVersion);
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
        catch (UnsupportedSettingsSchemaException exception) when (!rejectFutureSchema)
        {
            LastLoadWarning =
                $"Settings could not be loaded; defaults are active. {exception.Message}";
            return OpsSettingsDocument.CreateDefault();
        }
        catch (Exception exception) when (
            exception is JsonException or
            IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            LastLoadWarning =
                $"Settings could not be loaded; defaults are active. {exception.Message}";
            return OpsSettingsDocument.CreateDefault();
        }
    }

    private async Task SaveCoreAsync(
        OpsSettingsDocument settings,
        CancellationToken cancellationToken)
    {
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
        }
    }

    private static long ReadSchemaVersion(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The settings root must be a JSON object.");
        }

        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals("schemaVersion", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind != JsonValueKind.Number ||
                    !property.Value.TryGetInt64(out var schemaVersion))
                {
                    throw new InvalidDataException(
                        "The settings schema version must be an integer.");
                }

                return schemaVersion;
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

        _ = Require(settings.General, "general");
        var widgets = RequireCollection(settings.Widgets, "widgets");
        var themes = RequireCollection(settings.Themes, "themes");
        var scenes = RequireCollection(settings.Scenes, "scenes");
        var profiles = RequireCollection(
            settings.PerformanceProfiles,
            "performanceProfiles");
        var hotkeys = RequireCollection(settings.Hotkeys, "hotkeys");
        var dataRetention = Require(settings.DataRetention, "dataRetention");
        var alertRules = RequireCollection(settings.AlertRules, "alertRules");

        ValidateWidgets(widgets);
        ValidateThemes(themes);
        ValidateScenes(scenes);
        ValidateProfiles(profiles);
        ValidateHotkeys(hotkeys);
        ValidateAlertRules(alertRules);

        if (dataRetention.MaximumSamplesPerMetric < 2)
        {
            throw new InvalidDataException(
                "Data retention must allow at least two samples per metric.");
        }

        if (dataRetention.Retention <= TimeSpan.Zero)
        {
            throw new InvalidDataException("Data retention duration must be positive.");
        }

        EnsureUnique(widgets.Select(item => item.Id), "widget");
        EnsureUnique(themes.Select(item => item.Id), "theme");
        EnsureUnique(scenes.Select(item => item.Id), "scene");
        EnsureUnique(profiles.Select(item => item.Id), "profile");
        EnsureUnique(hotkeys.Select(item => item.Id), "hotkey");
        EnsureUnique(alertRules.Select(item => item.Id), "alert rule");

        var repairedWidgets = widgets
            .Select(widget => widget with
            {
                Modules = widget.Modules
                    .Select(RepairModuleMetricIds)
                    .ToList()
            })
            .ToList();
        var repairedAlerts = alertRules
            .Select(RepairAlertMetricId)
            .ToList();

        return settings with
        {
            SchemaVersion = OpsSettingsDocument.CurrentSchemaVersion,
            Widgets = repairedWidgets,
            AlertRules = repairedAlerts
        };
    }

    private static void ValidateWidgets(List<WidgetInstanceSettings> widgets)
    {
        for (var widgetIndex = 0; widgetIndex < widgets.Count; widgetIndex++)
        {
            var widget = widgets[widgetIndex];
            var path = $"widgets[{widgetIndex}]";
            RequireText(widget.Id, $"{path}.id");
            RequireText(widget.Name, $"{path}.name");
            RequireText(widget.ThemeId, $"{path}.themeId");
            RequireText(
                widget.PerformanceProfileId,
                $"{path}.performanceProfileId");
            _ = Require(widget.Window, $"{path}.window");
            RequireText(widget.Window.MonitorId, $"{path}.window.monitorId");

            var modules = RequireCollection(widget.Modules, $"{path}.modules");
            for (var moduleIndex = 0; moduleIndex < modules.Count; moduleIndex++)
            {
                var module = modules[moduleIndex];
                var modulePath = $"{path}.modules[{moduleIndex}]";
                RequireText(module.Id, $"{modulePath}.id");
                RequireText(module.Title, $"{modulePath}.title");
                RequireText(module.Icon, $"{modulePath}.icon");
                RequireText(module.AccentColor, $"{modulePath}.accentColor");
                _ = RequireCollection(
                    module.AdditionalMetrics,
                    $"{modulePath}.additionalMetrics");
            }
        }
    }

    private static void ValidateThemes(List<ThemeSettings> themes)
    {
        for (var index = 0; index < themes.Count; index++)
        {
            var theme = themes[index];
            var path = $"themes[{index}]";
            RequireText(theme.Id, $"{path}.id");
            RequireText(theme.Name, $"{path}.name");
            var palette = Require(theme.Palette, $"{path}.palette");
            _ = Require(theme.Surface, $"{path}.surface");
            var typography = Require(theme.Typography, $"{path}.typography");
            _ = Require(theme.Motion, $"{path}.motion");

            RequireText(palette.Background, $"{path}.palette.background");
            RequireText(palette.Card, $"{path}.palette.card");
            RequireText(palette.Border, $"{path}.palette.border");
            RequireText(palette.PrimaryText, $"{path}.palette.primaryText");
            RequireText(palette.SecondaryText, $"{path}.palette.secondaryText");
            RequireText(palette.CpuAccent, $"{path}.palette.cpuAccent");
            RequireText(palette.GpuAccent, $"{path}.palette.gpuAccent");
            RequireText(
                palette.NetworkAccent,
                $"{path}.palette.networkAccent");
            RequireText(palette.Warning, $"{path}.palette.warning");
            RequireText(palette.Critical, $"{path}.palette.critical");
            RequireText(palette.Success, $"{path}.palette.success");
            RequireText(
                typography.FontFamily,
                $"{path}.typography.fontFamily");
        }
    }

    private static void ValidateScenes(List<SceneSettings> scenes)
    {
        for (var index = 0; index < scenes.Count; index++)
        {
            var scene = scenes[index];
            var path = $"scenes[{index}]";
            RequireText(scene.Id, $"{path}.id");
            RequireText(scene.Name, $"{path}.name");
            RequireText(
                scene.PerformanceProfileId,
                $"{path}.performanceProfileId");
            var widgetIds = RequireCollection(
                scene.WidgetIds,
                $"{path}.widgetIds");
            ValidateTextItems(widgetIds, $"{path}.widgetIds");

            var activation = Require(scene.Activation, $"{path}.activation");
            var processNames = RequireCollection(
                activation.ProcessNames,
                $"{path}.activation.processNames");
            ValidateTextItems(
                processNames,
                $"{path}.activation.processNames");
            _ = RequireCollection(
                activation.Days,
                $"{path}.activation.days");
        }
    }

    private static void ValidateProfiles(
        List<PerformanceProfileSettings> profiles)
    {
        for (var index = 0; index < profiles.Count; index++)
        {
            var profile = profiles[index];
            var path = $"performanceProfiles[{index}]";
            RequireText(profile.Id, $"{path}.id");
            RequireText(profile.Name, $"{path}.name");
            _ = Require(
                profile.ProviderCadences,
                $"{path}.providerCadences");
            var disabledProviderIds = Require(
                profile.DisabledProviderIds,
                $"{path}.disabledProviderIds");
            ValidateTextItems(
                disabledProviderIds,
                $"{path}.disabledProviderIds");
        }
    }

    private static void ValidateHotkeys(List<HotkeySettings> hotkeys)
    {
        for (var index = 0; index < hotkeys.Count; index++)
        {
            var hotkey = hotkeys[index];
            var path = $"hotkeys[{index}]";
            RequireText(hotkey.Id, $"{path}.id");
            RequireText(hotkey.Key, $"{path}.key");
        }
    }

    private static void ValidateAlertRules(List<AlertRule> alertRules)
    {
        for (var index = 0; index < alertRules.Count; index++)
        {
            var alertRule = alertRules[index];
            var path = $"alertRules[{index}]";
            RequireText(alertRule.Id, $"{path}.id");
            RequireText(alertRule.Name, $"{path}.name");
        }
    }

    private static List<T> RequireCollection<T>(
        List<T>? values,
        string path)
    {
        if (values is null)
        {
            throw new InvalidDataException(
                $"The required settings member '{path}' was null.");
        }

        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] is null)
            {
                throw new InvalidDataException(
                    $"The required settings member '{path}[{index}]' was null.");
            }
        }

        return values;
    }

    private static T Require<T>(T? value, string path)
        where T : class
    {
        return value ?? throw new InvalidDataException(
            $"The required settings member '{path}' was null.");
    }

    private static void RequireText(string? value, string path)
    {
        if (value is null)
        {
            throw new InvalidDataException(
                $"The required settings member '{path}' was null.");
        }
    }

    private static void ValidateTextItems(
        IEnumerable<string> values,
        string path)
    {
        var index = 0;
        foreach (var value in values)
        {
            RequireText(value, $"{path}[{index}]");
            index++;
        }
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
