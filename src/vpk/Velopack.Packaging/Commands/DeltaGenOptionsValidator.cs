using FluentValidation;
using Velopack.Core.Validation;

namespace Velopack.Packaging.Commands;

public sealed class DeltaGenOptionsValidator : OptionsValidator<DeltaGenOptions>
{
    public DeltaGenOptionsValidator()
    {
        RuleFor(x => x.BasePackage).NotEmpty().MustHaveExtension(".nupkg").MustBeExistingFile();
        RuleFor(x => x.NewPackage).NotEmpty().MustHaveExtension(".nupkg").MustBeExistingFile();
        RuleFor(x => x.OutputFile).NotEmpty();
    }
}
