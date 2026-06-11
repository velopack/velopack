using FluentValidation;
using Velopack.Core.Validation;

namespace Velopack.Packaging.Commands;

public sealed class DeltaPatchOptionsValidator : OptionsValidator<DeltaPatchOptions>
{
    public DeltaPatchOptionsValidator()
    {
        RuleFor(x => x.BasePackage).NotEmpty().MustHaveExtension(".nupkg").MustBeExistingFile();
        RuleFor(x => x.OutputFile).NotEmpty();
        RuleFor(x => x.PatchFiles).NotEmpty().WithMessage("Must specify at least one patch file.");
        RuleForEach(x => x.PatchFiles).MustBeExistingFile();
    }
}
