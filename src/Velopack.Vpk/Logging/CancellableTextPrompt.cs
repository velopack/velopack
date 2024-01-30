#nullable enable
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Spectre.Console;

/// <summary>
/// Represents a prompt.
/// </summary>
/// <typeparam name="T">The prompt result type.</typeparam>
public sealed class CancellableTextPrompt<T> : IPrompt<T>, IHasCulture
{
    private readonly string _prompt;
    private readonly StringComparer? _comparer;

    /// <summary>
    /// Gets or sets the prompt style.
    /// </summary>
    public Style? PromptStyle { get; set; }

    /// <summary>
    /// Gets the list of choices.
    /// </summary>
    public List<T> Choices { get; } = new List<T>();

    /// <summary>
    /// Gets or sets the culture to use when converting input to object.
    /// </summary>
    public CultureInfo? Culture { get; set; }

    /// <summary>
    /// Gets or sets the message for invalid choices.
    /// </summary>
    public string InvalidChoiceMessage { get; set; } = "[red]Please select one of the available options[/]";

    /// <summary>
    /// Gets or sets a value indicating whether input should
    /// be hidden in the console.
    /// </summary>
    public bool IsSecret { get; set; }

    /// <summary>
    /// Gets or sets the character to use while masking
    /// a secret prompt.
    /// </summary>
    public char? Mask { get; set; } = '*';

    /// <summary>
    /// Gets or sets the validation error message.
    /// </summary>
    public string ValidationErrorMessage { get; set; } = "[red]Invalid input[/]";

