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
public class ExtractPortableCommandRunner : ICommand<ExtractPortableOptions>
{
    private readonly ILogger _logger;
    private readonly IFancyConsole _console;

    public ExtractPortableCommandRunner(ILogger logger, IFancyConsole console)
    {
        _logger = logger;
        _console = console;
    }

    public async Task Run(ExtractPortableOptions options)
    {
        await _console.ExecuteProgressAsync(async (ctx) => {
            await ctx.RunTask($"Extracting portable package", async (progress) => {
                await ExtractPortablePackage(progress, options.SetupPackagePath, options.OutputPath);
            });
        });
    }

    protected virtual async Task ExtractPortablePackage(Action<int> progress, string setupPackagePath, string outputPath)
    {
        if (!File.Exists(setupPackagePath)) {
            throw new ArgumentException($"Cannot create Portable package for '{setupPackagePath}' because the setup package does not exist.");
        }
        if (!SetupBundle.IsBundle(setupPackagePath, out long offset, out long _)) {
            throw new ArgumentException($"Cannot create Portable package for '{setupPackagePath}' because it does not appear to be a bundle setup package.");
        }

        progress(1);

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
