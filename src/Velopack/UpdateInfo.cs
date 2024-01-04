namespace Velopack
{
    /// <summary>
    /// Holds information about the current version and pending updates, such as how many there are, and access to release notes.
    /// </summary>
    public class UpdateInfo
    {
        /// <summary>
        /// The available version that we are updating to.
        /// </summary>
        public ReleaseEntry TargetFullRelease { get; }

        /// <summary>
        /// The base release that we are to apply delta updates from. If null, we can try doing a delta update from
        /// the currently installed version.
        /// </summary>
        public ReleaseEntry BaseRelease { get; }

        /// <summary>
        /// The list of delta versions between the current version and <see cref="TargetFullRelease"/>.
        /// These will attempt to be tried applied in order.
        /// </summary>
        public ReleaseEntry[] DeltasToTarget { get; } = new ReleaseEntry[0];

        /// <summary>
        /// Create a new instance of <see cref="UpdateInfo"/>
        /// </summary>
        public UpdateInfo(ReleaseEntry targetRelease, ReleaseEntry deltaBaseRelease = null, ReleaseEntry[] deltasToTarget = null)
        {
            TargetFullRelease = targetRelease;
            BaseRelease = deltaBaseRelease;
            DeltasToTarget = deltasToTarget ?? new ReleaseEntry[0];
        }

        // /// <summary>
        // /// Retrieves all the release notes for pending packages (ie. <see cref="ReleasesToApply"/>)
        // /// </summary>
        // public Dictionary<ReleaseEntry, string> FetchReleaseNotes(ReleaseNotesFormat format)
        // {
        //    return ReleasesToApply
        //        .SelectMany(x => {
        //            try {
        //                var releaseNotes = x.GetReleaseNotes(PackageDirectory, format);
        //                return EnumerableExtensions.Return(Tuple.Create(x, releaseNotes));
        //            } catch (Exception ex) {
        //                return Enumerable.Empty<Tuple<ReleaseEntry, string>>();
        //            }
        //        })
        //        .ToDictionary(k => k.Item1, v => v.Item2);
        // }
    }
}
