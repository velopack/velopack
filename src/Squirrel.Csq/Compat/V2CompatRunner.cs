using System.Diagnostics;
using Squirrel.Csq.Commands;

namespace Squirrel.Csq.Compat;

public class V2CompatRunner : ICommandRunner
{
    private readonly ILogger _logger;
    private readonly string _squirrelExePath;
    private readonly EmbeddedRunner _embedded;

    public V2CompatRunner(ILogger logger, string squirrelExePath)
    {
        _logger = logger;
        _squirrelExePath = squirrelExePath;
        _embedded = new EmbeddedRunner(logger);
    }

    public async Task ExecutePackWindows(WindowsPackCommand command)
    {
        if (!SquirrelRuntimeInfo.IsWindows || command.TargetRuntime.BaseRID != RuntimeOs.Windows) {
            throw new NotSupportedException("Squirrel v2.x is only supported on/for Windows.");
        }

        var options = new PackOptions {
            releaseDir = command.GetReleaseDirectory().FullName,
            package = command.Package,
            baseUrl = command.BaseUrl,
            framework = command.Runtimes,
            splashImage = command.SplashImage,
            icon = command.Icon,
            appIcon = command.AppIcon,
            noDelta = command.NoDelta,
            allowUnaware = false,
            signParams = command.SignParameters,
            signTemplate = command.SignTemplate,
            packId = command.PackId,
            includePdb = command.IncludePdb,
            packAuthors = command.PackAuthors,
            packDirectory = command.PackDirectory,
            packTitle = command.PackTitle,
            packVersion = command.PackVersion,
            releaseNotes = command.ReleaseNotes,
        };

        var args = new List<string> { "pack" };
        options.AddArgs(args);
        _logger.Debug($"Running V2 Squirrel.exe: '{_squirrelExePath} {String.Join(" ", args)}'");
        await Process.Start(_squirrelExePath, args).WaitForExitAsync();
    }

    public async Task ExecuteReleasifyWindows(WindowsReleasifyCommand command)
    {
        if (!SquirrelRuntimeInfo.IsWindows || command.TargetRuntime.BaseRID != RuntimeOs.Windows) {
            throw new NotSupportedException("Squirrel v2.x is only supported on/for Windows.");
        }

        var options = new ReleasifyOptions {
            releaseDir = command.GetReleaseDirectory().FullName,
            package = command.Package,
            baseUrl = command.BaseUrl,
            framework = command.Runtimes,
            splashImage = command.SplashImage,
            icon = command.Icon,
            appIcon = command.AppIcon,
            noDelta = command.NoDelta,
            allowUnaware = false,
            signParams = command.SignParameters,
            signTemplate = command.SignTemplate,
        };

        var args = new List<string> { "releasify" };
        options.AddArgs(args);
        _logger.Debug($"Running V2 Squirrel.exe: '{_squirrelExePath} {String.Join(" ", args)}'");
        await Process.Start(_squirrelExePath, args).WaitForExitAsync();
    }

    public Task ExecuteBundleOsx(OsxBundleCommand command)
    {
        throw new NotSupportedException("Squirrel v2.x is only supported on/for Windows.");
    }

    public Task ExecuteReleasifyOsx(OsxReleasifyCommand command)
    {
        throw new NotSupportedException("Squirrel v2.x is only supported on/for Windows.");
    }

    public Task ExecuteGithubDownload(GitHubDownloadCommand command)
    {
        return ((ICommandRunner) _embedded).ExecuteGithubDownload(command);
    }

    public Task ExecuteGithubUpload(GitHubUploadCommand command)
    {
        return ((ICommandRunner) _embedded).ExecuteGithubUpload(command);
    }

    public Task ExecuteHttpDownload(HttpDownloadCommand command)
    {
        return ((ICommandRunner) _embedded).ExecuteHttpDownload(command);
    }

    public Task ExecuteS3Download(S3DownloadCommand command)
    {
        return ((ICommandRunner) _embedded).ExecuteS3Download(command);
    }

