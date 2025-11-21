using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Velopack.Build;

/// <summary>
/// Resolves and ensures VPK tool is available
/// </summary>
public class VpkToolResolver
{
    private readonly TaskLoggingHelper _log;
    private readonly DotNetToolRunner _toolRunner;

    public VpkToolResolver(TaskLoggingHelper log)
    {
        _log = log;
        _toolRunner = new DotNetToolRunner(log);
    }

    /// <summary>
    /// Get the default tool version from Velopack.Build assembly
    /// </summary>
    private string GetDefaultToolVersion()
    {
        var assembly = typeof(PackTask).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        
        if (version != null)
        {
            // Remove git commit hash if present (e.g., "1.2.3+abc123" -> "1.2.3")
            var plusIndex = version.IndexOf('+');
            if (plusIndex >= 0)
            {
                version = version.Substring(0, plusIndex);
            }
            return version;
        }

        version = assembly.GetName().Version?.ToString();
        
        if (version == null)
        {
            throw new Exception("Could not determine Velopack.Build version");
        }

        return version;
    }

    /// <summary>
    /// Resolve the VPK tool and ensure it's installed
    /// </summary>
    public async Task<ResolvedTool> ResolveToolAsync(VpkToolConfiguration config, CancellationToken cancellationToken)
    {
        // Determine target version
        string targetVersion = config.Version ?? GetDefaultToolVersion();
        _log.LogMessage(MessageImportance.Normal, $"Target VPK tool version: {targetVersion}");

        // Try to resolve based on mode
        ResolvedTool? resolved = null;

        switch (config.Mode)
        {
            case VpkToolConfiguration.ToolMode.Local:
                resolved = await ResolveLocalToolAsync(targetVersion, config, cancellationToken);
                break;
            
            case VpkToolConfiguration.ToolMode.Global:
                resolved = await ResolveGlobalToolAsync(targetVersion, config, cancellationToken);
                break;
            
            case VpkToolConfiguration.ToolMode.Auto:
                // Try local first, then global
                resolved = await ResolveLocalToolAsync(targetVersion, config, cancellationToken);
                if (resolved == null)
                {
                    _log.LogMessage(MessageImportance.Low, "Local tool not found, trying global...");
                    resolved = await ResolveGlobalToolAsync(targetVersion, config, cancellationToken);
                }
                break;
        }

        if (resolved == null)
        {
            throw new Exception($"Failed to resolve VPK tool. Mode: {config.Mode}, SkipInstall: {config.SkipInstall}");
        }

        _log.LogMessage(MessageImportance.High, $"Using VPK tool: {resolved.Version} ({(resolved.IsLocal ? "local" : "global")})");
        return resolved;
    }

    private async Task<ResolvedTool?> ResolveLocalToolAsync(
        string targetVersion,
        VpkToolConfiguration config,
        CancellationToken cancellationToken)
    {
        var installedVersion = await _toolRunner.GetInstalledVersionAsync(
            "vpk",
            isLocal: true,
            config.WorkingDirectory,
            cancellationToken);

        if (installedVersion != null)
        {
            _log.LogMessage(MessageImportance.Low, $"Found local VPK tool version: {installedVersion}");
            
            if (installedVersion != targetVersion)
            {
                _log.LogWarning($"Local VPK tool version mismatch: expected {targetVersion}, found {installedVersion}");
                
                if (!config.SkipInstall)
                {
                    // Update to target version
                    var updated = await _toolRunner.UpdateToolAsync(
                        "vpk",
                        targetVersion,
                        isLocal: true,
                        config.AllowPrerelease,
                        config.Source,
                        config.WorkingDirectory,
                        cancellationToken);
                    
                    if (updated)
                    {
                        return new ResolvedTool(isLocal: true, targetVersion);
                    }
                }
            }
            
            return new ResolvedTool(isLocal: true, installedVersion);
        }

        // Tool not installed locally
        if (!config.SkipInstall)
        {
            var installed = await _toolRunner.InstallToolAsync(
                "vpk",
                targetVersion,
                isLocal: true,
                config.AllowPrerelease,
                config.Source,
                config.WorkingDirectory,
                cancellationToken);

            if (installed)
            {
                return new ResolvedTool(isLocal: true, targetVersion);
            }
        }

        return null;
    }

    private async Task<ResolvedTool?> ResolveGlobalToolAsync(
        string targetVersion,
        VpkToolConfiguration config,
        CancellationToken cancellationToken)
    {
        var installedVersion = await _toolRunner.GetInstalledVersionAsync(
            "vpk",
            isLocal: false,
            config.WorkingDirectory,
            cancellationToken);

        if (installedVersion != null)
        {
            _log.LogMessage(MessageImportance.Low, $"Found global VPK tool version: {installedVersion}");
            
            if (installedVersion != targetVersion)
            {
                _log.LogWarning($"Global VPK tool version mismatch: expected {targetVersion}, found {installedVersion}");
                
                if (!config.SkipInstall)
                {
                    // Update to target version
                    var updated = await _toolRunner.UpdateToolAsync(
                        "vpk",
                        targetVersion,
                        isLocal: false,
                        config.AllowPrerelease,
                        config.Source,
                        config.WorkingDirectory,
                        cancellationToken);
                    
                    if (updated)
                    {
                        return new ResolvedTool(isLocal: false, targetVersion);
                    }
                }
            }
            
            return new ResolvedTool(isLocal: false, installedVersion);
        }

        // Tool not installed globally
        if (!config.SkipInstall)
        {
            var installed = await _toolRunner.InstallToolAsync(
                "vpk",
                targetVersion,
                isLocal: false,
                config.AllowPrerelease,
                config.Source,
                config.WorkingDirectory,
                cancellationToken);

            if (installed)
            {
                return new ResolvedTool(isLocal: false, targetVersion);
            }
        }

        return null;
    }
}
