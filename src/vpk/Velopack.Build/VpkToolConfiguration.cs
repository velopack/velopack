using System;

namespace Velopack.Build;

/// <summary>
/// Configuration for VPK tool resolution and installation
/// </summary>
public class VpkToolConfiguration
{
    /// <summary>
    /// Tool installation mode
    /// </summary>
    public enum ToolMode
    {
        /// <summary>
        /// Automatically determine best mode (try local first, fallback to global)
        /// </summary>
        Auto,
        
        /// <summary>
        /// Use/install as local tool (creates .config/dotnet-tools.json)
        /// </summary>
        Local,
        
        /// <summary>
        /// Use/install as global tool
        /// </summary>
        Global
    }

    /// <summary>
    /// Gets or sets the tool mode (Auto, Local, or Global)
    /// </summary>
    public ToolMode Mode { get; set; } = ToolMode.Auto;

    /// <summary>
    /// Gets or sets the specific version to use. If null, uses the Velopack.Build version.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Gets or sets whether to allow prerelease versions
    /// </summary>
    public bool AllowPrerelease { get; set; }

    /// <summary>
    /// Gets or sets the NuGet source URL for the tool package
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Gets or sets whether to skip automatic tool installation
    /// </summary>
    public bool SkipInstall { get; set; }

    /// <summary>
    /// Gets or sets the working directory for tool operations
    /// </summary>
    public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;
}

/// <summary>
/// Information about a resolved VPK tool
/// </summary>
public class ResolvedTool
{
    /// <summary>
    /// Gets whether the tool is installed locally (vs globally)
    /// </summary>
    public bool IsLocal { get; }

    /// <summary>
    /// Gets the version of the resolved tool
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Gets the execution command prefix
    /// </summary>
    public string ExecutionPrefix { get; }

    public ResolvedTool(bool isLocal, string version)
    {
        IsLocal = isLocal;
        Version = version;
        ExecutionPrefix = isLocal ? "tool run vpk" : "vpk";
    }
}
