#nullable enable
using Velopack.Core;
using Velopack.Packaging;
using Velopack.Util;

namespace Velopack.CrossCompile.Tests;

/// <summary>
/// A Windows machine the cross-compiled installers are run on: either this process's own machine, or a
/// VM reached over SSH (used for Windows 7, which GitHub cannot host a runner on).
/// </summary>
internal interface IWindowsHost
{
    string LocalAppData { get; }

    /// <summary> Makes a local file or directory available on the host and returns its path there. </summary>
    string Stage(string localPath);

    (int ExitCode, string StdOutput) Run(string exe, params string[] args);

    bool Exists(string path);

    void Delete(string path);
}

internal static class WindowsHostExtensions
{
    public static string RunAndThrowIfNonZero(this IWindowsHost host, string exe, params string[] args)
    {
        var (exitCode, output) = host.Run(exe, args);
        if (exitCode != 0)
            throw new Exception($"'{exe} {String.Join(" ", args)}' exited with code {exitCode}:{Environment.NewLine}{output}");
        return output;
    }

    public static string Combine(this IWindowsHost _, params string[] parts) => String.Join("\\", parts);
}

internal sealed class LocalWindowsHost : IWindowsHost
{
    public string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public string Stage(string localPath) => localPath;

    public (int ExitCode, string StdOutput) Run(string exe, params string[] args)
    {
        var result = Exe.InvokeProcess(exe, args, Path.GetDirectoryName(exe)!);
        return (result.ExitCode, result.StdOutput);
    }

    public bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    public void Delete(string path) => IoUtil.DeleteFileOrDirectoryHard(path);
}

/// <summary>
/// A Windows VM reached with OpenSSH (password auth via sshpass), configured by the VELOPACK_WIN7_SSH
/// environment variable as "user:password@host:port". Remote commands run under cmd.exe.
/// </summary>
internal sealed class SshWindowsHost : IWindowsHost
{
    private const string StageDir = @"C:\velopack-test";

    private readonly string _user, _password, _host, _port;
    private readonly ILogger _logger;

    public string LocalAppData { get; }

    public SshWindowsHost(string connection, ILogger logger)
    {
        _logger = logger;
        var at = connection.LastIndexOf('@');
        var credentials = connection[..at].Split(':', 2);
        var endpoint = connection[(at + 1)..].Split(':', 2);
        _user = credentials[0];
        _password = credentials[1];
        _host = endpoint[0];
        _port = endpoint.Length > 1 ? endpoint[1] : "22";

        LocalAppData = Shell("echo %LOCALAPPDATA%").StdOutput.Trim();
        Shell($"if not exist {StageDir} mkdir {StageDir}");
    }

    public string Stage(string localPath)
    {
        var remote = $@"{StageDir}\{Path.GetFileName(localPath)}";
        Delete(remote);
        // the SFTP protocol (default since OpenSSH 9) wants drive paths as /C:/...
        var target = "/" + StageDir.Replace('\\', '/') + "/";
        var args = new List<string> { "-p", _password, "scp", "-r" };
        args.AddRange(SshOptions("-P"));
        args.Add(localPath);
        args.Add($"{_user}@{_host}:{target}");
        var result = Exe.InvokeProcess("sshpass", args, null);
        if (result.ExitCode != 0)
            throw new Exception($"scp of {localPath} failed ({result.ExitCode}): {result.StdErr}");
        return remote;
    }

    /// <summary>
    /// Runs the command in the VM's (auto-logged-on) desktop session via a scheduled task, like a user would.
    /// Running it directly over SSH is not equivalent: Win32-OpenSSH kills every process in the session's job
    /// when the command returns, which would kill the Update.exe that `apply` / `--uninstall` leave running.
    /// </summary>
    public (int ExitCode, string StdOutput) Run(string exe, params string[] args)
    {
        var command = $"\"{exe}\" " + String.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
        var job = $@"{StageDir}\job";
        using (TempUtil.GetTempDirectory(out var tempDir)) {
            var script = Path.Combine(tempDir, "job.cmd");
            File.WriteAllText(
                script,
                $"@echo off\r\n{command} > {job}.out 2> {job}.err\r\necho %ERRORLEVEL% > {job}.exit\r\n");
            Shell($"del /f /q {job}.out {job}.err {job}.exit 2>nul");
            Stage(script);
        }

        Shell($@"schtasks /create /f /tn velopack-test /tr {StageDir}\job.cmd /sc once /st 23:59 /it >nul");
        Shell("schtasks /run /tn velopack-test");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int exitCode;
        while (!int.TryParse(Shell($"type {job}.exit 2>nul").StdOutput.Trim(), out exitCode)) {
            if (sw.Elapsed > TimeSpan.FromMinutes(3))
                throw new TimeoutException($"'{command}' did not finish within 3 minutes on the VM");
            Thread.Sleep(1000);
        }

        var output = Shell($"type {job}.out").StdOutput;
        Shell($"type {job}.err");
        _logger.Info($"VM> {command} (exit {exitCode})");
        return (exitCode, output);
    }

    public bool Exists(string path) => Shell($"if exist \"{path}\" (echo yes) else (echo no)").StdOutput.Trim() == "yes";

    public void Delete(string path) =>
        Shell($"if exist \"{path}\\*\" (rmdir /s /q \"{path}\") else if exist \"{path}\" (del /f /q \"{path}\")");

    private (int ExitCode, string StdOutput) Shell(string command)
    {
        var args = new List<string> { "-p", _password, "ssh" };
        args.AddRange(SshOptions("-p"));
        args.Add($"{_user}@{_host}");
        args.Add(command);
        var result = Exe.InvokeProcess("sshpass", args, null);
        _logger.Info($"VM> {command} (exit {result.ExitCode}){Environment.NewLine}{result.StdOutput}{Environment.NewLine}{result.StdErr}".TrimEnd());
        // stderr carries velopack's log output; keep it out of the value tests match against
        return (result.ExitCode, result.StdOutput);
    }

    private IEnumerable<string> SshOptions(string portFlag) => [
        portFlag, _port,
        "-o", "StrictHostKeyChecking=no",
        "-o", "UserKnownHostsFile=/dev/null",
        "-o", "LogLevel=ERROR",
        "-o", "ConnectTimeout=30",
    ];
}
