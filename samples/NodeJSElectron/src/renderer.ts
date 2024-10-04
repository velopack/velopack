/**
 * This file will automatically be loaded by webpack and run in the "renderer" context.
 * To learn more about the differences between the "main" and the "renderer" context in
 * Electron, visit:
 *
 * https://electronjs.org/docs/latest/tutorial/process-model
 *
 * By default, Node.js integration in this file is disabled. When enabling Node.js integration
 * in a renderer process, please be aware of potential security implications. You can read
 * more about security risks here:
 *
 * https://electronjs.org/docs/tutorial/security
 *
 * To enable Node.js integration in this file, open up `main.js` and enable the `nodeIntegration`
 * flag:
 *
 * ```
 *  // Create the browser window.
 *  mainWindow = new BrowserWindow({
 *    width: 800,
 *    height: 600,
 *    webPreferences: {
 *      nodeIntegration: true
 *    }
 *  });
 * ```
 */

import type { UpdateInfo } from 'velopack';
import './index.css';

console.log('👋 This message is being logged by "renderer.js", included via webpack');

const labelElement = document.getElementById("app-info");
function setLabel(text: string) {
    labelElement.innerHTML = text;
}

// get and display the current program version
(async () => {
    const currentVersion = await window.velopackApi.getVersion();
    setLabel("Current Version: " + currentVersion);
})();

// wire up button clicks to velopack functions
let updateInfo: UpdateInfo;
let downloaded: boolean;
async function updateBtnClicked() {
    try {
        updateInfo = await window.velopackApi.checkForUpdates();
        if (updateInfo) {
            setLabel(`Update available: ${updateInfo.TargetFullRelease.Version}`);
        } else {
            setLabel("No update is available.");
        }
    } catch (e) {
        setLabel(e);
    }
}

async function downloadBtnClicked() {
    if (!updateInfo) {
        setLabel("No update is available to download.");
    }

    try {
        downloaded = await window.velopackApi.downloadUpdates(updateInfo);
        setLabel("Update is downloaded");
    } catch (e) {
        setLabel(e);
    }
}

async function applyBtnClicked() {
    if (!updateInfo || !downloaded) {
        setLabel("No update is downloaded.");
    }

    try {
        await window.velopackApi.applyUpdates(updateInfo);
        setLabel("Update is applying...");
    } catch (e) {
        setLabel(e);
    }
}

const updateBtn = document.getElementById("update-btn") as HTMLButtonElement;
const downloadBtn = document.getElementById("download-btn") as HTMLButtonElement;
const applyBtn = document.getElementById("apply-btn") as HTMLButtonElement;
updateBtn.addEventListener("click", updateBtnClicked);
downloadBtn.addEventListener("click", downloadBtnClicked);
applyBtn.addEventListener("click", applyBtnClicked);