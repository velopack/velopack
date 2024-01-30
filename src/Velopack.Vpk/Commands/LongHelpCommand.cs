using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.Reflection;
using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;
using Velopack.Vpk.Logging;
using static System.CommandLine.Help.HelpBuilder;

namespace Velopack.Vpk.Commands
{
    public class LongHelpCommand : CliOption<bool>
    {
        private CliAction _action;

        public LongHelpCommand() : this("--help", ["-h", "-H", "--vhelp"])
        {
        }

        public LongHelpCommand(string name, params string[] aliases)
            : base(name, aliases)
        {
            Recursive = true;
            Description = "Show help (-h) or extended help (-H).";
            Arity = ArgumentArity.Zero;
        }

        public override CliAction Action {
            get => _action ??= new LongHelpAction();
            set => _action = value ?? throw new ArgumentNullException(nameof(value));
        }

        public sealed class LongHelpAction : SynchronousCliAction
        {
            public override int Invoke(ParseResult parseResult)
            {
                var longHelpMode = parseResult.Tokens.Any(t => t.Value == "-H" || t.Value == "--vhelp");
                var command = parseResult.CommandResult.Command;

                var pad = new Padding(2, 0);

                List<IRenderable> output =
                [
                    new Text("Description:"),
                    Text.NewLine,
                    new Padder(new Markup($"[bold]{command.Description}[/]"), pad),
                    Text.NewLine,
                    new Text("Usage:"),
                    Text.NewLine,
                    new Padder(new Markup($"[bold]{Markup.Escape(GetUsage(command))}[/]"), pad),
                ];

                Table CreateTable()
                {
                    var t = new Table();
                    t.NoBorder();
                    t.LeftAligned();
                    t.HideHeaders();
                    t.Collapse();
                    t.AddColumn(new TableColumn("Name") { Padding = pad });
                    t.AddColumn("Description");
                    return t;
                }

                int hiddenOptions = 0;

                void AddOptionRows(Table table, IEnumerable<CliOption> options)
                {
                    foreach (var argument in options) {
                        if (argument.Hidden && !longHelpMode) {
                            hiddenOptions++;
                            continue;
                        }

                        var columns = GetTwoColumnRowOption(argument);
                        var aliasText = columns.FirstColumnText;
                        var argIdx = aliasText.IndexOf(" <");
                        if (argIdx > 0) {
                            aliasText = $"[bold]{aliasText.Substring(0, argIdx)}[/]{aliasText.Substring(argIdx)}";
                        } else {
                            aliasText = $"[bold]{aliasText}[/]";
                        }
                        aliasText = aliasText.Replace("(REQUIRED)", "[red](REQ)[/]");

                        var descriptionText = Markup.Escape(columns.SecondColumnText);
                        if (longHelpMode && command is BaseCommand bc) {
                            var envVarName = bc.GetEnvVariableName(argument);
                            if (envVarName != null) {
                                descriptionText = $"[italic]ENV=[bold blue]{envVarName}[/][/]  " + descriptionText;
                            }
                        }
                        table.AddRow(new Markup(aliasText), new Markup(descriptionText));
                    }
                }

                // look for global options (only rendered if long mode)
                var globalOptions = new List<CliOption>();
                foreach (var p in command.Parents) {
                    CliCommand parentCommand = null;
                    if ((parentCommand = p as CliCommand) is not null) {
                        if (parentCommand.HasOptions()) {
                            foreach (var option in parentCommand.Options) {
                                if (option is { Recursive: true, Hidden: false }) {
                                    if (longHelpMode) {
                                        globalOptions.Add(option);
                                    } else {
                                        hiddenOptions++;
                                    }
                                }
                            }
                        }
                    }
                }

                if (globalOptions.Any()) {
                    output.Add(Text.NewLine);
                    output.Add(new Text($"Global Options:"));
                    output.Add(Text.NewLine);
                    var globalOptionsTable = CreateTable();
                    AddOptionRows(globalOptionsTable, globalOptions);
                    output.Add(new Padder(globalOptionsTable, pad));
                }

                if (command.HasOptions()) {
                    output.Add(Text.NewLine);
                    output.Add(new Text($"Options:"));
                    output.Add(Text.NewLine);
                    var optionsTable = CreateTable();
                    AddOptionRows(optionsTable, command.Options);
                    output.Add(new Padder(optionsTable, pad));
                }

                if (hiddenOptions > 0) {
                    output.Add(Text.NewLine);
                    output.Add(new Markup($"[italic]([red]*[/]) {hiddenOptions} option(s) were hidden. Use [bold]-H / --vhelp[/] to show all options.[/]"));
                    output.Add(Text.NewLine);
                }

                if (command.HasSubcommands()) {
                    output.Add(Text.NewLine);
                    output.Add(new Text("Commands:"));
                    output.Add(Text.NewLine);

                    var commandsTable = CreateTable();
                    foreach (var cmd in command.Subcommands) {
                        var columns = GetTwoColumnRowCommand(cmd);
                        commandsTable.AddRow(new Markup($"[bold]{columns.FirstColumnText}[/]"), new Text(columns.SecondColumnText));
                    }

                    output.Add(new Padder(commandsTable, pad));
                }

                AnsiConsole.Write(new RenderableCollection(output));
                return 0;
            }

