
import { ipcMain, app } from "electron";
import { UpdateManager } from "velopack";

const updateUrl = "C:\\Source\\velopack\\samples\\NodeJSElectron\\releases";

export async function initializeUpdates() {
    ipcMain.on("get-version", (event) => {
        try {
            var updateManager = new UpdateManager(updateUrl);
            event.returnValue = updateManager.getCurrentVersion();
        } catch (e) {
            event.returnValue = "Not Installed";
        }
    });

    ipcMain.on("check-for-update", async (event) => {
        try {
            var updateManager = new UpdateManager(updateUrl);
            const updateInfo = await updateManager.checkForUpdatesAsync();
            event.sender.send("check-for-update-response", [updateInfo, null]);
        } catch (e) {
            event.sender.send("check-for-update-response", [null, e]);
        }
    });

    ipcMain.on("download-update", async (event, updateInfo) => {
        try {
            var updateManager = new UpdateManager(updateUrl);
            await updateManager.downloadUpdateAsync(updateInfo);
            event.sender.send("download-update-response", [true, null]);
        } catch (e) {
            event.sender.send("download-update-response", [null, e]);
        }
    });

    ipcMain.on("apply-update", async (event, updateInfo) => {
        try {
            var updateManager = new UpdateManager(updateUrl);
            await updateManager.waitExitThenApplyUpdate(updateInfo);
            event.sender.send("apply-update-response", [true, null]);
            app.quit();
        } catch (e) {
            event.sender.send("apply-update-response", [null, e]);
        }
    });
}