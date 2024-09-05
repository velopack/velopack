using System.Text.RegularExpressions;
using NuGet.Versioning;
using Velopack.NuGet;

namespace Velopack.Vpk.Commands;

internal static class SystemCommandLineExtensions
{
    public static string ToFullNameOrNull(this FileSystemInfo fsi)
    {
        return fsi?.FullName;
    }

    public static string ToAbsoluteOrNull(this Uri uri)
    {
        if (uri?.IsAbsoluteUri == true) return uri.AbsoluteUri;
        return null;
    }

    public static CliOption<T> SetDescription<T>(this CliOption<T> option, string description)
    {
        option.Description = description;
        return option;
    }

    public static CliOption<T> SetRecursive<T>(this CliOption<T> option, bool isRecursive = true)
    {
        option.Recursive = isRecursive;
        return option;
    }

    public static CliOption<T> SetHidden<T>(this CliOption<T> option, bool isHidden = true)
    {
        option.Hidden = isHidden;
        return option;
    }

    public static CliOption<T> AllowMultiple<T>(this CliOption<T> option, bool allowMultiple = true)
    {
        option.AllowMultipleArgumentsPerToken = allowMultiple;
        return option;
    }

    public static CliOption<T> SetRequired<T>(this CliOption<T> option, bool isRequired = true)
    {
        option.Required = isRequired;
        return option;
    }

    public static CliOption<T> SetDefault<T>(this CliOption<T> option, T defaultValue)
    {
        option.DefaultValueFactory = (r) => defaultValue;
        return option;
    }

    public static CliOption<T> SetArgumentHelpName<T>(this CliOption<T> option, string argumentHelpName)
    {
        option.HelpName = argumentHelpName;
        return option;
    }

    public static CliOption<int> MustBeBetween(this CliOption<int> option, int minimum, int maximum)
    {
        option.Validators.Add(x => Validate.MustBeBetween(x, minimum, maximum));
        return option;
    }

    public static CliOption<Uri> MustBeValidHttpUri(this CliOption<Uri> option)
    {
        option.CustomParser = (v) => new Uri(v.Tokens.Single().Value, UriKind.RelativeOrAbsolute);
        option.RequiresScheme(Uri.UriSchemeHttp, Uri.UriSchemeHttps).RequiresAbsolute();
        return option;
    }

    public static CliOption<FileInfo> RequiresExtension(this CliOption<FileInfo> option, string extension)
    {
        option.Validators.Add(x => Validate.RequiresExtension(x, extension));
        return option;
    }

    public static CliOption<DirectoryInfo> RequiresExtension(this CliOption<DirectoryInfo> option, string extension)
    {
        option.Validators.Add(x => Validate.RequiresExtension(x, extension));
        return option;
    }

    public static CliOption<string> RequiresExtension(this CliOption<string> option, string extension)
    {
        option.Validators.Add(x => Validate.RequiresExtension(x, extension));
        return option;
    }

    public static CliCommand AreMutuallyExclusive(this CliCommand command, params CliOption[] options)
    {
        command.Validators.Add(x => Validate.AreMutuallyExclusive(x, options));
        return command;
    }

    //public static Command RequiredAllowObsoleteFallback(this Command command, Option option, Option obsoleteOption)
    //{
    //    command.AddValidator(x => Validate.AtLeastOneRequired(x, new[] { option, obsoleteOption }, true));
    //    return command;
    //}

    public static CliCommand AtLeastOneRequired(this CliCommand command, params CliOption[] options)
    {
        command.Validators.Add(x => Validate.AtLeastOneRequired(x, options, false));
        return command;
    }

    public static CliOption<string> MustContain(this CliOption<string> option, string value)
    {
        option.Validators.Add(x => Validate.MustContain(x, value));
        return option;
    }

    public static CliOption<Uri> RequiresScheme(this CliOption<Uri> option, params string[] validSchemes)
    {
        option.Validators.Add(x => Validate.RequiresScheme(x, validSchemes));
        return option;
    }

    public static CliOption<Uri> RequiresAbsolute(this CliOption<Uri> option, params string[] validSchemes)
    {
        option.Validators.Add(Validate.RequiresAbsolute);
        return option;
    }

