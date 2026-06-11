using FluentValidation;
using Velopack.Core.Validation;

namespace Velopack.Flow;

/// <summary>
/// Base validator for all commands which talk to the Velopack Flow service.
/// </summary>
public class VelopackFlowServiceOptionsValidator<T> : OptionsValidator<T> where T : VelopackFlowServiceOptions
{
    public VelopackFlowServiceOptionsValidator()
    {
        RuleFor(x => x.VelopackBaseUrl).MustBeValidHttpUri();
        RuleFor(x => x.Timeout).GreaterThan(0);
    }
}