    public Task ExecuteS3Upload(S3UploadCommand command)
    {
        return ((ICommandRunner) _embedded).ExecuteS3Upload(command);
    }

    private abstract class BaseOptions
    {
        public string releaseDir { get; set; }

        public virtual void AddArgs(List<string> args)
        {
            if (!String.IsNullOrWhiteSpace(releaseDir)) {
                args.Add("--releaseDir");
                args.Add(releaseDir);
            }
        }
    }

    private class SigningOptions : BaseOptions
    {
        public string signParams { get; set; }
        public string signTemplate { get; set; }

        public override void AddArgs(List<string> args)
        {
            base.AddArgs(args);

            if (!String.IsNullOrWhiteSpace(signParams)) {
                args.Add("--signParams");
                args.Add(signParams);
            }

            if (!String.IsNullOrWhiteSpace(signTemplate)) {
                args.Add("--signTemplate");
                args.Add(signTemplate);
            }
        }
    }

    private class ReleasifyOptions : SigningOptions
    {
        public string package { get; set; }
        public string baseUrl { get; set; }
        public string framework { get; set; }
        public string splashImage { get; set; }
        public string icon { get; set; }
        public string appIcon { get; set; }
        public bool noDelta { get; set; }
        public bool allowUnaware { get; set; }
        public string msi { get; set; }
        public string debugSetupExe { get; set; }

        public override void AddArgs(List<string> args)
        {
            base.AddArgs(args);

            if (!String.IsNullOrWhiteSpace(package)) {
                args.Add("--package");
                args.Add(package);
            }

            if (!String.IsNullOrWhiteSpace(baseUrl)) {
                args.Add("--baseUrl");
                args.Add(baseUrl);
            }

            if (!String.IsNullOrWhiteSpace(framework)) {
                args.Add("--framework");
                args.Add(framework);
            }

            if (!String.IsNullOrWhiteSpace(splashImage)) {
                args.Add("--splashImage");
                args.Add(splashImage);
            }

            if (!String.IsNullOrWhiteSpace(icon)) {
                args.Add("--icon");
                args.Add(icon);
            }

            if (!String.IsNullOrWhiteSpace(appIcon)) {
                args.Add("--appIcon");
                args.Add(appIcon);
            }

            if (noDelta) {
                args.Add("--noDelta");
            }

            if (allowUnaware) {
                args.Add("--allowUnaware");
            }

            if (!String.IsNullOrWhiteSpace(msi)) {
                args.Add("--msi");
                args.Add(msi);
            }

            if (!String.IsNullOrWhiteSpace(debugSetupExe)) {
                args.Add("--debugSetupExe");
                args.Add(debugSetupExe);
            }
        }
    }

    private class PackOptions : ReleasifyOptions
    {
        public string packId { get; set; }
        public string packTitle { get; set; }
        public string packVersion { get; set; }
        public string packAuthors { get; set; }
        public string packDirectory { get; set; }
        public bool includePdb { get; set; }
        public string releaseNotes { get; set; }

        public override void AddArgs(List<string> args)
        {
            base.AddArgs(args);

            if (!String.IsNullOrWhiteSpace(packId)) {
                args.Add("--packId");
                args.Add(packId);
            }

            if (!String.IsNullOrWhiteSpace(packTitle)) {
                args.Add("--packTitle");
                args.Add(packTitle);
            }

            if (!String.IsNullOrWhiteSpace(packVersion)) {
                args.Add("--packVersion");
                args.Add(packVersion);
            }

            if (!String.IsNullOrWhiteSpace(packAuthors)) {
                args.Add("--packAuthors");
                args.Add(packAuthors);
            }

            if (!String.IsNullOrWhiteSpace(packDirectory)) {
                args.Add("--packDir");
                args.Add(packDirectory);
            }

            if (includePdb) {
                args.Add("--includePdb");
            }

            if (!String.IsNullOrWhiteSpace(releaseNotes)) {
                args.Add("--releaseNotes");
                args.Add(releaseNotes);
            }
        }
    }
}