    public static CliOption<string> RequiresValidNuGetId(this CliOption<string> option)
    {
        option.Validators.Add(Validate.RequiresValidNuGetId);
        return option;
    }

    //TODO: Could setup the options to accept type SemanticVersion and apply an appropriate parser for it
    public static CliOption<string> RequiresSemverCompliant(this CliOption<string> option)
    {
        option.Validators.Add(Validate.RequiresSemverCompliant);
        return option;
    }

    public static CliOption<DirectoryInfo> MustNotBeEmpty(this CliOption<DirectoryInfo> option)
    {
        option.Validators.Add(Validate.MustNotBeEmpty);
        return option;
    }

    public static CliOption<string> MustBeValidMsiVersion(this CliOption<string> option)
    {
        option.Validators.Add(Validate.MustBeValidMsiVersion);
        return option;
    }

    public static CliOption<string> MustBeSupportedRid(this CliOption<string> option)
    {
        option.Validators.Add(Validate.MustBeSupportedRid);
        return option;
    }

    public static CliOption<FileInfo> MustExist(this CliOption<FileInfo> option)
    {
        option.Validators.Add(Validate.FileMustExist);
        return option;
    }

    public static CliOption<DirectoryInfo> MustExist(this CliOption<DirectoryInfo> option)
    {
        option.Validators.Add(Validate.DirectoryMustExist);
        return option;
    }

    private static class Validate
    {
        public static void MustBeBetween(OptionResult result, int minimum, int maximum)
        {
            for (int i = 0; i < result.Tokens.Count; i++) {
                if (int.TryParse(result.Tokens[i].Value, out int value)) {
                    if (value < minimum || value > maximum) {
                        result.AddError($"The value for {result.IdentifierToken.Value} must be greater than {minimum} and less than {maximum}");
                        break;
                    }
                } else {
                    result.AddError($"{result.Tokens[i].Value} is not a valid integer for {result.IdentifierToken.Value}");
                    break;
                }
            }
        }

        public static void RequiresExtension(OptionResult result, string extension)
        {
            for (int i = 0; i < result.Tokens.Count; i++) {
                if (!string.Equals(Path.GetExtension(result.Tokens[i].Value), extension, StringComparison.InvariantCultureIgnoreCase)) {
                    result.AddError($"{result.IdentifierToken.Value} does not have an {extension} extension");
                    break;
                }
            }
        }

        public static void FileMustExist(OptionResult result)
        {
            for (int i = 0; i < result.Tokens.Count; i++) {
                var fsi = new FileInfo(result.Tokens[i].Value);
                if (!fsi.Exists) {
                    result.AddError($"{result.IdentifierToken.Value} file is not found, but must exist");
                    break;
                }
            }
        }

        public static void DirectoryMustExist(OptionResult result)
        {
            for (int i = 0; i < result.Tokens.Count; i++) {
                var fsi = new DirectoryInfo(result.Tokens[i].Value);
                if (!fsi.Exists) {
                    result.AddError($"{result.IdentifierToken.Value} directory is not found, but must exist");
                    break;
                }
            }
        }

        public static void AreMutuallyExclusive(CommandResult result, CliOption[] options)
        {
            var specifiedOptions = options
                .Where(x => result.GetResult(x) is not null)
                .ToList();
            if (specifiedOptions.Count > 1) {
                string optionsString = string.Join(" and ", specifiedOptions.Select(x => $"'{x.Aliases.First()}'"));
                result.AddError($"Cannot use {optionsString} options together, please choose one.");
            }
        }

        public static void AtLeastOneRequired(CommandResult result, CliOption[] options, bool onlyShowFirst = false)
        {
            var anySpecifiedOptions = options
                .Any(x => result.GetResult(x) is not null);
            if (!anySpecifiedOptions) {
                if (onlyShowFirst) {
                    result.AddError($"Required argument missing for option: {options.First().Aliases.First()}");
                } else {
                    string optionsString = string.Join(" and ", options.Select(x => $"'{x.Aliases.First()}'"));
                    result.AddError($"At least one of the following options are required {optionsString}.");
                }
            }
        }

