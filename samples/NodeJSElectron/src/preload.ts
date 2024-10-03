// See the Electron documentation for details on how to use preload scripts:
// https://www.electronjs.org/docs/latest/tutorial/process-model#preload-scripts

import { contextBridge, ipcRenderer } from "electron";

contextBridge.exposeInMainWorld('velopackApi', {
    getVersion: () => ipcRenderer.sendSync("get-version")
});

// "get-version"
// "check-for-update"
// "check-for-update-response"
// "download-update"
// "download-update-response"
// "apply-update"
// "apply-update-response"

// const updateLabel = document.getElementById("app-info");
// let currentVersion = ipcRenderer.sendSync("get-version");
// updateLabel.innerHTML = "Current Version: " + currentVersion;

// const updateBtn = document.getElementById("update-btn") as HTMLButtonElement;
// const downloadBtn = document.getElementById("download-btn") as HTMLButtonElement;
// const applyBtn = document.getElementById("apply-btn") as HTMLButtonElement;

// async function updateBtnClicked() {
// }

// async function downloadBtnClicked() {
// }

// async function applyBtnClicked() {
// }

// updateBtn.addEventListener("click", updateBtnClicked);
// downloadBtn.addEventListener("click", downloadBtnClicked);
// applyBtn.addEventListener("click", applyBtnClicked);