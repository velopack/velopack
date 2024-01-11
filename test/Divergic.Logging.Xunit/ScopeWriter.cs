namespace Divergic.Logging.Xunit
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.Text.Json;
    using global::Xunit.Abstractions;
    using Microsoft.Extensions.Logging;

    internal class ScopeWriter : IDisposable
    {
        private readonly string _category;
        private readonly LoggingConfig _config;
        private readonly int _depth;
        private readonly Action _onScopeEnd;
        private readonly ITestOutputHelper _output;
        private readonly object? _state;
        private string _scopeMessage = string.Empty;
        private string _structuredStateData = string.Empty;

        public ScopeWriter(
            ITestOutputHelper output,
            object? state,
            int depth,
            string category,
            Action onScopeEnd,
            LoggingConfig config)
        {
            _output = output;
            _state = state;
            _depth = depth;
            _category = category;
            _onScopeEnd = onScopeEnd;
            _config = config;

            DetermineScopeStateMessage();

            var scopeStartMessage = BuildScopeStateMessage(false);

            WriteLog(_depth, scopeStartMessage);

            if (string.IsNullOrWhiteSpace(_structuredStateData) == false)
            {
                // Add the padding to the structured data
                var structuredLines =
                    _structuredStateData.Split(new[] {Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries);

                WriteLog(_depth + 1, "Scope data: ");

                foreach (var structuredLine in structuredLines)
                {
                    WriteLog(_depth + 1, structuredLine);
                }
            }
        }

        private void WriteLog(int depth, string message)
        {
            var formattedMessage = _config.ScopeFormatter.Format(depth, _category, LogLevel.Information, 0, message, null);

            _output.WriteLine(formattedMessage);

            // Write the message to the output window
            Trace.WriteLine(formattedMessage);
        }

        public void Dispose()
        {
            var scopeStartMessage = BuildScopeStateMessage(true);

            _output.WriteLine(scopeStartMessage);

            _onScopeEnd.Invoke();
        }
        
        private string BuildScopeStateMessage(bool isScopeEnd)
        {
            var endScopeMarker = isScopeEnd ? "/" : string.Empty;
            const string format = "<{0}{1}>";

            var message = string.Format(CultureInfo.InvariantCulture, format, endScopeMarker, _scopeMessage);

            var formattedMessage =
                _config.ScopeFormatter.Format(_depth, _category, LogLevel.Information, 0, message, null);

            return formattedMessage;
        }

        private void DetermineScopeStateMessage()
        {
            const string scopeMarker = "Scope: ";
            var defaultScopeMessage = "Scope " + (_depth + 1);

            if (_state == null)
            {
                _scopeMessage = defaultScopeMessage;
            }
            else if (_state is string state)
            {
                if (string.IsNullOrWhiteSpace(state))
                {
                    _scopeMessage = defaultScopeMessage;
                }
                else
                {
                    _scopeMessage = scopeMarker + state;
                }
            }
            else if (_state.GetType().IsValueType)
            {
                _scopeMessage = scopeMarker + _state;
            }
            else
            {
                try
                {
                    // The data is probably a complex object or a structured log entry
                    _structuredStateData = JsonSerializer.Serialize(_state, SerializerSettings.Default);
                }
                catch (JsonException ex)
                {
                    _structuredStateData = ex.ToString();
                }

                _scopeMessage = defaultScopeMessage;
            }
        }
    }
}