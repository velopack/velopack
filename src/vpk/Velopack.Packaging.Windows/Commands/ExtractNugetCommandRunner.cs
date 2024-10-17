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

namespace Velopack.Packaging.Commands;
public class ExtractNugetCommandRunner : ICommand<ExtractNugetOptions>
{
    private readonly ILogger _logger;
    private readonly IFancyConsole _console;

    public ExtractNugetCommandRunner(ILogger logger, IFancyConsole console)
    {
        _logger = logger;
        _console = console;
    }

    public async Task Run(ExtractNugetOptions options)
    {
        await _console.ExecuteProgressAsync(async (ctx) => {
            await ctx.RunTask($"Extracting nuget package", async (progress) => {
                await ExtractNugetPackage(progress, options.SetupPackagePath, options.OutputPath);
            });
        });
    }

    protected virtual async Task ExtractNugetPackage(Action<int> progress, string setupPackagePath, string outputPath)
    {
        if (!File.Exists(setupPackagePath)) {
            throw new ArgumentException($"Cannot create nuget package for '{setupPackagePath}' because the setup package does not exist.");
        }
        if (!SetupBundle.IsBundle(setupPackagePath, out long offset, out long _)) {
            throw new ArgumentException($"Cannot create nuget package for '{setupPackagePath}' because it does not appear to be a bundle setup package.");
        }

        progress(1);

        //System.IO.Compression and thus ZipPackage has troubles with the exe+zip, so read the file, copy off the zip to a MemoryStream, then parse / save it
        using var zipStream = File.OpenRead(setupPackagePath);
        zipStream.Position = offset;
        using var memStream = new MemoryStream();
        await zipStream.CopyToAsync(memStream);

        progress(34);
        //the full nupkg file is literally just the zip contents of the setup package.
        var setupPkgSpec = new ZipPackage(memStream);

        var fullPkg = Path.Combine(outputPath, $"{setupPkgSpec.Id}-{setupPkgSpec.Version}-full.nupkg");

        progress(67);

        memStream.Position = 0;
        await memStream.CopyToAsync(File.Create(fullPkg));

        progress(100);
    }
}