    /// <summary>
    /// Gets or sets a value indicating whether or not
    /// choices should be shown.
    /// </summary>
    public bool ShowChoices { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether or not
    /// default values should be shown.
    /// </summary>
    public bool ShowDefaultValue { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether or not an empty result is valid.
    /// </summary>
    public bool AllowEmpty { get; set; }

    /// <summary>
    /// Gets or sets the converter to get the display string for a choice. By default
    /// the corresponding <see cref="TypeConverter"/> is used.
    /// </summary>
    public Func<T, string>? Converter { get; set; } = TypeConverterHelper.ConvertToString;

    /// <summary>
    /// Gets or sets the validator.
    /// </summary>
    public Func<T, ValidationResult>? Validator { get; set; }

    /// <summary>
    /// Gets or sets the style in which the default value is displayed. Defaults to green when <see langword="null"/>.
    /// </summary>
    public Style? DefaultValueStyle { get; set; }

    /// <summary>
    /// Gets or sets the style in which the list of choices is displayed. Defaults to blue when <see langword="null"/>.
    /// </summary>
    public Style? ChoicesStyle { get; set; }

    /// <summary>
    /// Gets or sets the default value.
    /// </summary>
    internal DefaultPromptValue<T>? DefaultValue { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CancelableTextPrompt{T}"/> class.
    /// </summary>
    /// <param name="prompt">The prompt markup text.</param>
    /// <param name="comparer">The comparer used for choices.</param>
    public CancellableTextPrompt(string prompt, StringComparer? comparer = null)
    {
        _prompt = prompt ?? throw new System.ArgumentNullException(nameof(prompt));
        _comparer = comparer;
    }

    /// <summary>
    /// Shows the prompt and requests input from the user.
    /// </summary>
    /// <param name="console">The console to show the prompt in.</param>
    /// <returns>The user input converted to the expected type.</returns>
    /// <inheritdoc/>
    public T Show(IAnsiConsole console)
    {
        return ShowAsync(console, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public async Task<T> ShowAsync(IAnsiConsole console, CancellationToken cancellationToken)
    {
        if (console is null) {
            throw new ArgumentNullException(nameof(console));
        }

        return await console.RunExclusive(async () => {
            var promptStyle = PromptStyle ?? Style.Plain;
            var converter = Converter ?? TypeConverterHelper.ConvertToString;
            var choices = Choices.Select(choice => converter(choice)).ToList();
            var choiceMap = Choices.ToDictionary(choice => converter(choice), choice => choice, _comparer);

            WritePrompt(console);

            while (true) {
                cancellationToken.ThrowIfCancellationRequested();
                var input = await console.ReadLine(promptStyle, IsSecret, Mask, choices, cancellationToken).ConfigureAwait(false);

                // Nothing entered?
                if (string.IsNullOrWhiteSpace(input)) {
                    if (DefaultValue != null) {
                        var defaultValue = converter(DefaultValue.Value);
                        console.Write(IsSecret ? defaultValue.Mask(Mask) : defaultValue, promptStyle);
                        console.WriteLine();
                        return DefaultValue.Value;
                    }

                    if (!AllowEmpty) {
                        continue;
                    }
                }

                console.WriteLine();

                T? result;
                if (Choices.Count > 0) {
                    if (choiceMap.TryGetValue(input, out result) && result != null) {
                        return result;
                    } else {
                        console.MarkupLine(InvalidChoiceMessage);
                        WritePrompt(console);
                        continue;
                    }
                } else if (!TypeConverterHelper.TryConvertFromStringWithCulture<T>(input, Culture, out result) || result == null) {
                    console.MarkupLine(ValidationErrorMessage);
                    WritePrompt(console);
                    continue;
                }

                // Run all validators
                if (!ValidateResult(result, out var validationMessage)) {
                    console.MarkupLine(validationMessage);
                    WritePrompt(console);
                    continue;
                }

                return result;
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the prompt to the console.
    /// </summary>
    /// <param name="console">The console to write the prompt to.</param>
    private void WritePrompt(IAnsiConsole console)
    {
        if (console is null) {
            throw new ArgumentNullException(nameof(console));
        }

        var builder = new StringBuilder();
        builder.Append(_prompt.TrimEnd());

        var appendSuffix = false;
        if (ShowChoices && Choices.Count > 0) {
            appendSuffix = true;
            var converter = Converter ?? TypeConverterHelper.ConvertToString;
            var choices = string.Join("/", Choices.Select(choice => converter(choice)));
            var choicesStyle = ChoicesStyle?.ToMarkup() ?? "blue";
            builder.AppendFormat(CultureInfo.InvariantCulture, " [{0}][[{1}]][/]", choicesStyle, choices);
        }

        if (ShowDefaultValue && DefaultValue != null) {
            appendSuffix = true;
            var converter = Converter ?? TypeConverterHelper.ConvertToString;
            var defaultValueStyle = DefaultValueStyle?.ToMarkup() ?? "green";
            var defaultValue = converter(DefaultValue.Value);

            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                " [{0}]({1})[/]",
                defaultValueStyle,
                IsSecret ? defaultValue.Mask(Mask) : defaultValue);
        }

        var markup = builder.ToString().Trim();
        if (appendSuffix) {
            markup += ":";
        }

        console.Markup(markup + " ");
    }

    private bool ValidateResult(T value, [NotNullWhen(false)] out string? message)
    {
        if (Validator != null) {
            var result = Validator(value);
            if (!result.Successful) {
                message = result.Message ?? ValidationErrorMessage;
                return false;
            }
        }

        message = null;
        return true;
    }
}

internal static class TypeConverterHelper
{
    public static string ConvertToString<T>(T input)
    {
        var result = GetTypeConverter<T>().ConvertToInvariantString(input);
        if (result == null) {
            throw new InvalidOperationException("Could not convert input to a string");
        }

        return result;
    }

    public static bool TryConvertFromString<T>(string input, [MaybeNull] out T? result)
    {
        try {
            result = (T?) GetTypeConverter<T>().ConvertFromInvariantString(input);
            return true;
        } catch {
            result = default;
            return false;
        }
    }

    public static bool TryConvertFromStringWithCulture<T>(string input, CultureInfo? info, [MaybeNull] out T? result)
    {
        try {
            if (info == null) {
                return TryConvertFromString<T>(input, out result);
            } else {
                result = (T?) GetTypeConverter<T>().ConvertFromString(null!, info, input);
            }

            return true;
        } catch {
            result = default;
            return false;
        }
    }

    public static TypeConverter GetTypeConverter<T>()
    {
        var converter = TypeDescriptor.GetConverter(typeof(T));
        if (converter != null) {
            return converter;
        }

        var attribute = typeof(T).GetCustomAttribute<TypeConverterAttribute>();
        if (attribute != null) {
            var type = Type.GetType(attribute.ConverterTypeName, false, false);
            if (type != null) {
                converter = Activator.CreateInstance(type) as TypeConverter;
                if (converter != null) {
                    return converter;
                }
            }
        }

        throw new InvalidOperationException("Could not find type converter");
    }
}

internal sealed class DefaultPromptValue<T>
{
    public T Value { get; }

    public DefaultPromptValue(T value)
    {
        Value = value;
    }
}

/// <summary>
/// Contains extension methods for <see cref="IAnsiConsole"/>.
/// </summary>
public static partial class AnsiConsoleExtensions
{
    internal static async Task<string> ReadLine(this IAnsiConsole console, Style? style, bool secret, char? mask, IEnumerable<string>? items = null, CancellationToken cancellationToken = default)
    {
        if (console is null) {
            throw new ArgumentNullException(nameof(console));
        }

        style ??= Style.Plain;
        var text = string.Empty;

        var autocomplete = new List<string>(items ?? Enumerable.Empty<string>());

        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            var rawKey = await console.Input.ReadKeyAsync(true, cancellationToken).ConfigureAwait(false);
            if (rawKey == null) {
                continue;
            }

            var key = rawKey.Value;
            if (key.Key == ConsoleKey.Enter) {
                return text;
            }

            if (key.Key == ConsoleKey.Tab && autocomplete.Count > 0) {
                var autoCompleteDirection = key.Modifiers.HasFlag(ConsoleModifiers.Shift)
                    ? AutoCompleteDirection.Backward
                    : AutoCompleteDirection.Forward;
                var replace = AutoComplete(autocomplete, text, autoCompleteDirection);
                if (!string.IsNullOrEmpty(replace)) {
                    // Render the suggestion
                    console.Write("\b \b".Repeat(text.Length), style);
                    console.Write(replace);
                    text = replace;
                    continue;
                }
            }

            if (key.Key == ConsoleKey.Backspace) {
                if (text.Length > 0) {
                    text = text.Substring(0, text.Length - 1);
                    console.Write("\b \b");
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar)) {
                text += key.KeyChar.ToString();
                var output = key.KeyChar.ToString();
                console.Write(secret ? output.Mask(mask) : output, style);
            }
        }
    }

    internal static string Repeat(this string text, int count)
    {
        if (text is null) {
            throw new ArgumentNullException(nameof(text));
        }

        if (count <= 0) {
            return string.Empty;
        }

        if (count == 1) {
            return text;
        }

        return string.Concat(Enumerable.Repeat(text, count));
    }

    private static string AutoComplete(List<string> autocomplete, string text, AutoCompleteDirection autoCompleteDirection)
    {
        var found = autocomplete.Find(i => i == text);
        var replace = string.Empty;

        if (found == null) {
            // Get the closest match
            var next = autocomplete.Find(i => i.StartsWith(text, true, CultureInfo.InvariantCulture));
            if (next != null) {
                replace = next;
            } else if (string.IsNullOrEmpty(text)) {
                // Use the first item
                replace = autocomplete[0];
            }
        } else {
            // Get the next match
            replace = GetAutocompleteValue(autoCompleteDirection, autocomplete, found);
        }

        return replace;
    }

    private static string GetAutocompleteValue(AutoCompleteDirection autoCompleteDirection, IList<string> autocomplete, string found)
    {
        var foundAutocompleteIndex = autocomplete.IndexOf(found);
        var index = autoCompleteDirection switch {
            AutoCompleteDirection.Forward => foundAutocompleteIndex + 1,
            AutoCompleteDirection.Backward => foundAutocompleteIndex - 1,
            _ => throw new ArgumentOutOfRangeException(nameof(autoCompleteDirection), autoCompleteDirection, null),
        };

        if (index >= autocomplete.Count) {
            index = 0;
        }

        if (index < 0) {
            index = autocomplete.Count - 1;
        }

        return autocomplete[index];
    }

    private enum AutoCompleteDirection
    {
        Forward,
        Backward,
    }
}