using Microsoft.Extensions.Configuration;
using Velopack.Packaging;
using Velopack.Vpk.Commands;
using Velopack.Vpk.Updates;

namespace Velopack.Vpk.Compat;

public class RunnerFactory
{
    private const string NUGET_PACKAGE_NAME = "Velopack";
    private readonly ILogger _logger;
    private readonly IConfiguration _config;

    public RunnerFactory(ILogger logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    public async Task CreateAndExecuteAsync<T>(string commandName, T options) where T : BaseCommand
    {
        _logger.LogInformation($"[bold]{Program.INTRO}[/]");
        var runner = await CreateAsync(options);
        var method = typeof(ICommandRunner).GetMethod(commandName);
        try {
            await (Task) method.Invoke(runner, new object[] { options });
        } catch (Exception ex) when (ex is ProcessFailedException or UserInfoException) {
            // some exceptions are just user info / user error, so don't need a stack trace.
            _logger.Fatal($"[bold orange3]{ex.Message}[/]");
        } catch (Exception ex) {
            _logger.Fatal(ex, $"Command {commandName} had an exception.");
        }
    }

    private async Task<ICommandRunner> CreateAsync<T>(T options)
    {
        if (_config.GetValue<bool?>("SKIP_UPDATE_CHECK") != true) {
            var updateCheck = new UpdateChecker(_logger);
            await updateCheck.CheckForUpdates();
        }

        return new EmbeddedRunner(_logger);
        //if (options is not PlatformCommand) {
        //}

        //var cmd = (PlatformCommand) (object) options;
        //var solutionDir = FindSolutionDirectory(cmd.SolutionDir?.FullName);

        //if (solutionDir is null) {
        //    throw new Exception($"Could not find '.sln'. Specify solution or solution directory with '--solution='.");
        //}

        //var version = new SdkVersionLocator(_logger).Search(solutionDir, NUGET_PACKAGE_NAME);

        //var myVer = VelopackRuntimeInfo.VelopackNugetVersion;
        //if (version != myVer) {
        //    _logger.Warn($"Installed SDK is {version}, while vpk is {myVer}, this is not recommended when building packages.");
        //}

        //return new EmbeddedRunner(_logger);
    }

    private string FindSolutionDirectory(string slnArgument)
    {
        if (!String.IsNullOrWhiteSpace(slnArgument)) {
            if (File.Exists(slnArgument) && slnArgument.EndsWith(".sln", StringComparison.InvariantCultureIgnoreCase)) {
                // we were given a sln file as argument
                return Path.GetDirectoryName(Path.GetFullPath(slnArgument));
            }

            if (Directory.Exists(slnArgument) && Directory.EnumerateFiles(slnArgument, "*.sln").Any()) {
                return Path.GetFullPath(slnArgument);
            }
        }

        // try to find the solution directory from cwd
        var cwd = Environment.CurrentDirectory;
        var slnSearchDirs = new string[] {
            cwd,
            Path.Combine(cwd, ".."),
            Path.Combine(cwd, "..", ".."),
        };

        return slnSearchDirs.FirstOrDefault(d => Directory.EnumerateFiles(d, "*.sln").Any());
    }
}
