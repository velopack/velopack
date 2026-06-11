using FluentValidation;
using Velopack.Core.Validation;

namespace Velopack.Flow.Commands;

public sealed class PublishOptionsValidator : OptionsValidator<PublishOptions>
{
    public PublishOptionsValidator()
    {
        RuleFor(x => x.ReleaseDirectory).NotEmpty();
        RuleFor(x => x.TieredRolloutPercentage).InclusiveBetween(0, 100);
    }
}
