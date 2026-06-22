#nullable enable
using System.Text;
using System.Threading;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Velopack.Packaging.Abstractions;

namespace Velopack.Vpk.Telemetry;

public sealed class VpkTelemetry : IDisposable
{
    private const string EventName = "vpk.command";
    private const string FeatureEventName = "vpk.feature";
    private const int MaxFileSizeBytes = 50 * 1024 * 1024;
    public const string VpkConnectionStringConfigKey = "TELEMETRY_CONNECTION_STRING";
    public const string AppInsightsConnectionStringConfigKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    // These marker values are what each SDK should embed for language identification.
    private static readonly IReadOnlyDictionary<string, string> LanguageMarkerMap = new Dictionary<string, string>(StringComparer.Ordinal) {
        ["csharp"] = "VELOPACK_SDK_LANGUAGE_CSHARP",
        ["cpp"] = "VELOPACK_SDK_LANGUAGE_CPP",
        ["nodejs"] = "VELOPACK_SDK_LANGUAGE_NODEJS",
        ["python"] = "VELOPACK_SDK_LANGUAGE_PYTHON",
        ["rust"] = "VELOPACK_SDK_LANGUAGE_RUST",
    };

    private readonly ILogger<VpkTelemetry> _logger;
    private readonly TelemetryClient? _telemetryClient;
    private readonly TelemetryConfiguration? _telemetryConfiguration;
    public bool IsConfigured => _telemetryClient != null;

    public VpkTelemetry(ILogger<VpkTelemetry> logger, IConfiguration configuration)
    {
        _logger = logger;
        var connectionString = ResolveConnectionString(configuration);
        if (String.IsNullOrWhiteSpace(connectionString)) {
            return;
        }

        _telemetryConfiguration = TelemetryConfiguration.CreateDefault();
        _telemetryConfiguration.ConnectionString = connectionString;
        _telemetryClient = new TelemetryClient(_telemetryConfiguration);
    }

    public void TrackCommandInvocation(ParseResult parseResult, VelopackDefaults defaults, object options, bool succeeded)
    {
        if (!defaults.TelemetryEnabled || _telemetryClient == null) {
            return;
        }

        var features = GetExplicitFeatureNames(parseResult);
        var properties = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["command"] = GetCommandPath(parseResult),
            ["build_os"] = VelopackRuntimeInfo.SystemOs.GetOsShortName(),
            ["target_os"] = defaults.TargetOs.GetOsShortName(),
            ["features"] = String.Join(",", features),
            ["succeeded"] = succeeded ? "true" : "false",
            ["cli_version"] = VelopackRuntimeInfo.VelopackDisplayVersion,
        };

        if (options is IPlatformOptions platformOptions && platformOptions.TargetRuntime != null) {
            properties["target_rid"] = platformOptions.TargetRuntime.ToString();
            properties["target_arch"] = platformOptions.TargetRuntime.Architecture.ToString();
        }

        if (options is INugetPackCommand packOptions) {
            var languages = DetectLanguagesFromPackDirectory(packOptions.PackDirectory, _logger);
            properties["languages"] = String.Join(",", languages);
        }

        var metrics = new Dictionary<string, double> {
            ["feature_count"] = features.Count,
        };

        _telemetryClient.TrackEvent(EventName, properties, metrics);

        foreach (string feature in features) {
            _telemetryClient.TrackEvent(FeatureEventName, new Dictionary<string, string>(properties, StringComparer.Ordinal) {
                ["feature"] = feature,
            });
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_telemetryClient == null) {
            return;
        }

        await _telemetryClient.FlushAsync(cancellationToken);
    }

    public void Dispose()
    {
        _telemetryConfiguration?.Dispose();
    }

    public static IReadOnlyList<string> GetExplicitFeatureNames(ParseResult parseResult)
    {
        var features = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CommandResult? commandResult = parseResult.CommandResult;
        while (commandResult != null) {
            foreach (var option in commandResult.Command.Options) {
                var optionResult = parseResult.GetResult(option);
                if (optionResult != null && !optionResult.Implicit) {
                    features.Add(option.Name);
                }
            }

            commandResult = commandResult.Parent as CommandResult;
        }

        return features.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<string> DetectLanguagesFromPackDirectory(string? packDirectory, ILogger? logger = null)
    {
        if (String.IsNullOrWhiteSpace(packDirectory) || !Directory.Exists(packDirectory)) {
            return Array.Empty<string>();
        }

        var markers = LanguageMarkerMap.ToDictionary(x => x.Key, x => Encoding.UTF8.GetBytes(x.Value), StringComparer.Ordinal);
        int maxMarkerLength = markers.Values.Max(x => x.Length);
        var pending = new HashSet<string>(markers.Keys, StringComparer.Ordinal);
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var filePath in Directory.EnumerateFiles(packDirectory, "*", new EnumerationOptions {
                     RecurseSubdirectories = true,
                     IgnoreInaccessible = true,
                     AttributesToSkip = FileAttributes.ReparsePoint
                 })) {
            if (pending.Count == 0) {
                break;
            }

            try {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length <= 0 || fileInfo.Length > MaxFileSizeBytes) {
                    continue;
                }

                FindMarkersInFile(filePath, markers, pending, found, maxMarkerLength);
            } catch (IOException ex) {
                logger?.LogDebug(ex, "Skipping telemetry language scan for inaccessible file '{Path}'.", filePath);
            } catch (UnauthorizedAccessException ex) {
                logger?.LogDebug(ex, "Skipping telemetry language scan for inaccessible file '{Path}'.", filePath);
            }
        }

        return found.Order(StringComparer.Ordinal).ToArray();
    }

    private static string GetCommandPath(ParseResult parseResult)
    {
        var names = new Stack<string>();
        CommandResult? current = parseResult.CommandResult;
        while (current != null) {
            names.Push(current.Command.Name);
            current = current.Parent as CommandResult;
        }

        return String.Join(" ", names);
    }

    private static string? ResolveConnectionString(IConfiguration configuration)
    {
        return configuration[VpkConnectionStringConfigKey]
            ?? configuration[AppInsightsConnectionStringConfigKey]
            ?? Environment.GetEnvironmentVariable(AppInsightsConnectionStringConfigKey);
    }

    private static void FindMarkersInFile(
        string filePath,
        IReadOnlyDictionary<string, byte[]> markers,
        HashSet<string> pending,
        HashSet<string> found,
        int maxMarkerLength)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        byte[] buffer = new byte[8192];
        byte[] tail = new byte[Math.Max(0, maxMarkerLength - 1)];
        int tailLength = 0;

        while (pending.Count > 0) {
            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            if (bytesRead == 0) {
                break;
            }

            byte[] searchBuffer = new byte[tailLength + bytesRead];
            if (tailLength > 0) {
                Buffer.BlockCopy(tail, 0, searchBuffer, 0, tailLength);
            }

            Buffer.BlockCopy(buffer, 0, searchBuffer, tailLength, bytesRead);
            foreach (var language in pending.ToArray()) {
                if (searchBuffer.AsSpan().IndexOf(markers[language]) >= 0) {
                    found.Add(language);
                    pending.Remove(language);
                }
            }

            tailLength = Math.Min(tail.Length, searchBuffer.Length);
            if (tailLength > 0) {
                Buffer.BlockCopy(searchBuffer, searchBuffer.Length - tailLength, tail, 0, tailLength);
            }
        }
    }
}
