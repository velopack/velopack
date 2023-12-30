using Microsoft.Extensions.Configuration;
using Squirrel.Csq.Commands;
using Squirrel.Csq.Updates;

namespace Squirrel.Csq.Compat;

public class RunnerFactory
{
    private const string CLOWD_PACKAGE_NAME = "Clowd.Squirrel";
    private readonly ILogger _logger;
    private readonly IConfiguration _config;

    public RunnerFactory(ILogger logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    public async Task CreateAndExecuteAsync<T>(string commandName, T options) where T : BaseCommand
    {
        var runner = await CreateAsync(options);
        var method = typeof(ICommandRunner).GetMethod(commandName);
        await (Task) method.Invoke(runner, new object[] { options });
    }

    private async Task<ICommandRunner> CreateAsync<T>(T options)
    {
        if (_config.GetValue<bool?>("SKIP_UPDATE_CHECK") != true) {
            var updateCheck = new UpdateChecker(_logger);
            await updateCheck.CheckForUpdates();
        }

        if (options is not PlatformCommand) {
            return new EmbeddedRunner(_logger);
        }

        var cmd = (PlatformCommand) (object) options;
        var solutionDir = FindSolutionDirectory(cmd.SolutionDir?.FullName);

        if (solutionDir is null) {
            throw new Exception($"Could not find '.sln'. Specify solution or solution directory with '--solution='.");
        }

        var version = new SquirrelVersionLocator(_logger).Search(solutionDir, CLOWD_PACKAGE_NAME);

        if (version.Major == 4) {
            var myVer = SquirrelRuntimeInfo.SquirrelNugetVersion;
            if (version != myVer) {
                _logger.Warn($"Installed SDK is {version}, while csq is {myVer}, this is not recommended.");
            }
            return new EmbeddedRunner(_logger);
        }

        if (version.Major == 2 && version.Minor > 7) {
            _logger.Warn("Running in V2 compatibility mode. Not all features may be available.");

            Dictionary<string, string> packageSearchPaths = new();
            var nugetPackagesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
            packageSearchPaths.Add("nuget user profile cache", Path.Combine(nugetPackagesDir, CLOWD_PACKAGE_NAME.ToLower(), "{0}", "tools"));
            packageSearchPaths.Add("visual studio packages cache", Path.Combine(solutionDir, "packages", CLOWD_PACKAGE_NAME + ".{0}", "tools"));
            string squirrelExe = null;

            foreach (var kvp in packageSearchPaths) {
                var path = String.Format(kvp.Value, version);
                if (Directory.Exists(path)) {
                    _logger.Debug($"Found {CLOWD_PACKAGE_NAME} {version} from {kvp.Key}");
                    var toolExePath = Path.Combine(path, "Squirrel.exe");
                    if (File.Exists(toolExePath)) {
                        squirrelExe = toolExePath;
                        break;
                    }
                }
            }

            if (squirrelExe is null) {
                throw new Exception($"Could not find {CLOWD_PACKAGE_NAME} {version} Squirrel.exe");
            }

            return new V2CompatRunner(_logger, squirrelExe);
        }

        throw new NotSupportedException($"Squirrel {version} is installed in this project, but not supported by this version of Csq. Supported versions are [>= v2.8] and [>= v4.0]");
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
