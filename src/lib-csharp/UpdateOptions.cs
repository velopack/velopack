namespace Velopack
{
    /// <summary>
    /// Options to customise the behaviour of <see cref="UpdateManager"/>.
    /// </summary>
    public class UpdateOptions
    {
        /// <summary>
        /// Allows UpdateManager to update to a version that's lower than the current version (i.e. downgrading).
        /// This could happen if a release has bugs and was retracted from the release feed, or if you're using 
        /// <see cref="ExplicitChannel"/> to switch channels to another channel where the latest version on that 
        /// channel is lower than the current version.
        /// </summary>
        public bool AllowVersionDowngrade { get; set; }

        /// <summary>
        /// <b>This option should usually be left null</b>. Overrides the default channel used to fetch updates. 
        /// The default channel will be whatever channel was specified on the command line when building this release. 
        /// For example, if the current release was packaged with '--channel beta', then the default channel will be 'beta'.
        /// This allows users to automatically receive updates from the same channel they installed from. This options
        /// allows you to explicitly switch channels, for example if the user wished to switch back to the 'stable' channel
        /// without having to reinstall the application.
        /// </summary>
        public string? ExplicitChannel { get; set; }
        /// <summary>
        /// Optionally validate if the update package files are signed with a trusted cert
        /// </summary>
        public bool ValidatePackageIsSigned { get; set; }
    }
}
