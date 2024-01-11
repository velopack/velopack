namespace Divergic.Logging.Xunit
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    ///     The <see cref="SerializerSettings" />
    ///     class provides access to settings for JSON serialization.
    /// </summary>
    public static class SerializerSettings
    {
        private static JsonSerializerOptions BuildSerializerSettings()
        {
            var settings = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = true
            };

            return settings;
        }

        /// <summary>
        ///     Gets the default serializer settings.
        /// </summary>
        public static JsonSerializerOptions Default { get; } = BuildSerializerSettings();
    }
}