using System.ComponentModel;
using Humanizer;
using Microsoft.Extensions.Configuration;
using Velopack.Packaging.Abstractions;

namespace Velopack.Vpk.Commands;

public class BaseCommand : CliCommand
{
    private readonly Dictionary<CliOption, Action<ParseResult, IConfiguration>> _setters = new();

    private readonly static Dictionary<BaseCommand, List<(string name, CliOption option)>> _envHelp = new();

    protected BaseCommand(string name, string description)
        : base(name, description)
    {
        _envHelp[this] = new List<(string name, CliOption option)>();
    }

    protected virtual CliOption<T> AddOption<T>(Action<T> setValue, params string[] aliases)
    {
        return AddOption(setValue, new CliOption<T>(aliases.OrderByDescending(a => a.Length).First(), aliases));
    }

    private CliOption<T> AddOption<T>(Action<T> setValue, CliOption<T> opt)
    {
        string optionName = opt.Name.TrimStart('-');
        string titleCase = String.Join("_", optionName.Humanize(LetterCasing.AllCaps).Split(' '));

        _envHelp[this].Add(("VPK_" + titleCase, opt));
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

    public static void PrintEnvironmentHelp(IFancyConsole console)
    {
        console.WriteLine("The following environment variables can be specified instead of command line arguments.");
        console.WriteLine();
        foreach (var kvp in _envHelp) {
            List<string[]> rows = [];
            foreach (var (name, opt) in kvp.Value) {
                rows.Add([name, opt.Description]);
            }
            console.WriteTable(kvp.Key.Name, rows, false);
            console.WriteLine();
        }
    }

    public virtual void SetProperties(ParseResult context, IConfiguration config)
    {
        foreach (var kvp in _setters) {
            kvp.Value(context, config);
        }
    }

    public virtual ParseResult ParseAndApply(string command, IConfiguration config = null)
    {
        var x = Parse(command);
        SetProperties(x, config ?? new ConfigurationManager());
        return x;
    }
}
