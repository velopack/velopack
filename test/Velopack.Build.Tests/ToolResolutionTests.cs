using System.Diagnostics;
using System.Xml.Linq;
using TaskLoggingHelper = Microsoft.Build.Utilities.TaskLoggingHelper;

namespace Velopack.Build.Tests;

public class ToolResolutionTests
{
    [Theory]
    [InlineData("1.3.68-pr.755.run.123", "", "1.3.68-pr.755.run.123")]
    [InlineData("1.3.68", "1.3.69-beta", "1.3.69-beta")]
    [InlineData("", "1.3.69", "1.3.69")]
    public async Task TargetsResolveExactLibraryVersionAndAllowOverride(string libraryVersion, string toolVersion, string expected)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"velopack-resolution-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try {
            var project = new XDocument(
                new XElement("Project",
                    new XElement("PropertyGroup",
                        new XElement("VelopackVpkVersion", toolVersion),
                        new XElement("VelopackVpkNugetSource", "https://example.test/feed"),
                        new XElement("VelopackPackDirectory", directory),
                        new XElement("VelopackReleaseDirectory", directory)),
                    new XElement("ItemGroup",
                        new XElement("ReferencePath", new XAttribute("Include", "Velopack.dll"),
                            new XElement("NuGetPackageId", "Velopack"),
                            new XElement("NuGetPackageVersion", libraryVersion))),
                    new XElement("Import", new XAttribute("Project", Path.Combine(AppContext.BaseDirectory, "Velopack.Build.targets"))),
                    new XElement("Target", new XAttribute("Name", "ResolveReferences")),
                    new XElement("Target", new XAttribute("Name", "Check"), new XAttribute("DependsOnTargets", "_VelopackResolveProperties"),
                        new XElement("WriteLinesToFile",
                            new XAttribute("File", Path.Combine(directory, "resolved.txt")),
                            new XAttribute("Lines", "$(_VelopackVpkVersion);$(_VelopackVpkNugetSource)")))));
            var projectPath = Path.Combine(directory, "test.proj");
            project.Save(projectPath);

            var startInfo = new ProcessStartInfo("dotnet") {
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[] { "msbuild", projectPath, "-t:Check", "-nologo", "-v:quiet" }) {
                startInfo.ArgumentList.Add(argument);
            }
            using var process = Process.Start(startInfo)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, await output + await error);
            Assert.Equal(new[] { expected, "https://example.test/feed" }, await File.ReadAllLinesAsync(Path.Combine(directory, "resolved.txt")));
        } finally {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void RunnerUsesProjectConfigAndExactVersion()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"velopack-runner-{Guid.NewGuid():N}");
        var projectDirectory = Path.Combine(directory, "src", "app");
        Directory.CreateDirectory(projectDirectory);
        try {
            var configPath = Path.Combine(directory, "NuGet.Config");
            File.WriteAllText(configPath, "<configuration />");
            var runner = new VpkToolRunner("1.3.68-pr.755.run.123", null, new TaskLoggingHelper(new PackTask())) {
                ProjectDirectory = projectDirectory,
            };

            var startInfo = runner.CreateStartInfo(["[json]", "pack", "config.json"]);

            Assert.Equal(projectDirectory, startInfo.WorkingDirectory);
            Assert.StartsWith(
                "tool exec vpk -y --version 1.3.68-pr.755.run.123 --configfile ",
                startInfo.Arguments);
            Assert.Contains(configPath, startInfo.Arguments);
            Assert.EndsWith("-- [json] pack config.json", startInfo.Arguments);

            runner.NugetConfigFile = Path.Combine(directory, "explicit.config");
            Assert.Contains(runner.NugetConfigFile, runner.CreateStartInfo([]).Arguments);

            var overrideRunner = new VpkToolRunner("1.3.68", directory, new TaskLoggingHelper(new PackTask())) {
                ProjectDirectory = projectDirectory,
            };
            var overrideArguments = overrideRunner.CreateStartInfo([]).Arguments;
            Assert.Contains("--source", overrideArguments);
            Assert.DoesNotContain("--add-source", overrideArguments);
        } finally {
            Directory.Delete(directory, true);
        }
    }
}
