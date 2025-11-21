using Microsoft.Build.Utilities;
using MSBuildTask = Microsoft.Build.Utilities.Task;

namespace Velopack.Build.Tests;

/// <summary>
/// Integration tests for VPK tool resolution and installation
/// These tests require dotnet CLI to be available
/// </summary>
public class VpkToolResolverIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public VpkToolResolverIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Skip = "Integration test - requires dotnet CLI and network access")]
    public async System.Threading.Tasks.Task ResolveToolAsync_WithAutoMode_ShouldResolveOrInstallTool()
    {
        // Arrange
        var loggingHelper = new TaskLoggingHelper(new MockBuildEngine(), "TestTask");
        var resolver = new VpkToolResolver(loggingHelper);
        var config = new VpkToolConfiguration
        {
            Mode = VpkToolConfiguration.ToolMode.Auto,
            SkipInstall = false,
            AllowPrerelease = true // Allow prerelease for testing
        };

        // Act
        var result = await resolver.ResolveToolAsync(config, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Version);
        Assert.NotEmpty(result.Version);
        _output.WriteLine($"Resolved VPK tool: {result.Version} ({(result.IsLocal ? "local" : "global")})");
    }

    [Fact(Skip = "Integration test - requires dotnet CLI")]
    public async System.Threading.Tasks.Task ResolveToolAsync_WithSkipInstall_ShouldFailIfNotInstalled()
    {
        // Arrange
        var loggingHelper = new TaskLoggingHelper(new MockBuildEngine(), "TestTask");
        var resolver = new VpkToolResolver(loggingHelper);
        var config = new VpkToolConfiguration
        {
            Mode = VpkToolConfiguration.ToolMode.Local,
            SkipInstall = true,
            Version = "999.999.999" // Non-existent version
        };

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(async () =>
        {
            await resolver.ResolveToolAsync(config, CancellationToken.None);
        });
    }

    [Fact(Skip = "Integration test - requires dotnet CLI")]
    public async System.Threading.Tasks.Task ResolveToolAsync_WithSpecificVersion_ShouldResolveCorrectVersion()
    {
        // Arrange
        var loggingHelper = new TaskLoggingHelper(new MockBuildEngine(), "TestTask");
        var resolver = new VpkToolResolver(loggingHelper);
        var config = new VpkToolConfiguration
        {
            Mode = VpkToolConfiguration.ToolMode.Auto,
            Version = "0.0.1369-g1d5c984", // A known version from NuGet
            SkipInstall = false,
            AllowPrerelease = true
        };

        // Act
        var result = await resolver.ResolveToolAsync(config, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("0.0.1369-g1d5c984", result.Version);
        _output.WriteLine($"Resolved VPK tool: {result.Version} ({(result.IsLocal ? "local" : "global")})");
    }

    [Fact(Skip = "Integration test - requires dotnet CLI")]
    public async System.Threading.Tasks.Task DotNetToolRunner_GetInstalledVersion_ShouldReturnNullForNonExistent()
    {
        // Arrange
        var loggingHelper = new TaskLoggingHelper(new MockBuildEngine(), "TestTask");
        var runner = new DotNetToolRunner(loggingHelper);

        // Act
        var version = await runner.GetInstalledVersionAsync(
            "non-existent-tool-12345",
            isLocal: true,
            Environment.CurrentDirectory,
            CancellationToken.None);

        // Assert
        Assert.Null(version);
    }
}

/// <summary>
/// Helper class for creating test environments
/// </summary>
public class TestHelper
{
    /// <summary>
    /// Creates a temporary directory for testing
    /// </summary>
    public static string CreateTempDirectory()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "VelopackBuildTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);
        return tempPath;
    }

    /// <summary>
    /// Cleans up a temporary directory
    /// </summary>
    public static void CleanupTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    /// <summary>
    /// Creates a minimal test publish directory structure
    /// </summary>
    public static string CreateTestPublishDirectory()
    {
        var tempDir = CreateTempDirectory();
        
        // Create a simple executable file
        var exePath = Path.Combine(tempDir, "TestApp.exe");
        File.WriteAllText(exePath, "Test executable content");
        
        // Create a dll
        var dllPath = Path.Combine(tempDir, "TestApp.dll");
        File.WriteAllText(dllPath, "Test dll content");
        
        return tempDir;
    }
}
