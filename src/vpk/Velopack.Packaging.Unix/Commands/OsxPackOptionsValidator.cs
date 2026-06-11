using FluentValidation;
using Velopack.Core.Validation;

namespace Velopack.Packaging.Unix.Commands;

public sealed class OsxPackOptionsValidator : OsxBundleOptionsValidator<OsxPackOptions>
{
    public OsxPackOptionsValidator()
    {
        RuleFor(x => x.Channel).MustBeValidNuGetId();
        RuleFor(x => x.TargetRuntime).MustBeSupportedRid();
        RuleFor(x => x.ReleaseNotes).MustBeExistingFile();
        RuleFor(x => x.NoPortable)
            .Must((opt, noPortable) => !(noPortable && opt.NoInst))
            .WithMessage("Cannot use 'noPortable' and 'noInst' options together, please choose one.");
        RuleFor(x => x.InstWelcome).MustBeExistingFile();
        RuleFor(x => x.InstReadme).MustBeExistingFile();
        RuleFor(x => x.InstLicense).MustBeExistingFile();
        RuleFor(x => x.InstConclusion).MustBeExistingFile();
        RuleFor(x => x.SignEntitlements).MustBeExistingFile().MustHaveExtension(".entitlements");
        RuleFor(x => x.Keychain).MustBeExistingFile();
    }
}
