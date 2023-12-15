using System.Runtime.Versioning;
using Squirrel.Csq.Commands;

namespace Squirrel.Csq.Compat;

public interface ICommandRunner
{
    public Task ExecuteGithubDownload(GitHubDownloadCommand command);
    public Task ExecuteGithubUpload(GitHubUploadCommand command);
    public Task ExecuteHttpDownload(HttpDownloadCommand command);
    public Task ExecuteS3Download(S3DownloadCommand command);
    public Task ExecuteS3Upload(S3UploadCommand command);
    public Task ExecuteBundleOsx(OsxBundleCommand command);
    public Task ExecuteReleasifyOsx(OsxReleasifyCommand command);
    public Task ExecuteReleasifyWindows(WindowsReleasifyCommand command);
    public Task ExecutePackWindows(WindowsPackCommand command);
}
