using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Velopack
{
    [ExcludeFromCodeCoverage]
    internal static class ProcessStartExtensions
    {

#if NET5_0_OR_GREATER
        public static void AppendArgumentListSafe(this ProcessStartInfo psi, IEnumerable<string> args, out string debug)
        {
            foreach (var a in args) {
                psi.ArgumentList.Add(a);
            }
            var sb = new StringBuilder();
            AppendArgumentsTo(sb, args);
            debug = sb.ToString();
        }

#else
        public static void AppendArgumentListSafe(this ProcessStartInfo psi, IEnumerable<string> args, out string debug)
        {
            var sb = new StringBuilder();
            AppendArgumentsTo(sb, args);
            psi.Arguments = sb.ToString();
            debug = psi.Arguments;
        }
#endif

        public static Process StartRedirectOutputToILogger(this ProcessStartInfo psi, ILogger log, LogLevel outputLevel)
        {
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.UseShellExecute = false;

            var p = Process.Start(psi);
            p.BeginErrorReadLine();
            p.BeginOutputReadLine();

            p.ErrorDataReceived += (o, e) => {
                if (e.Data != null) {
                    log.LogError(e.Data);
                }
            };

            p.OutputDataReceived += (o, e) => {
                if (e.Data != null) {
                    log.Log(outputLevel, e.Data);
                }
            };

            return p;
        }

        public static string Output(this ProcessStartInfo psi, int timeoutMs)
        {
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.UseShellExecute = false;

            var p = Process.Start(psi);
            p.BeginErrorReadLine();
            p.BeginOutputReadLine();

            var sb = new StringBuilder();

            p.ErrorDataReceived += (o, e) => {
                if (e.Data != null) {
                    sb.AppendLine(e.Data);
                }
            };

            p.OutputDataReceived += (o, e) => {
                if (e.Data != null) {
                    sb.AppendLine(e.Data);
                }
            };

            if (!p.WaitForExit(timeoutMs)) {
                throw new TimeoutException($"Process timed out after {timeoutMs}ms.");
            }

            if (p.ExitCode != 0) {
                throw new Exception($"Process exited with code {p.ExitCode}.");
            }

            return sb.ToString();
        }


        // https://source.dot.net/#System.Diagnostics.Process/System/Diagnostics/ProcessStartInfo.cs,204
        private static void AppendArgumentsTo(StringBuilder stringBuilder, IEnumerable<string> args)
        {
            if (args != null && args.Any()) {
                foreach (string argument in args) {
                    AppendArgument(stringBuilder, argument);
                }
            }
        }

        // https://source.dot.net/#System.Diagnostics.Process/src/libraries/System.Private.CoreLib/src/System/PasteArguments.cs,624678ba1465e776
        private static void AppendArgument(StringBuilder stringBuilder, string argument)
        {
            if (stringBuilder.Length != 0) {
                stringBuilder.Append(' ');
            }

            // Parsing rules for non-argv[0] arguments:
            //   - Backslash is a normal character except followed by a quote.
            //   - 2N backslashes followed by a quote ==> N literal backslashes followed by unescaped quote
            //   - 2N+1 backslashes followed by a quote ==> N literal backslashes followed by a literal quote
            //   - Parsing stops at first whitespace outside of quoted region.
            //   - (post 2008 rule): A closing quote followed by another quote ==> literal quote, and parsing remains in quoting mode.
            if (argument.Length != 0 && ContainsNoWhitespaceOrQuotes(argument)) {
                // Simple case - no quoting or changes needed.
                stringBuilder.Append(argument);
            } else {
                stringBuilder.Append(Quote);
                int idx = 0;
                while (idx < argument.Length) {
                    char c = argument[idx++];
                    if (c == Backslash) {
                        int numBackSlash = 1;
                        while (idx < argument.Length && argument[idx] == Backslash) {
                            idx++;
                            numBackSlash++;
                        }

                        if (idx == argument.Length) {
                            // We'll emit an end quote after this so must double the number of backslashes.
                            stringBuilder.Append(Backslash, numBackSlash * 2);
                        } else if (argument[idx] == Quote) {
                            // Backslashes will be followed by a quote. Must double the number of backslashes.
                            stringBuilder.Append(Backslash, numBackSlash * 2 + 1);
                            stringBuilder.Append(Quote);
                            idx++;
                        } else {
                            // Backslash will not be followed by a quote, so emit as normal characters.
                            stringBuilder.Append(Backslash, numBackSlash);
                        }

                        continue;
                    }

                    if (c == Quote) {
                        // Escape the quote so it appears as a literal. This also guarantees that we won't end up generating a closing quote followed
                        // by another quote (which parses differently pre-2008 vs. post-2008.)
                        stringBuilder.Append(Backslash);
                        stringBuilder.Append(Quote);
                        continue;
                    }

                    stringBuilder.Append(c);
                }

                stringBuilder.Append(Quote);
            }
        }

        private static bool ContainsNoWhitespaceOrQuotes(string s)
        {
            for (int i = 0; i < s.Length; i++) {
                char c = s[i];
                if (char.IsWhiteSpace(c) || c == Quote) {
                    return false;
                }
            }

            return true;
        }

        private const char Quote = '\"';
        private const char Backslash = '\\';
    }
}
