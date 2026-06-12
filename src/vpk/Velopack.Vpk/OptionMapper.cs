using Riok.Mapperly.Abstractions;
using Velopack.Core;
using Velopack.Deployment;
using Velopack.Flow.Commands;
using Velopack.Packaging.Commands;
using Velopack.Packaging.Unix.Commands;
using Velopack.Packaging.Windows.Commands;
using Velopack.Vpk.Commands.Deployment;
using Velopack.Vpk.Commands.Flow;
using Velopack.Vpk.Commands.Packaging;

namespace Velopack.Vpk;

[Mapper(
    RequiredMappingStrategy = RequiredMappingStrategy.Target,
    EnabledConversions = MappingConversionType.None)]
public static partial class OptionMapper
{
    public static partial TDest Map<TDest>(object source);

    public static partial OsxPackOptions ToOptions(this OsxPackCommand cmd);

    public static partial WindowsPackOptions ToOptions(this WindowsPackCommand cmd);

    public static partial LinuxPackOptions ToOptions(this LinuxPackCommand cmd);

    public static partial OsxBundleOptions ToOptions(this OsxBundleCommand cmd);

    public static partial GitHubDownloadOptions ToOptions(this GitHubDownloadCommand cmd);

    public static partial GitHubUploadOptions ToOptions(this GitHubUploadCommand cmd);

    // GitHubUploadOptions no longer derives from GitHubDownloadOptions (both derive from the
    // shared GitRelease*Options bases), so the generic Map<TDest> needs an explicit mapping
    // to convert an upload command to download options.
    public static partial GitHubDownloadOptions ToDownloadOptions(this GitHubUploadCommand cmd);

    public static partial GiteaDownloadOptions ToDownloadOptions(this GiteaUploadCommand cmd);

    public static partial GiteaDownloadOptions ToOptions(this GiteaDownloadCommand cmd);

    public static partial GiteaUploadOptions ToOptions(this GiteaUploadCommand cmd);

    public static partial HttpDownloadOptions ToOptions(this HttpDownloadCommand cmd);

    [MapperIgnoreTarget(nameof(LocalDownloadOptions.Timeout))]
    public static partial LocalDownloadOptions ToOptions(this LocalDownloadCommand cmd);

    [MapperIgnoreTarget(nameof(LocalDownloadOptions.Timeout))]
    public static partial LocalUploadOptions ToOptions(this LocalUploadCommand cmd);

    public static partial S3DownloadOptions ToOptions(this S3DownloadCommand cmd);

    public static partial S3UploadOptions ToOptions(this S3UploadCommand cmd);

    public static partial AzureDownloadOptions ToOptions(this AzureDownloadCommand cmd);

    public static partial AzureUploadOptions ToOptions(this AzureUploadCommand cmd);

    public static partial DeltaGenOptions ToOptions(this DeltaGenCommand cmd);

    public static partial DeltaPatchOptions ToOptions(this DeltaPatchCommand cmd);

    public static partial LoginOptions ToOptions(this LoginCommand cmd);

    public static partial LogoutOptions ToOptions(this LogoutCommand cmd);

    public static partial PublishOptions ToOptions(this PublishCommand cmd);

    public static partial ApiOptions ToOptions(this ApiCommand cmd);

    private static DirectoryInfo StringToDirectoryInfo(string t)
    {
        if (t == null) return null;
        var di = new DirectoryInfo(t);
        if (!di.Exists) di.Create();
        return di;
    }

    private static RID StringToRID(string t)
    {
        if (t == null) return null;
        try {
            return RID.Parse(t);
        } catch (Exception ex) {
            throw new UserInfoException($"Invalid runtime identifier '{t}': {ex.Message}");
        }
    }
}