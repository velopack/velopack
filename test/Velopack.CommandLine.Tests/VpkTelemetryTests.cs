using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Velopack.Vpk;
using Velopack.Vpk.Telemetry;

namespace Velopack.CommandLine.Tests;

public class VpkTelemetryTests : TempFileTestBase
{
    [Fact]
    public void GetExplicitFeatureNames_ReturnsOnlyExplicitOptionNames()
    {
        var command = new Command("pack");
        var verboseOption = new Option<bool>("--verbose");
        var signParamsOption = new Option<string>("--signParams");
        command.Options.Add(verboseOption);
        command.Options.Add(signParamsOption);

        var parseResult = command.Parse("--verbose --signParams secret-value");
        var features = VpkTelemetry.GetExplicitFeatureNames(parseResult);

        Assert.Contains("--verbose", features);
        Assert.Contains("--signParams", features);
        Assert.DoesNotContain("secret-value", features);
    }

    [Fact]
    public void GetExplicitFeatureNames_ExcludesImplicitDefaults()
    {
        var command = new Command("pack");
        var verboseOption = new Option<bool>("--verbose");
        command.Options.Add(verboseOption);

        var parseResult = command.Parse("");
        var features = VpkTelemetry.GetExplicitFeatureNames(parseResult);

        Assert.DoesNotContain("--verbose", features);
    }

    [Fact]
    public void DetectLanguagesFromPackDirectory_FindsLanguageMarkers()
    {
        var packDir = CreateTempDirectory();
        var markerFile = CreateTempFile(packDir, "app.bin");
        File.WriteAllText(markerFile.FullName, "header VELOPACK_SDK_LANGUAGE_NODEJS footer");

        var languages = VpkTelemetry.DetectLanguagesFromPackDirectory(packDir.FullName);

        Assert.Equal(new[] { "nodejs" }, languages);
    }

    [Fact]
    public async Task TrackCommandInvocation_WithTelemetryDisabled_DoesNotThrow()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<VpkTelemetry>();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var telemetry = new VpkTelemetry(logger, configuration);
        var defaults = new VelopackDefaults(false, RuntimeOs.Windows, skipUpdates: true, telemetryEnabled: false);

        var command = new Command("pack");
        command.Options.Add(new Option<bool>("--verbose"));
        var parseResult = command.Parse("--verbose");

        telemetry.TrackCommandInvocation(parseResult, defaults, new object(), succeeded: true);
        await telemetry.FlushAsync(CancellationToken.None);
    }
}
