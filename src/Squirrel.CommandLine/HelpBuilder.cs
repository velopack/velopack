using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Help;
using System.Drawing;
using System.Linq;
using Squirrel.CommandLine.Windows;
using static System.Net.Mime.MediaTypeNames;
using SCHelpBuilder = System.CommandLine.Help.HelpBuilder;
namespace Squirrel.CommandLine
{
    internal class HelpBuilder : SCHelpBuilder
    {
        public HelpBuilder(LocalizationResources localizationResources, int maxWidth = int.MaxValue)
            : base(localizationResources, maxWidth)
        {
        }

        public override void Write(HelpContext context)
        {
            if (context.Command is RootCommand rootCommand) {

                //Write global options
                context.Output.WriteLine("[ Global Options ]");
                List<TwoColumnHelpRow> globalOptionRows = new List<TwoColumnHelpRow>();
                if (rootCommand.Children.OfType<Option>().FirstOrDefault(x => x.HasAlias("--help")) is { } helpOption) {
                    globalOptionRows.Add(context.HelpBuilder.GetTwoColumnRow(helpOption, context));
                }
                globalOptionRows.Add(context.HelpBuilder.GetTwoColumnRow(SquirrelHost.PlatformOption, context));
                globalOptionRows.Add(context.HelpBuilder.GetTwoColumnRow(SquirrelHost.VerboseOption, context));
                context.HelpBuilder.WriteColumns(globalOptionRows, context);
                
                context.Output.WriteLine();

                //Group commands
                foreach(var commandGrouping in rootCommand.Children.OfType<ICommand>().GroupBy(x => x.HelpGroupName)){
                    context.Output.WriteLine($"[ {commandGrouping.Key} ]");

                    foreach (var command in commandGrouping.OfType<Command>()) {
                        context.Output.WriteLine();
                        
                        var fc = Console.ForegroundColor;
                        Console.ForegroundColor = ConsoleColor.Magenta;
                        context.Output.Write(command.Name);
                        Console.ForegroundColor = fc;
                        
                        context.Output.WriteLine($": {command.Description}");

                        List<TwoColumnHelpRow> optionHelpRows = new List<TwoColumnHelpRow>();
                        foreach(var option in command.Options) {
                            optionHelpRows.Add(context.HelpBuilder.GetTwoColumnRow(option, context));
                        }
                        context.HelpBuilder.WriteColumns(optionHelpRows, context);
                    }
                }
            } else {
                base.Write(context);
            }
        }
    }
}
