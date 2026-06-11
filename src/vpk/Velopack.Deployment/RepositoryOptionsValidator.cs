using FluentValidation;
using Velopack.Core.Validation;
using Velopack.NuGet;

namespace Velopack.Deployment;

public class RepositoryOptionsValidator<T> : OptionsValidator<T> where T : RepositoryOptions
{
    public RepositoryOptionsValidator()
    {
        // the Channel getter computes a default from TargetOs and can throw for RuntimeOs.Unknown,
        // so it must be read inside a guarded Custom rule rather than a RuleFor() expression.
        RuleFor(x => x).Custom((opt, context) => {
            string channel;
            try {
                channel = opt.Channel;
            } catch {
                return;
            }

            if (!string.IsNullOrEmpty(channel) && !NugetUtil.IsValidNuGetId(channel)) {
                context.AddFailure(
                    "channel",
                    $"channel is an invalid NuGet package id ('{channel}'). " +
                    "It must contain only alphanumeric characters, underscores, dashes, and dots.");
            }
        });
    }

    protected void AddReleaseDirRules()
    {
        RuleFor(x => x.ReleaseDir).NotNull().MustBeNonEmptyDirectory();
    }
}
