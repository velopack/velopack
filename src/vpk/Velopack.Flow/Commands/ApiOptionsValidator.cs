using FluentValidation;
using Velopack.Core.Validation;

namespace Velopack.Flow.Commands;

public sealed class ApiOptionsValidator : OptionsValidator<ApiOptions>
{
    public ApiOptionsValidator()
    {
        RuleFor(x => x.Method).NotEmpty();
        RuleFor(x => x.Endpoint).NotEmpty();
    }
}
