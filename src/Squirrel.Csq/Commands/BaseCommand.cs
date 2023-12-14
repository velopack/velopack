namespace Squirrel.Csq.Commands;

public class BaseCommand : CliCommand
{
    public RID TargetRuntime { get; set; }

    public string ReleaseDirectory { get; private set; }

    protected CliOption<DirectoryInfo> ReleaseDirectoryOption { get; private set; }

    private Dictionary<CliOption, Action<ParseResult>> _setters = new();

    protected BaseCommand(string name, string description)
        : base(name, description)
    {
        ReleaseDirectoryOption = AddOption<DirectoryInfo>((v) => ReleaseDirectory = v.ToFullNameOrNull(), "-o", "--outputDir")
            .SetDescription("Output directory for Squirrel packages.")
            .SetArgumentHelpName("DIR")
            .SetDefault(new DirectoryInfo(".\\Releases"));
    }

    public DirectoryInfo GetReleaseDirectory()
    {
        var di = new DirectoryInfo(ReleaseDirectory);
        if (!di.Exists) di.Create();
        return di;
    }

    protected virtual CliOption<T> AddOption<T>(Action<T> setValue, params string[] aliases)
    {
        return AddOption(setValue, new CliOption<T>(aliases.OrderBy(a => a.Length).First(), aliases));
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