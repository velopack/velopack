using FluentValidation;

namespace Velopack.Core.Validation;

public static class ValidationExtensions
{
    private static int _configured;

    /// <summary>
    /// Configures FluentValidation global options. Property names are resolved to camelCase
    /// so validation messages match the option keys used in JSON config files.
    /// </summary>
    public static void EnsureGlobalConfiguration()
    {
        if (Interlocked.Exchange(ref _configured, 1) != 0) {
            return;
        }

        ValidatorOptions.Global.DisplayNameResolver = (type, member, expression) =>
            member == null || member.Name.Length == 0
                ? null
                : char.ToLowerInvariant(member.Name[0]) + member.Name.Substring(1);
    }

    /// <summary>
    /// Returns the names of all properties which have a NotNull / NotEmpty rule in this
    /// validator, ie. the properties which the user must provide a value for. This is used
    /// to render '(REQUIRED)' markers in the CLI help text.
    /// </summary>
    public static IReadOnlyCollection<string> GetRequiredProperties(this IValidator validator)
    {
        return validator.CreateDescriptor()
            .GetMembersWithValidators()
            .Where(m => m.Any(v => v.Validator is FluentValidation.Validators.INotNullValidator or FluentValidation.Validators.INotEmptyValidator))
            .Select(m => m.Key)
            .ToList();
    }

    /// <summary>
    /// Runs the validator and throws a <see cref="UserInfoException"/> containing
    /// every failure message if the options are not valid.
    /// </summary>
    public static void EnsureValidOptions<T>(this IValidator<T> validator, T options)
    {
        var result = validator.Validate(options);
        if (!result.IsValid) {
            var messages = result.Errors.Select(x => "- " + x.ErrorMessage);
            throw new UserInfoException("The provided options are invalid:" + Environment.NewLine + string.Join(Environment.NewLine, messages));
        }
    }
}

/// <summary>
/// Base class for all options validators. Ensures the global FluentValidation
/// configuration is applied before any rules are created.
/// </summary>
public abstract class OptionsValidator<T> : AbstractValidator<T>
{
    static OptionsValidator()
    {
        ValidationExtensions.EnsureGlobalConfiguration();
    }
}
