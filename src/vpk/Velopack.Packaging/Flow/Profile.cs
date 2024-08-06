namespace Velopack.Packaging.Flow;

#nullable enable
public class Profile
{
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }

    public string? GetDisplayName()
    {
        return DisplayName ?? Email ?? "<unknown>";
    }
}
