#nullable enable
using FluentValidation;
using Velopack.Core;

namespace Velopack.Packaging;

/// <summary>
/// FluentValidation rules for the <c>--fileAssociations</c> / <c>--urlProtocols</c> options, surfacing the
/// <see cref="AppAssociations"/> parse errors as validation failures. Null/empty values pass.
/// </summary>
public static class AppAssociationRules
{
    public static IRuleBuilderOptionsConditions<T, string?> MustBeValidFileAssociations<T>(this IRuleBuilder<T, string?> rule)
    {
        return rule.Custom((v, context) => {
            try {
                AppAssociations.ParseFileAssociations(v);
            } catch (UserInfoException ex) {
                context.AddFailure(ex.Message);
            }
        });
    }

    public static IRuleBuilderOptionsConditions<T, string?> MustBeValidUrlProtocols<T>(this IRuleBuilder<T, string?> rule)
    {
        return rule.Custom((v, context) => {
            try {
                AppAssociations.ParseUrlProtocols(v);
            } catch (UserInfoException ex) {
                context.AddFailure(ex.Message);
            }
        });
    }
}
