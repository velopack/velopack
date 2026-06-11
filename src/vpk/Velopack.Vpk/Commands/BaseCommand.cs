using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Humanizer;
using Microsoft.Extensions.Configuration;
using Serilog.Core;

namespace Velopack.Vpk.Commands;

public class BaseCommand : Command
{
    public RuntimeOs TargetOs { get; private set; }

    private readonly Dictionary<Option, Action<ParseResult, IConfiguration>> _setters = new();

    private readonly Dictionary<Option, string> _envHelp = new();

    private readonly Dictionary<Option, string> _targetProperties = new();

    /// <summary>
    /// Hidden positional argument which accepts the path to a JSON config file when the
    /// [json] directive is used (eg. 'vpk [json] pack myconfig.json').
    /// </summary>
    public Argument<string> JsonConfigArgument { get; }

    protected BaseCommand(string name, string description)
        : base(name, description)
    {
        JsonConfigArgument = new Argument<string>("jsonConfig") {
            Arity = ArgumentArity.ZeroOrOne,
            Hidden = true,
            Description = "Path to a JSON config file, used with the [json] directive.",
        };
        Add(JsonConfigArgument);
    }

    protected Option<T> AddOption<T>(Action<T> setValue, string[] aliases,
        [CallerArgumentExpression(nameof(setValue))] string setterExpression = null)
    {
        var opt = AddOption(setValue, new Option<T>(aliases.OrderByDescending(a => a.Length).First(), aliases));

        // extract the assignment target from the setter lambda (eg. "(v) => PackId = v" -> "PackId"),
        // which is the property name the option maps to - used to match validator rules for help text.
        var match = Regex.Match(setterExpression ?? "", @"=>\s*(\w+)\s*=");
        if (match.Success) {
            _targetProperties[opt] = match.Groups[1].Value;
        }

        return opt;
    }

    private Option<T> AddOption<T>(Action<T> setValue, Option<T> opt)
    {
        string optionName = opt.Name.TrimStart('-');
        string titleCase = String.Join("_", optionName.Humanize(LetterCasing.AllCaps).Split(' '));

        _envHelp[opt] = "VPK_" + titleCase;
        _setters[opt] = (ctx, config) => {
            // 1. if the option was set explicitly on the command line, only use that value
            var optionResult = ctx.GetResult(opt);
            if (optionResult != null && !optionResult.Errors.Any() && !optionResult.Implicit) {
                setValue(ctx.GetValue(opt));
                return;
            }

            // 2. if the option was not set explicitly on the command line, try to get IConfiguration value
            var configSection = config.GetSection(titleCase);
            if (configSection.Exists()) {
                setValue(config.GetValue<T>(titleCase));
                return;
            }

            // 3. if we found no value in IConfiguration, try to get the default value
            if (optionResult != null && !optionResult.Errors.Any()) {
                setValue(ctx.GetValue(opt));
                return;
            }
        };

        Add(opt);
        return opt;
    }

    protected void RemoveOption(Option option)
    {
        _setters.Remove(option);
        _envHelp.Remove(option);
        _targetProperties.Remove(option);
        Options.Remove(option);
    }

    /// <summary>
    /// Returns the name of the options-class property this option maps to, or null if unknown.
    /// </summary>
    public string GetTargetPropertyName(Option option) => _targetProperties.TryGetValue(option, out string value) ? value : null;

    /// <summary>
    /// Marks any option whose target property appears in <paramref name="requiredProperties"/>
    /// as required, for rendering '(REQUIRED)' in the help text.
    /// </summary>
    public void ApplyRequiredHints(IReadOnlyCollection<string> requiredProperties)
    {
        foreach (var opt in _targetProperties) {
            if (requiredProperties.Contains(opt.Value)) {
                opt.Key.SetRequiredHint();
            }
        }
    }

    public string GetEnvVariableName(Option option) => _envHelp.TryGetValue(option, out string value) ? value : null;

    /// <summary>
    /// Returns the names of all options which were explicitly provided on the command line
    /// (ie. excluding default values, and excluding global/recursive options).
    /// </summary>
    public IReadOnlyList<string> GetExplicitOptionNames(ParseResult context)
    {
        return _setters.Keys
            .Where(opt => context.GetResult(opt) is { Implicit: false })
            .Select(opt => opt.Name)
            .ToList();
    }

    public void SetProperties(ParseResult context, IConfiguration config, RuntimeOs targetOs)
    {
        TargetOs = targetOs;
        foreach (var kvp in _setters) {
            kvp.Value(context, config);
        }
    }

    public virtual void Initialize(LoggingLevelSwitch logLevelSwitch)
    { }

    public ParseResult ParseAndApply(string command, IConfiguration config = null, RuntimeOs? targetOs = null)
    {
        var x = Parse(command);
        SetProperties(x, config ?? new ConfigurationManager(), targetOs ?? VelopackRuntimeInfo.SystemOs);
        return x;
    }
}
