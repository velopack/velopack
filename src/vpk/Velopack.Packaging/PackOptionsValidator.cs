using FluentValidation;
using Velopack.Core.Validation;
using Velopack.Packaging.Abstractions;

namespace Velopack.Packaging;

public class PackOptionsValidator<T> : OptionsValidator<T> where T : class, IPackOptions
{
    public PackOptionsValidator()
    {
        RuleFor(x => x.PackId).NotEmpty().MustBeValidNuGetId();
        RuleFor(x => x.PackVersion).NotEmpty().MustBeSemverCompliant();
        RuleFor(x => x.PackDirectory).NotEmpty();
        RuleFor(x => x.ReleaseNotes).MustBeExistingFile();
        RuleFor(x => x.Icon).MustBeExistingFile();
        RuleFor(x => x.Channel).MustBeValidNuGetId();
        RuleFor(x => x.TargetRuntime).MustBeSupportedRid();
        RuleFor(x => x.NoPortable)
            .Must((opt, noPortable) => !(noPortable && opt.NoInst))
            .WithMessage("Cannot use 'noPortable' and 'noInst' options together, please choose one.");
    }
}
