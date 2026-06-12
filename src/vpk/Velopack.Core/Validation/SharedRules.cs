using FluentValidation;
using Velopack.NuGet;

namespace Velopack.Core.Validation;

/// <summary>
/// Reusable FluentValidation rules shared by the vpk command validators. All rules skip
/// null/empty values - required-ness should be expressed separately with NotEmpty()/NotNull().
/// </summary>
public static class SharedRules
{
    public static IRuleBuilderOptions<T, string?> MustBeValidNuGetId<T>(this IRuleBuilder<T, string?> rule)
    {
        return rule.Must(v => string.IsNullOrEmpty(v) || NugetUtil.IsValidNuGetId(v!))
            .WithMessage(
                "{PropertyName} is an invalid NuGet package id ('{PropertyValue}'). " +
                "It must contain only alphanumeric characters, underscores, dashes, and dots.");
    }

    public static IRuleBuilderOptions<T, string?> MustBeSemverCompliant<T>(this IRuleBuilder<T, string?> rule)
    {
        return rule.Must((_, v, context) => {
            if (string.IsNullOrEmpty(v)) return true;
            try {
                NugetUtil.ThrowIfVersionNotSemverCompliant(v!);
                return true;
            } catch (ArgumentException ex) {
                context.MessageFormatter.AppendArgument("VersionError", ex.Message);
                return false;
            }
        }).WithMessage("{PropertyName} contains an invalid package version '{PropertyValue}': {VersionError}");
    }

    public static IRuleBuilderOptions<T, string?> MustBeExistingFile<T>(this IRuleBuilder<T, string?> rule)
    {
        return rule.Must(v => string.IsNullOrEmpty(v) || File.Exists(v))
            .WithMessage("{PropertyName} file is not found, but must exist ('{PropertyValue}').");
    }

    public static IRuleBuilderOptions<T, FileInfo?> MustBeExistingFile<T>(this IRuleBuilder<T, FileInfo?> rule)
    {
        return rule.Must(v => v == null || File.Exists(v.FullName))
            .WithMessage("{PropertyName} file is not found, but must exist ('{PropertyValue}').");
    }

    public static IRuleBuilderOptions<T, string?> MustBeExistingDirectory<T>(this IRuleBuilder<T, string?> rule)
    {
        return rule.Must(v => string.IsNullOrEmpty(v) || Directory.Exists(v))
            .WithMessage("{PropertyName} directory is not found, but must exist ('{PropertyValue}').");
    }

    public static IRuleBuilderOptions<T, DirectoryInfo?> MustBeExistingDirectory<T>(this IRuleBuilder<T, DirectoryInfo?> rule)
    {
        return rule.Must(v => v == null || Directory.Exists(v.FullName))
            .WithMessage("{PropertyName} directory is not found, but must exist ('{PropertyValue}').");
    }

    public static IRuleBuilderOptions<T, string?> MustHaveExtension<T>(this IRuleBuilder<T, string?> rule, string extension)
    {
        return rule.Must(v => string.IsNullOrEmpty(v) || string.Equals(Path.GetExtension(v), extension, StringComparison.OrdinalIgnoreCase))
            .WithMessage($"{{PropertyName}} must have a '{extension}' extension ('{{PropertyValue}}').");
    }

    public static IRuleBuilderOptions<T, string?> MustBeValidRegex<T>(this IRuleBuilder<T, string?> rule)
    {
        return rule.Must(v => {
            if (string.IsNullOrEmpty(v)) return true;
            try {
                _ = new System.Text.RegularExpressions.Regex(v!);
                return true;
            } catch (ArgumentException) {
                return false;
            }
        }).WithMessage("{PropertyName} is not a valid regular expression ('{PropertyValue}').");
    }

    public static IRuleBuilderOptions<T, string?> MustBeValidMsiVersion<T>(this IRuleBuilder<T, string?> rule)
    {
        return rule.Must(v => {
            if (string.IsNullOrEmpty(v)) return true;
            if (!Version.TryParse(v, out var parsed)) return false;
            // per the MSI ProductVersion docs there is no fourth field, but a zero revision
            // is tolerated because the default version is generated as 'major.minor.patch.0'.
            return parsed.Major <= 255 && parsed.Minor <= 255 && parsed.Build <= 65535 && parsed.Revision <= 0;
        }).WithMessage("{PropertyName} is an invalid MSI ProductVersion ('{PropertyValue}'). Valid range is [0-255].[0-255].[0-65535]");
    }

    public static IRuleBuilderOptions<T, string?> MustBeValidHttpUri<T>(this IRuleBuilder<T, string?> rule)
    {
        return rule.Must(v => string.IsNullOrEmpty(v) ||
                (Uri.TryCreate(v, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
            .WithMessage("{PropertyName} must contain an absolute http / https Uri ('{PropertyValue}').");
    }

    public static IRuleBuilderOptions<T, DirectoryInfo?> MustBeNonEmptyDirectory<T>(this IRuleBuilder<T, DirectoryInfo?> rule)
    {
        return rule.Must(v => {
            if (v == null) return true;
            try {
                return Directory.Exists(v.FullName) && Directory.EnumerateFileSystemEntries(v.FullName).Any();
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) {
                return false;
            }
        }).WithMessage("{PropertyName} must be a non-empty directory ('{PropertyValue}').");
    }

    public static IRuleBuilderOptions<T, RID?> MustBeSupportedRid<T>(this IRuleBuilder<T, RID?> rule)
    {
        return rule.Must(v => v == null ||
                (v.HasArchitecture && v.BaseRID is RuntimeOs.Windows or RuntimeOs.Linux or RuntimeOs.OSX))
            .WithMessage("{PropertyName} is an invalid or unsupported runtime ('{PropertyValue}'). Valid example: win-x64, osx-arm64.");
    }
}
