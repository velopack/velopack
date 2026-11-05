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
        RuleFor(x => x.Exclude).MustBeValidRegex();
        RuleFor(x => x.NoPortable)
            .Must((opt, noPortable) => !(noPortable && opt.NoInst))
            .WithMessage("Cannot use 'noPortable' and 'noInst' options together, please choose one.");
        RuleFor(x => x.InstWelcome).MustBeExistingFile();
        RuleFor(x => x.InstReadme).MustBeExistingFile();
        RuleFor(x => x.InstLicense).MustBeExistingFile();
        RuleFor(x => x.InstConclusion).MustBeExistingFile();
        RuleFor(x => x.SignEntitlements).MustBeExistingFile().MustHaveExtension(".entitlements");
        RuleFor(x => x.Keychain).MustBeExistingFile();
        RuleFor(x => x.NotaryKey).MustBeExistingFile();
        RuleFor(x => x.NotaryKey)
            .Must((opt, key) => string.IsNullOrEmpty(key) == string.IsNullOrEmpty(opt.NotaryKeyId))
            .WithMessage("The 'notaryKey' and 'notaryKeyId' options must be used together.");
        RuleFor(x => x.NotaryIssuer)
            .Must((opt, issuer) => string.IsNullOrEmpty(issuer) || !string.IsNullOrEmpty(opt.NotaryKey))
            .WithMessage("The 'notaryIssuer' option requires 'notaryKey' and 'notaryKeyId'.");
        RuleFor(x => x.NotaryProfile)
            .Must((opt, profile) => string.IsNullOrEmpty(profile) || (string.IsNullOrEmpty(opt.NotaryKey) && string.IsNullOrEmpty(opt.NotaryKeyId)))
            .WithMessage("Cannot use 'notaryProfile' and 'notaryKey' options together, please choose one.");
    }
}
