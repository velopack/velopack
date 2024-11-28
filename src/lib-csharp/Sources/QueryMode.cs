namespace Velopack.Sources
{
    /// <summary>
    /// Mode determining when query string parameters should be appended to a URL.
    /// </summary>
    public enum QueryMode
    {
        /// <summary>
        /// Default value. No query string parameters will be appended to the URL.
        /// </summary>
        None,

        /// <summary>
        /// No query string parameters will be appended to the URL.
        /// </summary>
        NoQuery,

        /// <summary>
        /// Only release feed query string parameters will be appended to the URL.
        /// </summary>
        ReleaseFeedOnly,

        /// <summary>
        /// All request query string parameters will be appended to the URL.
        /// </summary>
        AllRequests
    }
}
