using System.Drawing;
using System.Text;
using Microsoft.Extensions.Logging;
using Velopack.NuGet;

namespace Velopack.Packaging.Windows.Commands;

public class WindowsPackCommandRunner
{
    private readonly ILogger _logger;

    public WindowsPackCommandRunner(ILogger logger)
    {
        _logger = logger;
    }

    public void Pack(WindowsPackOptions options)
    {
        if (options.EntryExecutableName == null)
            options.EntryExecutableName = options.PackId + ".exe";

        using (Utility.GetTempDirectory(out var tmp)) {
            var nupkgPath = new NugetConsole(_logger).CreatePackageFromOptions(tmp, options);
            options.Package = nupkgPath;
            var runner = new WindowsReleasifyCommandRunner(_logger);
            runner.Releasify(options);
        }
    }
}