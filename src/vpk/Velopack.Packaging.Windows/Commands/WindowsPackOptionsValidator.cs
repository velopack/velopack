using FluentValidation;
using Velopack.Core;
using Velopack.Core.Validation;
using Velopack.Windows;

namespace Velopack.Packaging.Windows.Commands;

public sealed class WindowsPackOptionsValidator : PackOptionsValidator<WindowsPackOptions>
{
    public WindowsPackOptionsValidator()
    {
        RuleFor(x => x.EntryExecutableName).MustHaveExtension(".exe");
        RuleFor(x => x.Icon).MustHaveExtension(".ico");
        RuleFor(x => x.SplashImage).MustBeExistingFile();
        RuleFor(x => x.SignParallel).InclusiveBetween(1, 1000);
        RuleFor(x => x.SignExclude).MustBeValidRegex();
        RuleFor(x => x.AzureTrustedSignFile).MustBeExistingFile();
        RuleFor(x => x.MsiVersionOverride).MustBeValidMsiVersion();
        RuleFor(x => x.MsiBanner).MustHaveExtension(".bmp");
        RuleFor(x => x.MsiLogo).MustHaveExtension(".bmp");
        RuleFor(x => x.InstWelcome).MustBeExistingFile();
        RuleFor(x => x.InstReadme).MustBeExistingFile();
        RuleFor(x => x.InstLicense).MustBeExistingFile();
        RuleFor(x => x.InstConclusion).MustBeExistingFile();
        RuleFor(x => x.SignTemplate)
            .Must((opt, _) => new[] { opt.SignTemplate, opt.SignParameters, opt.AzureTrustedSignFile }.Count(v => !string.IsNullOrEmpty(v)) <= 1)
            .WithMessage("Cannot use more than one of 'signTemplate', 'signParams' and 'azureTrustedSignFile' options together, please choose one.");
        RuleFor(x => x.Shortcuts)
            .Must(v => string.IsNullOrEmpty(v) || v
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .All(x => Enum.TryParse<ShortcutLocation>(x, true, out _)))
            .WithMessage(
                "{PropertyName} contains invalid shortcut locations ('{PropertyValue}'). " +
                $"Valid values for comma delimited list are: {string.Join(", ", Enum.GetNames(typeof(ShortcutLocation)))}.");
        RuleFor(x => x.Runtimes).Custom((v, context) => {
            try {
                WindowsPackCommandRunner.ParseRuntimeDependencies(v);
            } catch (UserInfoException ex) {
                context.AddFailure(ex.Message);
            }
        });
    }
}
