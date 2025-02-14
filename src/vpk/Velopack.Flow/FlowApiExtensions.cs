using Velopack.Core;

namespace Velopack.Flow;

public partial class Profile
{
    public string GetDisplayName()
    {
        return DisplayName ?? Email ?? "<unknown>";
    }
}

public class ApiErrorResult
{
    public string? Type { get; set; }
    public string? Title { get; set; }
    public string? Detail { get; set; }
    public int? Status { get; set; }

    public UserInfoException? ToUserInfoException()
    {
        if (!String.IsNullOrWhiteSpace(Detail)) {
            return new UserInfoException(Detail!);
        }

        return null;
    }
}

public static class FlowApiExtensions
{
    public static ApiErrorResult? ToErrorResult(this ApiException ex)
    {
        if (ex.Response != null) {
            return SimpleJson.DeserializeObject<ApiErrorResult>(ex.Response);
        }

        return null;
    }

    public static FileType ToFileType(this Velopack.VelopackAssetType type)
    {
        return type switch {
            Velopack.VelopackAssetType.Full => FileType.Full,
            Velopack.VelopackAssetType.Delta => FileType.Delta,
            Velopack.VelopackAssetType.Portable => FileType.Portable,
            Velopack.VelopackAssetType.Installer => FileType.Setup,
            //TODO: MSI Deployment Tool?
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }
}