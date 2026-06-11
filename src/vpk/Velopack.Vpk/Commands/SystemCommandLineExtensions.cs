using System.Runtime.CompilerServices;

namespace Velopack.Vpk.Commands;

public static class SystemCommandLineExtensions
{
    private static readonly ConditionalWeakTable<Option, object> RequiredHints = new();

    public static string ToFullNameOrNull(this FileSystemInfo fsi)
    {
        return fsi?.FullName;
    }

    public static Option<T> SetDescription<T>(this Option<T> option, string description)
    {
        option.Description = description;
        return option;
    }

    public static Option<T> SetRecursive<T>(this Option<T> option, bool isRecursive = true)
    {
        option.Recursive = isRecursive;
        return option;
    }

    public static Option<T> SetHidden<T>(this Option<T> option, bool isHidden = true)
    {
        option.Hidden = isHidden;
        return option;
    }

    public static Option<T> AllowMultiple<T>(this Option<T> option, bool allowMultiple = true)
    {
        option.AllowMultipleArgumentsPerToken = allowMultiple;
        return option;
    }

    /// <summary>
    /// Marks the option as required for help rendering purposes only. It does not set
    /// <see cref="Option.Required"/>, because required options must be satisfiable via
    /// VPK_* environment variables or a [json] config file, which are applied after
    /// parsing. Required-ness is enforced by the FluentValidation validators in the
    /// command runners, and this hint is derived from those validators at startup.
    /// </summary>
    public static void SetRequiredHint(this Option option)
    {
        RequiredHints.AddOrUpdate(option, new object());
    }

    public static bool IsRequiredHint(this Option option)
    {
        return RequiredHints.TryGetValue(option, out _);
    }

    public static Option<T> SetDefault<T>(this Option<T> option, T defaultValue)
    {
        option.DefaultValueFactory = (r) => defaultValue;
        return option;
    }

    public static Option<T> SetArgumentHelpName<T>(this Option<T> option, string argumentHelpName)
    {
        option.HelpName = argumentHelpName;
        return option;
    }
}
