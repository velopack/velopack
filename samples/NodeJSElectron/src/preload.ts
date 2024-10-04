// See the Electron documentation for details on how to use preload scripts:
// https://www.electronjs.org/docs/latest/tutorial/process-model#preload-scripts

import { contextBridge, ipcRenderer } from "electron";
import type { UpdateInfo } from "velopack";

interface VelopackBridgeApi {
    getVersion: () => Promise<string>,
    checkForUpdates: () => Promise<UpdateInfo>,
    downloadUpdates: (updateInfo: UpdateInfo) => Promise<boolean>,
    applyUpdates: (updateInfo: UpdateInfo) => Promise<boolean>,
}

declare global {
    interface Window {
        velopackApi: VelopackBridgeApi;
    }
}

const velopackApi: VelopackBridgeApi = {
    getVersion: () => ipcRenderer.invoke("velopack:get-version"),
    checkForUpdates: () => ipcRenderer.invoke("velopack:check-for-update"),
    downloadUpdates: (updateInfo: UpdateInfo) => ipcRenderer.invoke("velopack:download-update", updateInfo),
    applyUpdates: (updateInfo: UpdateInfo) => ipcRenderer.invoke("velopack:apply-update", updateInfo)
};

contextBridge.exposeInMainWorld("velopackApi", velopackApi);