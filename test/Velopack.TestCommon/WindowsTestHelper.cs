using System.Diagnostics;
using System.Runtime.Versioning;
using Velopack.Core;
using Velopack.Packaging.Windows.Commands;
using Velopack.Util;
using Velopack.Vpk;
using Velopack.Vpk.Logging;

namespace Velopack.TestCommon;

[SupportedOSPlatform("windows")]
public static class WindowsTestHelper
{
    public static string RandomString(int length)
        => TestHelper.RandomString(length);

    public static WindowsPackCommandRunner GetPackRunner(ILogger logger)
    {
        var console = new BasicConsole(logger, new VelopackDefaults(false));
        return new WindowsPackCommandRunner(logger, console);
    }

    public static string GetLogFilePath(string appId)
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "velopack",
            $"velopack_{appId}.log");
    }

    public static string ReadFileWithRetry(string path, ILogger logger)
        => TestHelper.ReadFileWithRetry(path, logger);

    public static string RunCoveredDotnet(string exe, string[] args, string workingDir, ILogger logger, int? exitCode = 0)
    {
        var outputfile = PathHelper.GetTestRootPath("coverage", $"coverage.rundotnet.{TestHelper.RandomString(8)}.xml");

        if (!File.Exists(exe))
            throw new Exception($"File {exe} does not exist.");

        var psi = new ProcessStartInfo("dotnet-coverage");
        psi.WorkingDirectory = workingDir;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;

        psi.ArgumentList.Add("collect");
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(outputfile);
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("cobertura");
        psi.ArgumentList.Add(exe);
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        return TestHelper.RunImpl(psi, logger, exitCode);
    }

    public static string RunNoCoverage(string exe, string[] args, string workingDir, ILogger logger, int? exitCode = 0)
        => TestHelper.RunNoCoverage(exe, args, workingDir, logger, exitCode);
}
