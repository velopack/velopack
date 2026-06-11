using FluentValidation;
using Velopack.Core.Validation;

namespace Velopack.Core.Abstractions;

/// <summary>
/// Base class for command runners which do not require validation. Derive from
/// <see cref="ValidatedCommand{TOpt, TValidator}"/> instead to validate the options
/// before the command body executes.
/// </summary>
public abstract class ValidatedCommand<TOpt> : ICommand<TOpt> where TOpt : class
{
    public virtual IValidator<TOpt>? Validator => null;

    public async Task Run(TOpt options)
    {
        Validator?.EnsureValidOptions(options);
        await RunCoreAsync(options).ConfigureAwait(false);
    }

    protected abstract Task RunCoreAsync(TOpt options);
}

/// <summary>
/// Base class for command runners which validates the options with <typeparamref name="TValidator"/>
/// before executing the command body. Validation failures are aggregated and thrown as a single
/// <see cref="UserInfoException"/>.
/// </summary>
public abstract class ValidatedCommand<TOpt, TValidator> : ValidatedCommand<TOpt>
    where TOpt : class
    where TValidator : IValidator<TOpt>, new()
{
    public override IValidator<TOpt> Validator { get; } = new TValidator();
}
