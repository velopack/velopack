using FluentValidation;
using Velopack.Core.Validation;

namespace Velopack.Packaging.Windows.Commands;

public sealed class WindowsPackOptionsValidator : PackOptionsValidator<WindowsPackOptions>
{
    public WindowsPackOptionsValidator()
    {
        RuleFor(x => x.EntryExecutableName).MustHaveExtension(".exe");
        RuleFor(x => x.Icon).MustHaveExtension(".ico");
        RuleFor(x => x.SplashImage).MustBeExistingFile();
        RuleFor(x => x.SignParallel).InclusiveBetween(1, 1000);
        RuleFor(x => x.MsiVersionOverride).MustBeValidMsiVersion();
        RuleFor(x => x.MsiBanner).MustHaveExtension(".bmp");
        RuleFor(x => x.MsiLogo).MustHaveExtension(".bmp");
        RuleFor(x => x.SignTemplate)
            .Must((opt, _) => new[] { opt.SignTemplate, opt.SignParameters, opt.AzureTrustedSignFile }.Count(v => !string.IsNullOrEmpty(v)) <= 1)
            .WithMessage("Cannot use more than one of 'signTemplate', 'signParams' and 'azureTrustedSignFile' options together, please choose one.");
    }
}