        public static void MustContain(OptionResult result, string value)
        {
            for (int i = 0; i < result.Tokens.Count; i++) {
                if (result.Tokens[i].Value?.Contains(value) == false) {
                    result.AddError($"{result.IdentifierToken.Value} must contain '{value}'. Current value is '{result.Tokens[i].Value}'");
                    break;
                }
            }
        }

        public static void RequiresScheme(OptionResult result, string[] validSchemes)
        {
            for (int i = 0; i < result.Tokens.Count; i++) {
                if (Uri.TryCreate(result.Tokens[i].Value, UriKind.RelativeOrAbsolute, out Uri uri) &&
                    uri.IsAbsoluteUri &&
                    !validSchemes.Contains(uri.Scheme)) {
                    result.AddError($"{result.IdentifierToken.Value} must contain a Uri with one of the following schems: {string.Join(", ", validSchemes)}. Current value is '{result.Tokens[i].Value}'");
                    break;
                }
            }
        }

        public static void RequiresAbsolute(OptionResult result)
        {
            for (int i = 0; i < result.Tokens.Count; i++) {
                if (!Uri.TryCreate(result.Tokens[i].Value, UriKind.Absolute, out Uri _)) {
                    result.AddError($"{result.IdentifierToken.Value} must contain an absolute Uri. Current value is '{result.Tokens[i].Value}'");
                    break;
                }
            }
        }

        public static void RequiresValidNuGetId(OptionResult result)
        {
            for (int i = 0; i < result.Tokens.Count; i++) {
                if (!NugetUtil.IsValidNuGetId(result.Tokens[i].Value)) {
                    result.AddError($"{result.IdentifierToken.Value} is an invalid NuGet package id. It must contain only alphanumeric characters, underscores, dashes, and dots.. Current value is '{result.Tokens[i].Value}'");
                    break;
                }
            }
        }

        public static void RequiresSemverCompliant(OptionResult result)
        {
            string specifiedAlias = result.IdentifierToken.Value;

            for (int i = 0; i < result.Tokens.Count; i++) {
                string version = result.Tokens[i].Value;
                //TODO: This is duplicating NugetUtil.ThrowIfVersionNotSemverCompliant
                if (SemanticVersion.TryParse(version, out var parsed)) {
                    if (parsed < new SemanticVersion(0, 0, 1, parsed.Release)) {
                        result.AddError($"{result.IdentifierToken.Value} contains an invalid package version '{version}', it must be >= 0.0.1.");
                        break;
                    }
                } else {
                    result.AddError($"{result.IdentifierToken.Value} contains an invalid package version '{version}', it must be a 3-part SemVer2 compliant version string.");
                    break;
                }
            }
        }

        public static void MustNotBeEmpty(OptionResult result)
        {
            for (int i = 0; i < result.Tokens.Count; i++) {
                var token = result.Tokens[i];

                if (!Directory.Exists(token.Value) ||
                    !Directory.EnumerateFileSystemEntries(token.Value).Any()) {
                    result.AddError($"{result.IdentifierToken.Value} must be a non-empty directory, but the specified directory '{token.Value}' was empty.");
                    return;
                }
            }
        }

        public static void MustBeValidMsiVersion(OptionResult result)
        {
            for (var i = 0; i < result.Tokens.Count; i++) {
                var version = result.Tokens[i].Value;
                if (Version.TryParse(version, out var parsed)) {
                    if (parsed.Major > 255 || parsed.Minor > 255 || parsed.Build > 65535 || parsed.Revision > 0) {
                        result.AddError($"MSI ProductVersion out of bounds '{version}'. Valid range is [0-255].[0-255].[0-65535].[0]");
                    }
                } else {
                    result.AddError("Version string is invalid / could not be parsed.");
                    break;
                }
            }
        }

        public static void MustBeSupportedRid(OptionResult result)
        {
            for (var i = 0; i < result.Tokens.Count; i++) {
                if (!Regex.IsMatch(result.Tokens[i].Value, @"^(?<os>osx|linux|win)\.?(?<ver>[\d\.]+)?(?:-(?<arch>(?:x|arm)\d{2}))$")) {
                    result.AddError($"Invalid or unsupported runtime '{result.IdentifierToken.Value}'. Valid example: win-x64, osx-arm64.");
                    break;
                }
            }
        }
    }
}