            public TwoColumnHelpRow GetTwoColumnRowCommand(CliCommand command)
            {
                if (command is null) {
                    throw new ArgumentNullException(nameof(command));
                }
                var firstColumnText = Default.GetCommandUsageLabel(command);
                var symbolDescription = command.Description ?? string.Empty;
                var secondColumnText = symbolDescription.Trim();
                return new TwoColumnHelpRow(firstColumnText, secondColumnText);
            }

            public TwoColumnHelpRow GetTwoColumnRowOption(CliOption symbol)
            {
                if (symbol is null) {
                    throw new ArgumentNullException(nameof(symbol));
                }

                var firstColumnText = Default.GetOptionUsageLabel(symbol);
                var symbolDescription = symbol.Description ?? string.Empty;

                var defaultValueDescription = "";
                if (symbol.HasDefaultValue) {
                    // TODO: this is a hack, but the property is internal. what do you want me to do?
                    var argument = symbol.GetType()?.GetProperty("Argument", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(symbol) as CliArgument;
                    if (argument is not null) {
                        defaultValueDescription = $"[default: {Default.GetArgumentDefaultValue(argument)}]";
                    }
                }

                var secondColumnText = $"{symbolDescription} {defaultValueDescription}".Trim();
                return new TwoColumnHelpRow(firstColumnText, secondColumnText);
            }

            private string GetUsage(CliCommand command)
            {
                return string.Join(" ", GetUsageParts().Where(x => !string.IsNullOrWhiteSpace(x)));

                IEnumerable<string> GetUsageParts()
                {
                    bool displayOptionTitle = false;

                    IEnumerable<CliCommand> parentCommands =
                        command
                            .RecurseWhileNotNull(c => c.Parents.OfType<CliCommand>().FirstOrDefault())
                            .Reverse();

                    foreach (var parentCommand in parentCommands) {
                        if (!displayOptionTitle) {
                            displayOptionTitle = parentCommand.HasOptions() && parentCommand.Options.Any(x => x.Recursive && !x.Hidden);
                        }

                        yield return parentCommand.Name;

                        if (parentCommand.HasArguments()) {
                            yield return FormatArgumentUsage(parentCommand.Arguments);
                        }
                    }

                    var hasCommandWithHelp = command.HasSubcommands() && command.Subcommands.Any(x => !x.Hidden);

                    if (hasCommandWithHelp) {
                        yield return "[command]";
                    }

                    displayOptionTitle = displayOptionTitle || (command.HasOptions() && command.Options.Any(x => !x.Hidden));

                    if (displayOptionTitle) {
                        yield return "[options]";
                    }

                    if (!command.TreatUnmatchedTokensAsErrors) {
                        yield return "[[--] <additional arguments>...]]";
                    }
                }
            }

            private string FormatArgumentUsage(IList<CliArgument> arguments)
            {
                var sb = new StringBuilder(arguments.Count * 100);

                var end = default(List<char>);

                for (var i = 0; i < arguments.Count; i++) {
                    var argument = arguments[i];
                    if (argument.Hidden) {
                        continue;
                    }

                    var arityIndicator =
                        argument.Arity.MaximumNumberOfValues > 1
                            ? "..."
                            : "";

                    var isOptional = IsOptional(argument);

                    if (isOptional) {
                        sb.Append($"[<{argument.Name}>{arityIndicator}");
                        (end ??= new()).Add(']');
                    } else {
                        sb.Append($"<{argument.Name}>{arityIndicator}");
                    }

                    sb.Append(' ');
                }

                if (sb.Length > 0) {
                    sb.Length--;

                    if (end is { }) {
                        while (end.Count > 0) {
                            sb.Append(end[end.Count - 1]);
                            end.RemoveAt(end.Count - 1);
                        }
                    }
                }

                return sb.ToString();

                bool IsOptional(CliArgument argument) =>
                    argument.Arity.MinimumNumberOfValues == 0;
            }
        }
    }

    public static class HelpExtensions
    {
        public static bool HasArguments(this CliCommand command) => command.Arguments?.Count > 0;
        public static bool HasSubcommands(this CliCommand command) => command.Subcommands?.Count > 0;
        public static bool HasOptions(this CliCommand command) => command.Options?.Count > 0;

        internal static IEnumerable<T> RecurseWhileNotNull<T>(
            this T? source,
            Func<T, T?> next)
            where T : class
        {
            while (source is not null) {
                yield return source;

                source = next(source);
            }
        }
    }
}
