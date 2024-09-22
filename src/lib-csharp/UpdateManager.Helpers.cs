using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace Velopack
{
    public partial class UpdateManager
    {
        /// <summary>
        /// This will exit your app immediately, apply updates, and then optionally relaunch the app using the specified 
        /// restart arguments. If you need to save state or clean up, you should do that before calling this method. 
        /// The user may be prompted during the update, if the update requires additional frameworks to be installed etc.
        /// You can check if there are pending updates by checking <see cref="UpdatePendingRestart"/>.
        /// </summary>
        /// <param name="toApply">The target release to apply. Can be left null to auto-apply the newest downloaded release.</param>
        /// <param name="restartArgs">The arguments to pass to the application when it is restarted.</param>
        public void ApplyUpdatesAndRestart(VelopackAsset? toApply, string[]? restartArgs = null)
        {
            WaitExitThenApplyUpdates(toApply, silent: false, restart: true, restartArgs);
            Environment.Exit(0);
        }

        /// <summary>
        /// This will exit your app immediately, apply updates, and then optionally relaunch the app using the specified 
        /// restart arguments. If you need to save state or clean up, you should do that before calling this method. 
        /// The user may be prompted during the update, if the update requires additional frameworks to be installed etc.
        /// You can check if there are pending updates by checking <see cref="UpdatePendingRestart"/>.
        /// </summary>
        /// <param name="toApply">The target release to apply. Can be left null to auto-apply the newest downloaded release.</param>
        public void ApplyUpdatesAndExit(VelopackAsset? toApply)
        {
            WaitExitThenApplyUpdates(toApply, silent: true, restart: false);
            Environment.Exit(0);
        }
        
        /// <summary>
        /// This will launch the Velopack updater and tell it to wait for this program to exit gracefully.
        /// You should then clean up any state and exit your app. The updater will apply updates and then
        /// optionally restart your app. The updater will only wait for 60 seconds before giving up.
        /// You can check if there are pending updates by checking <see cref="UpdatePendingRestart"/>.
        /// </summary>
        /// <param name="toApply">The target release to apply. Can be left null to auto-apply the newest downloaded release.</param>
        /// <param name="silent">Configure whether Velopack should show a progress window / dialogs during the updates or not.</param>
        /// <param name="restart">Configure whether Velopack should restart the app after the updates have been applied.</param>
        /// <param name="restartArgs">The arguments to pass to the application when it is restarted.</param>
        public void WaitExitThenApplyUpdates(VelopackAsset? toApply, bool silent = false, bool restart = true, string[]? restartArgs = null)
        {
            UpdateExe.Apply(Locator, toApply, silent, restart, restartArgs, Log);
        }
        
        /// <inheritdoc cref="WaitExitThenApplyUpdates"/>
        public async Task WaitExitThenApplyUpdatesAsync(VelopackAsset? toApply, bool silent = false, bool restart = true, string[]? restartArgs = null)
        {
            await UpdateExe.ApplyAsync(Locator, toApply, silent, restart, restartArgs, Log).ConfigureAwait(false);
        }
    }
}
