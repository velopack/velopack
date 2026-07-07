using FluentValidation;
using Velopack.Core.Validation;

namespace Velopack.Flow.Commands;

public sealed class PublishOptionsValidator : VelopackFlowServiceOptionsValidator<PublishOptions>
{
    public PublishOptionsValidator()
    {
        RuleFor(x => x.ReleaseDirectory).NotEmpty().MustBeExistingDirectory();
        RuleFor(x => x.TieredRolloutPercentage).InclusiveBetween(0, 100);
    }
}
