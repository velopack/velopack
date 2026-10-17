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
        /// In this case, any local packages on disk newer than the downloaded version will be deleted.
        /// </summary>
        public bool IsDowngrade { get; }

        /// <summary>
        /// Create a new instance of <see cref="UpdateInfo"/>
        /// </summary>
        public UpdateInfo(VelopackAsset targetRelease, bool isDowngrade)
        {
            TargetFullRelease = targetRelease;
            IsDowngrade = isDowngrade;
        }

        /// <summary>
        /// Implicitly convert an <see cref="UpdateInfo"/> to a <see cref="VelopackAsset"/>.
        /// </summary>
        public static implicit operator VelopackAsset(UpdateInfo updateInfo) => updateInfo.TargetFullRelease;
    }
}
