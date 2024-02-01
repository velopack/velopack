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
        public VelopackAsset TargetFullRelease { get; }

        /// <summary>
        /// True if the update is a version downgrade or lateral move (such as when switching channels to the same version number).
        /// In this case, only full updates are allowed, and any local packages on disk newer than the downloaded version will be
        /// deleted.
        /// </summary>
        public bool IsDowngrade { get; }

        /// <summary>
        /// The base release that we are to apply delta updates from. If null, we can try doing a delta update from
        /// the currently installed version.
        /// </summary>
        public VelopackAsset? BaseRelease { get; }

        /// <summary>
        /// The list of delta versions between the current version and <see cref="TargetFullRelease"/>.
        /// These will be applied in order.
        /// </summary>
        public VelopackAsset[] DeltasToTarget { get; } = new VelopackAsset[0];

        /// <summary>
        /// Create a new instance of <see cref="UpdateInfo"/>
        /// </summary>
        public UpdateInfo(VelopackAsset targetRelease, bool isDowngrade, VelopackAsset? deltaBaseRelease = null, VelopackAsset[]? deltasToTarget = null)
        {
            TargetFullRelease = targetRelease;
            IsDowngrade = isDowngrade;
            BaseRelease = deltaBaseRelease;
            DeltasToTarget = deltasToTarget ?? new VelopackAsset[0];
        }
    }
}
