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
            .WithMessage(
                VelopackRuntimeInfo.IsOSX
                    ? "Cannot use 'noPortable' and 'noInst' options together, please choose one."
                    : "Cannot use 'noPortable' off macOS: no .pkg installer is built, so the portable package is required.");
        RuleFor(x => x.PackDirectory)
            .Must(dir => VelopackRuntimeInfo.IsOSX || !dir.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase))
            .When(x => !String.IsNullOrEmpty(x.PackDirectory))
            .WithMessage("Cannot use a .pkg as 'packDir' off macOS (extracting it needs pkgutil): pass the .app bundle instead.");
        RuleFor(x => x.SignEntitlements).MustBeExistingFile().MustHaveExtension(".entitlements");

        // rcodesign (signP12File, notaryApiKeyFile) signs and notarizes the app bundle on any OS.
        RuleFor(x => x.SignP12File).MustBeExistingFile();
        RuleFor(x => x.SignP12PasswordFile).MustBeExistingFile();
        RuleFor(x => x.NotaryApiKeyFile).MustBeExistingFile();
        // Must rather than NotEmpty().When(...): NotEmpty would mark the option (REQUIRED) in the help text.
        RuleFor(x => x.SignP12PasswordFile)
            .Must((opt, password) => String.IsNullOrEmpty(opt.SignP12File) || !String.IsNullOrEmpty(password))
            .WithMessage("'signP12File' needs its password: pass 'signP12PasswordFile'.");
        RuleFor(x => x.SignP12PasswordFile)
            .Empty()
            .When(x => String.IsNullOrEmpty(x.SignP12File))
            .WithMessage("'signP12PasswordFile' is only supported together with 'signP12File'.");
        RuleFor(x => x.NotaryApiKeyFile)
            .Empty()
            .When(x => String.IsNullOrEmpty(x.SignP12File))
            .WithMessage("'notaryApiKeyFile' is only supported together with 'signP12File'.");

        if (VelopackRuntimeInfo.IsOSX) {
            RuleFor(x => x.InstWelcome).MustBeExistingFile();
            RuleFor(x => x.InstReadme).MustBeExistingFile();
            RuleFor(x => x.InstLicense).MustBeExistingFile();
            RuleFor(x => x.InstConclusion).MustBeExistingFile();
            RuleFor(x => x.Keychain).MustBeExistingFile();

            // The .pkg installer is still signed and notarized with Apple's tools, so with signP12File,
            // signInstallIdentity signs the installer, and notaryProfile and keychain notarize it.
            RuleFor(x => x.SignP12File)
                .Empty()
                .When(x => !String.IsNullOrEmpty(x.SignAppIdentity))
                .WithMessage("Cannot use 'signAppIdentity' and 'signP12File' options together, please choose one.");
            RuleFor(x => x.SignInstallIdentity).Empty().When(SignsWithP12WithoutInstaller).WithMessage(InstallerOnly("signInstallIdentity"));
            RuleFor(x => x.NotaryProfile).Empty().When(SignsWithP12WithoutInstaller).WithMessage(InstallerOnly("notaryProfile"));
            RuleFor(x => x.Keychain).Empty().When(SignsWithP12WithoutInstaller).WithMessage(InstallerOnly("keychain"));
            RuleFor(x => x.NotaryProfile)
                .Empty()
                .When(x => SignsWithP12WithInstaller(x) && String.IsNullOrEmpty(x.SignInstallIdentity))
                .WithMessage("With 'signP12File', 'notaryProfile' only notarizes the .pkg installer, which needs 'signInstallIdentity'.");
            RuleFor(x => x.Keychain)
                .Empty()
                .When(x => SignsWithP12WithInstaller(x) && String.IsNullOrEmpty(x.NotaryProfile))
                .WithMessage("With 'signP12File', 'keychain' is only used to notarize the .pkg installer, which needs 'notaryProfile'.");
        } else {
            // vpk only offers these options on macOS, but a [json] config populates the options directly and could still
            // set them. Refuse them rather than silently dropping them.
            RuleFor(x => x.SignAppIdentity).Empty().WithMessage(MacOSOnly("signAppIdentity"));
            RuleFor(x => x.SignInstallIdentity).Empty().WithMessage(MacOSOnly("signInstallIdentity"));
            RuleFor(x => x.NotaryProfile).Empty().WithMessage(MacOSOnly("notaryProfile"));
            RuleFor(x => x.Keychain).Empty().WithMessage(MacOSOnly("keychain"));
            RuleFor(x => x.InstWelcome).Empty().WithMessage(MacOSOnly("instWelcome"));
            RuleFor(x => x.InstReadme).Empty().WithMessage(MacOSOnly("instReadme"));
            RuleFor(x => x.InstLicense).Empty().WithMessage(MacOSOnly("instLicense"));
            RuleFor(x => x.InstConclusion).Empty().WithMessage(MacOSOnly("instConclusion"));
        }
    }

    private static bool SignsWithP12WithoutInstaller(OsxPackOptions options) =>
        !String.IsNullOrEmpty(options.SignP12File) && options.NoInst;

    private static bool SignsWithP12WithInstaller(OsxPackOptions options) =>
        !String.IsNullOrEmpty(options.SignP12File) && !options.NoInst;

    private static string InstallerOnly(string option) =>
        $"With 'signP12File', '{option}' only applies to the .pkg installer, which is not being built.";

    private static string MacOSOnly(string option) =>
        $"'{option}' needs Apple's tools and is only available on macOS.";
}
