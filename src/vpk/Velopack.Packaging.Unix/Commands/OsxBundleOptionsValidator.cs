using FluentValidation;
using Velopack.Core.Validation;

namespace Velopack.Packaging.Unix.Commands;

public class OsxBundleOptionsValidator<T> : OptionsValidator<T> where T : OsxBundleOptions
{
    public OsxBundleOptionsValidator()
    {
        RuleFor(x => x.PackId).NotEmpty().MustBeValidNuGetId();
        RuleFor(x => x.PackVersion).NotEmpty().MustBeSemverCompliant();
        RuleFor(x => x.PackDirectory).NotEmpty();
        RuleFor(x => x.Icon).MustBeExistingFile().MustHaveExtension(".icns");
        RuleFor(x => x.InfoPlistPath).MustBeExistingFile();
        RuleFor(x => x.BundleId)
            .Must((opt, bundleId) => string.IsNullOrEmpty(bundleId) || string.IsNullOrEmpty(opt.InfoPlistPath))
            .WithMessage("Cannot use 'bundleId' and 'plist' options together, please choose one.");
    }
}

public sealed class OsxBundleOptionsValidator : OsxBundleOptionsValidator<OsxBundleOptions>;
