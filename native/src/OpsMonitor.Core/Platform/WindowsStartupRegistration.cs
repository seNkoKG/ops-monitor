using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace OpsMonitor.Core.Platform;

public sealed record WindowsStartupRegistrationState
{
    public bool IsRegistered { get; init; }
    public bool IsWellFormed { get; init; }
    public string? CommandLine { get; init; }
    public string? ExecutablePath { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public RegistryValueKind? RegistryValueKind { get; init; }
}

/// <summary>
/// Manages a single per-user Windows sign-in command in
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsStartupRegistration
{
    public const int MaximumRunCommandLength = 260;
    public const string RunRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    public WindowsStartupRegistration(string valueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        if (valueName.Contains('\0'))
        {
            throw new ArgumentException(
                "The startup value name cannot contain a null character.",
                nameof(valueName));
        }

        ValueName = valueName.Trim();
    }

    public string ValueName { get; }

    /// <summary>
    /// Creates or replaces the registration and returns the value read back from
    /// the registry. Each argument is encoded using CommandLineToArgvW-compatible
    /// quoting.
    /// </summary>
    public WindowsStartupRegistrationState Register(
        string executablePath,
        IEnumerable<string>? arguments = null,
        bool requireExecutableExists = true)
    {
        var normalizedPath = NormalizeExecutablePath(
            executablePath,
            requireExecutableExists);
        var materializedArguments = MaterializeArguments(arguments);
        var commandLine = BuildCommandLine(normalizedPath, materializedArguments);
        if (commandLine.Length > MaximumRunCommandLength)
        {
            throw new ArgumentException(
                $"The Windows Run command cannot exceed {MaximumRunCommandLength} characters.",
                nameof(arguments));
        }

        using var currentUser = RegistryKey.OpenBaseKey(
            RegistryHive.CurrentUser,
            RegistryView.Default);
        using var runKey = currentUser.CreateSubKey(
            RunRegistryPath,
            writable: true) ??
            throw new IOException(
                $@"Unable to open HKCU\{RunRegistryPath} for writing.");
        runKey.SetValue(ValueName, commandLine, RegistryValueKind.String);
        runKey.Flush();

        return Query();
    }

    /// <summary>
    /// Reads the current registration. A present but non-string or unparsable
    /// registry value is reported as registered but not well formed.
    /// </summary>
    public WindowsStartupRegistrationState Query()
    {
        using var currentUser = RegistryKey.OpenBaseKey(
            RegistryHive.CurrentUser,
            RegistryView.Default);
        using var runKey = currentUser.OpenSubKey(RunRegistryPath, writable: false);
        if (runKey is null ||
            !runKey.GetValueNames().Contains(ValueName, StringComparer.OrdinalIgnoreCase))
        {
            return new WindowsStartupRegistrationState();
        }

        var valueKind = runKey.GetValueKind(ValueName);
        var rawValue = runKey.GetValue(
            ValueName,
            defaultValue: null,
            RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (rawValue is not string commandLine ||
            string.IsNullOrWhiteSpace(commandLine))
        {
            return new WindowsStartupRegistrationState
            {
                IsRegistered = true,
                RegistryValueKind = valueKind
            };
        }

        if (!TryParseCommandLine(commandLine, out var parts) || parts.Count == 0)
        {
            return new WindowsStartupRegistrationState
            {
                IsRegistered = true,
                CommandLine = commandLine,
                RegistryValueKind = valueKind
            };
        }

        return new WindowsStartupRegistrationState
        {
            IsRegistered = true,
            IsWellFormed = true,
            CommandLine = commandLine,
            ExecutablePath = parts[0],
            Arguments = parts.Skip(1).ToArray(),
            RegistryValueKind = valueKind
        };
    }

    /// <summary>
    /// Returns true only when the registered executable and every argument match
    /// the requested command after normalizing the executable path.
    /// </summary>
    public bool IsRegisteredFor(
        string executablePath,
        IEnumerable<string>? arguments = null)
    {
        var expectedPath = NormalizeExecutablePath(
            executablePath,
            requireExecutableExists: false);
        var expectedArguments = MaterializeArguments(arguments);
        var state = Query();
        if (!state.IsWellFormed || state.ExecutablePath is null)
        {
            return false;
        }

        string registeredPath;
        try
        {
            registeredPath = Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(state.ExecutablePath));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        return StringComparer.OrdinalIgnoreCase.Equals(expectedPath, registeredPath) &&
               expectedArguments.SequenceEqual(state.Arguments, StringComparer.Ordinal);
    }

    /// <summary>
    /// Removes only this instance's named Run value. Returns false when it was
    /// already absent.
    /// </summary>
    public bool Remove()
    {
        using var currentUser = RegistryKey.OpenBaseKey(
            RegistryHive.CurrentUser,
            RegistryView.Default);
        using var runKey = currentUser.OpenSubKey(RunRegistryPath, writable: true);
        if (runKey is null ||
            !runKey.GetValueNames().Contains(ValueName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        runKey.DeleteValue(ValueName, throwOnMissingValue: false);
        runKey.Flush();
        return true;
    }

    public static string BuildCommandLine(
        string executablePath,
        IEnumerable<string>? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var materializedArguments = MaterializeArguments(arguments);
        var command = new StringBuilder();
        command.Append(QuoteArgument(executablePath, alwaysQuote: true));
        foreach (var argument in materializedArguments)
        {
            command.Append(' ');
            command.Append(QuoteArgument(argument, alwaysQuote: false));
        }

        return command.ToString();
    }

    private static string NormalizeExecutablePath(
        string executablePath,
        bool requireExecutableExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (executablePath.Contains('\0'))
        {
            throw new ArgumentException(
                "The executable path cannot contain a null character.",
                nameof(executablePath));
        }

        var expanded = Environment.ExpandEnvironmentVariables(executablePath.Trim());
        var normalized = Path.GetFullPath(expanded);
        if (requireExecutableExists && !File.Exists(normalized))
        {
            throw new FileNotFoundException(
                "The startup executable does not exist.",
                normalized);
        }

        return normalized;
    }

    private static string[] MaterializeArguments(IEnumerable<string>? arguments)
    {
        if (arguments is null)
        {
            return [];
        }

        var materialized = arguments.ToArray();
        for (var index = 0; index < materialized.Length; index++)
        {
            if (materialized[index] is null)
            {
                throw new ArgumentException(
                    "Startup arguments cannot contain null values.",
                    nameof(arguments));
            }

            if (materialized[index].Contains('\0'))
            {
                throw new ArgumentException(
                    "Startup arguments cannot contain null characters.",
                    nameof(arguments));
            }
        }

        return materialized;
    }

    private static string QuoteArgument(string argument, bool alwaysQuote)
    {
        if (!alwaysQuote &&
            argument.Length > 0 &&
            !argument.Any(character => char.IsWhiteSpace(character) || character == '"'))
        {
            return argument;
        }

        var result = new StringBuilder(argument.Length + 2);
        result.Append('"');
        var backslashCount = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashCount++;
                continue;
            }

            if (character == '"')
            {
                result.Append('\\', (backslashCount * 2) + 1);
                result.Append('"');
                backslashCount = 0;
                continue;
            }

            result.Append('\\', backslashCount);
            backslashCount = 0;
            result.Append(character);
        }

        // Backslashes before the closing quote must be doubled.
        result.Append('\\', backslashCount * 2);
        result.Append('"');
        return result.ToString();
    }

    private static bool TryParseCommandLine(
        string commandLine,
        out IReadOnlyList<string> arguments)
    {
        var pointer = NativeMethods.CommandLineToArgvW(commandLine, out var argumentCount);
        if (pointer == 0)
        {
            arguments = [];
            return false;
        }

        try
        {
            var parsed = new string[argumentCount];
            for (var index = 0; index < argumentCount; index++)
            {
                var itemPointer = Marshal.ReadIntPtr(pointer, index * IntPtr.Size);
                parsed[index] = Marshal.PtrToStringUni(itemPointer) ?? string.Empty;
            }

            arguments = parsed;
            return true;
        }
        finally
        {
            _ = NativeMethods.LocalFree(pointer);
        }
    }

    private static class NativeMethods
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern nint CommandLineToArgvW(
            string commandLine,
            out int argumentCount);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern nint LocalFree(nint memory);
    }
}
