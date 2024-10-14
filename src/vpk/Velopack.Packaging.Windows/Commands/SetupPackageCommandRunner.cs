using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO.Compression;
using System.Linq;
using System.Net.Security;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Velopack.NuGet;
using Velopack.Packaging.Abstractions;
using Velopack.Packaging.Windows;
using Velopack.Packaging.Windows.Commands;

namespace Velopack.Packaging.Commands;
public class SetupPackageCommandRunner : ICommand<SetupPackageOptions>
{
    private readonly ILogger _logger;
    private readonly IFancyConsole _console;

    public SetupPackageCommandRunner(ILogger logger, IFancyConsole console)
    {
        _logger = logger;
        _console = console;
    }

    public async Task Run(SetupPackageOptions options)
    {
        await _console.ExecuteProgressAsync(async (ctx) => {
            await ctx.RunTask($"Creating setup package", (progress) => {
                var zipPackage = new ZipPackage(options.NugetPackagePath);

                var outExePath = Path.Combine(options.OutputPath, $"{zipPackage.Id}-win-Setup.exe");

                WindowsPackCommandRunner.CreateSetupPackageImpl(progress, new WindowsPackOptions() {
                    Icon = options.Icon, SignParameters = options.SignParameters, SignTemplate = options.SignTemplate, SignParallel = options.SignParallel
                } , _logger, options.NugetPackagePath, outExePath);
                return Task.CompletedTask;
            });
        });
    }
}
