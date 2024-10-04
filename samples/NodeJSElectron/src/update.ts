
import { ipcMain, app } from "electron";
import { UpdateManager } from "velopack";

// replace me
const updateUrl = "C:\\Source\\velopack\\samples\\NodeJSElectron\\releases";

export async function initializeUpdates() {
    ipcMain.handle("velopack:get-version", () => {
        try {
            const updateManager = new UpdateManager(updateUrl);
            return updateManager.getCurrentVersion();
        } catch (e) {
            return `Not Installed (${e})`;
        }
    });

    ipcMain.handle("velopack:check-for-update", async () => {
        const updateManager = new UpdateManager(updateUrl);
        return await updateManager.checkForUpdatesAsync();
    });

    ipcMain.handle("velopack:download-update", async (_, updateInfo) => {
        const updateManager = new UpdateManager(updateUrl);
        await updateManager.downloadUpdateAsync(updateInfo);
        return true;
    });

    ipcMain.handle("velopack:apply-update", async (_, updateInfo) => {
        const updateManager = new UpdateManager(updateUrl);
        await updateManager.waitExitThenApplyUpdate(updateInfo);
        app.quit();
        return true;
    });
}