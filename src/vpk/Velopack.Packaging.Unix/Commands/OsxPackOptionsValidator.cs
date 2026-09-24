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

        // rcodesign signing and notarization (RcodesignTools): the route that works off macOS.
        RuleFor(x => x.SignP12File).MustBeExistingFile();
        RuleFor(x => x.SignP12PasswordFile).MustBeExistingFile();
        RuleFor(x => x.NotaryApiKeyFile).MustBeExistingFile();
        RuleFor(x => x.SignP12PasswordFile)
            .NotEmpty()
            .When(x => !String.IsNullOrEmpty(x.SignP12File))
            .WithMessage("'signP12File' needs its password: pass 'signP12PasswordFile'.");
        RuleFor(x => x.SignP12File)
            .Empty()
            .When(x => !String.IsNullOrEmpty(x.SignAppIdentity))
            .WithMessage("Cannot use 'signAppIdentity' and 'signP12File' together: sign with the keychain (codesign) " +
                         "or with a certificate file (rcodesign), not both.");
        RuleFor(x => x.NotaryApiKeyFile)
            .Empty()
            .When(x => !String.IsNullOrEmpty(x.NotaryProfile))
            .WithMessage("Cannot use 'notaryProfile' and 'notaryApiKeyFile' together, please choose one.");
        RuleFor(x => x.NotaryApiKeyFile)
            .Empty()
            .When(x => String.IsNullOrEmpty(x.SignP12File))
            .WithMessage("'notaryApiKeyFile' notarizes what rcodesign signed, so it needs 'signP12File' too.");

        // Apple's own tools (codesign, notarytool, the keychain, pkgbuild) exist only on macOS.
        RuleFor(x => x.SignAppIdentity).Must(BeMacOnly).WithMessage(MacOnly("signAppIdentity"));
        RuleFor(x => x.SignInstallIdentity).Must(BeMacOnly).WithMessage(MacOnly("signInstallIdentity"));
        RuleFor(x => x.NotaryProfile).Must(BeMacOnly).WithMessage(MacOnly("notaryProfile"));
        RuleFor(x => x.Keychain).Must(BeMacOnly).WithMessage(MacOnly("keychain"));
        RuleFor(x => x.NoInst)
            .Must(noInst => noInst || OperatingSystem.IsMacOS())
            .WithMessage("The .pkg installer is built with pkgbuild and productbuild, which only exist on macOS. " +
                         "Off macOS, pass 'noInst': the release and the portable .app are built without it.");
    }

    private static bool BeMacOnly(string value) => String.IsNullOrEmpty(value) || OperatingSystem.IsMacOS();

    private static string MacOnly(string option) =>
        $"'{option}' uses Apple's codesign/notarytool, which only exist on macOS. Off macOS, sign and notarize with " +
        "rcodesign instead: 'signP12File', 'signP12PasswordFile' and 'notaryApiKeyFile'.";
}
