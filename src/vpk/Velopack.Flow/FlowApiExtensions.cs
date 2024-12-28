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

    public static FileType ToFileType(this VelopackAssetType type)
    {
        switch (type) {
        case VelopackAssetType.Full:
            return FileType.Full;
        case VelopackAssetType.Delta:
            return FileType.Delta;
        case VelopackAssetType.Portable:
            return FileType.Portable;
        case VelopackAssetType.Installer:
            return FileType.Setup;
        default:
            throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }
    }
}