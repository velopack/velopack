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

        public LongHelpCommand() : this("--help", new[] { "-h", "-H", "--vhelp" })
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

                        var columns = GetTwoColumnRow(argument);

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
                    foreach (var argument in command.Subcommands) {
                        var columns = GetTwoColumnRow(argument);
                        commandsTable.AddRow(new Markup($"[bold]{columns.FirstColumnText}[/]"), new Text(columns.SecondColumnText));
                    }

                    output.Add(new Padder(commandsTable, pad));
                }

                AnsiConsole.Write(new RenderableCollection(output));
                return 0;
            }

            public TwoColumnHelpRow GetTwoColumnRow(CliSymbol symbol)
            {
                if (symbol is null) {
                    throw new ArgumentNullException(nameof(symbol));
                }

                if (symbol is CliOption or CliCommand) {
                    return GetOptionOrCommandRow();
                } else if (symbol is CliArgument argument) {
                    return GetCommandArgumentRow(argument);
                } else {
                    throw new NotSupportedException($"Symbol type {symbol.GetType()} is not supported.");
                }

                TwoColumnHelpRow GetOptionOrCommandRow()
                {
                    var firstColumnText = symbol is CliOption option
                                ? Default.GetOptionUsageLabel(option)
                                : Default.GetCommandUsageLabel((CliCommand) symbol);

                    var symbolDescription = symbol.Description ?? string.Empty;

                    //in case symbol description is customized, do not output default value
                    //default value output is not customizable for identifier symbols
                    var defaultValueDescription = GetSymbolDefaultValue(symbol);

                    var secondColumnText = $"{symbolDescription} {defaultValueDescription}".Trim();

                    return new TwoColumnHelpRow(firstColumnText, secondColumnText);
                }

                TwoColumnHelpRow GetCommandArgumentRow(CliArgument argument)
                {
                    var firstColumnText = Default.GetArgumentUsageLabel(argument);

                    var argumentDescription = Default.GetArgumentDescription(argument);

                    var defaultValueDescription =
                        argument.HasDefaultValue
                            ? $"[{GetArgumentDefaultValue(argument, true)}]"
                            : "";

                    var secondColumnText = $"{argumentDescription} {defaultValueDescription}".Trim();

                    return new TwoColumnHelpRow(firstColumnText, secondColumnText);
                }

                string GetSymbolDefaultValue(CliSymbol symbol)
                {
                    IList<CliArgument> arguments = symbol.Arguments();
                    var defaultArguments = arguments.Where(x => !x.Hidden && x.HasDefaultValue).ToArray();

                    if (defaultArguments.Length == 0) return "";

                    var isSingleArgument = defaultArguments.Length == 1;
                    var argumentDefaultValues = defaultArguments
                        .Select(argument => GetArgumentDefaultValue(argument, isSingleArgument));
                    return $"[{string.Join(", ", argumentDefaultValues)}]";
                }
            }

            private string GetArgumentDefaultValue(
                CliArgument argument,
                bool displayArgumentName)
            {
                string label = displayArgumentName
                                  ? "default"
                                  : argument.Name;

                string? displayedDefaultValue = null;

                displayedDefaultValue ??= Default.GetArgumentDefaultValue(argument);

                if (string.IsNullOrWhiteSpace(displayedDefaultValue)) {
                    return "";
                }

                return $"{label}: {displayedDefaultValue}";
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

        internal static IList<CliArgument> Arguments(this CliSymbol symbol)
        {
            switch (symbol) {
            case CliOption option:
                //throw new NotSupportedException();
                return new[]
                {
                    (CliArgument)option.GetType().GetProperty("Argument", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(symbol)
                };
            case CliCommand command:
                return command.Arguments;
            case CliArgument argument:
                return new[]
                {
                    argument
                };
            default:
                throw new NotSupportedException();
            }
        }
    }
}
