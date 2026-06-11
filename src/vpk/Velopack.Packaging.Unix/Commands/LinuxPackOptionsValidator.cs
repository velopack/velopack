using FluentValidation;

namespace Velopack.Packaging.Unix.Commands;

public sealed class LinuxPackOptionsValidator : PackOptionsValidator<LinuxPackOptions>
{
    private static readonly string[] ValidCompression = ["gzip", "xz"];

    public LinuxPackOptionsValidator()
    {
        RuleFor(x => x.Compression)
            .Must(v => string.IsNullOrEmpty(v) || ValidCompression.Contains(v, StringComparer.OrdinalIgnoreCase))
            .WithMessage("{PropertyName} must be one of the following values: " + string.Join(", ", ValidCompression) + ".");
    }
}
