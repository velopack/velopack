namespace Squirrel.Csq.Commands;

public class BaseCommand : CliCommand
{
    private Dictionary<CliOption, Action<ParseResult>> _setters = new();

    protected BaseCommand(string name, string description)
        : base(name, description)
    {
    }

    protected virtual CliOption<T> AddOption<T>(Action<T> setValue, params string[] aliases)
    {
        return AddOption(setValue, new CliOption<T>(aliases.OrderByDescending(a => a.Length).First(), aliases));
    }

    protected virtual CliOption<T> AddMultipleTokenOption<T>(Action<T> setValue, params string[] aliases)
    {
        var opt = new CliOption<T>(aliases.OrderByDescending(a => a.Length).First(), aliases);
        opt.AllowMultipleArgumentsPerToken = true;
        return AddOption(setValue, opt);
    }

    protected virtual CliOption<T> AddOption<T>(Action<T> setValue, CliOption<T> opt)
    {
        _setters[opt] = (ctx) => setValue(ctx.GetValue(opt));
        Add(opt);
        return opt;
    }

    public virtual void SetProperties(ParseResult context)
    {
        foreach (var kvp in _setters) {
            if (context.Errors.Any(e => e.SymbolResult?.Tokens?.Any(t => t.Equals(kvp.Key)) == true)) {
                continue; // skip setting values for options with errors
            }
            kvp.Value(context);
        }
    }

    public virtual ParseResult ParseAndApply(string command)
    {
        var x = this.Parse(command);
        SetProperties(x);
        return x;
    }
}