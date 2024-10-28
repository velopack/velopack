using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Riok.Mapperly.Abstractions;

namespace Velopack.Deployment;

[Mapper(
    RequiredMappingStrategy = RequiredMappingStrategy.None,
    EnabledConversions = MappingConversionType.ExplicitCast | MappingConversionType.ImplicitCast)]
public static partial class DeploymentOptionsMapper
{
    public static partial AzureDownloadOptions ToDownloadOptions(this AzureUploadOptions uploadOptions);
    public static partial AzureUploadOptions ToUploadOptions(this AzureDownloadOptions downloadOptions);

    public static partial GiteaDownloadOptions ToDownloadOptions(this GiteaUploadOptions uploadOptions);
    public static partial GiteaUploadOptions ToUploadOptions(this GiteaDownloadOptions downloadOptions);

    public static partial GitHubDownloadOptions ToDownloadOptions(this GitHubUploadOptions uploadOptions);
    public static partial GitHubUploadOptions ToUploadOptions(this GitHubDownloadOptions downloadOptions);

    public static partial LocalDownloadOptions ToDownloadOptions(this LocalUploadOptions uploadOptions);
    public static partial LocalUploadOptions ToUploadOptions(this LocalDownloadOptions downloadOptions);

    public static partial S3DownloadOptions ToDownloadOptions(this S3UploadOptions uploadOptions);
    public static partial S3UploadOptions ToUploadOptions(this S3DownloadOptions downloadOptions);
}
