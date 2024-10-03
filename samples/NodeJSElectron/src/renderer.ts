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

import './index.css';

console.log('👋 This message is being logged by "renderer.js", included via webpack');

// "get-version"
// "check-for-update"
// "check-for-update-response"
// "download-update"
// "download-update-response"
// "apply-update"
// "apply-update-response"

const updateLabel = document.getElementById("app-info");
let currentVersion = velopackApi.getVersion();
updateLabel.innerHTML = "Current Version: " + currentVersion;

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