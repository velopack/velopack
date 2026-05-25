import { resolve } from "node:path";
import {
  getWasm,
  setProgressCallback,
  setLoggerCallback,
  loadVelopack,
} from "./host/wasm-loader.js";

import type {
  UpdateInfo,
  UpdateOptions,
  VelopackLocatorConfig,
  VelopackAsset,
} from "./types.js";
export { UpdateInfo, UpdateOptions, VelopackLocatorConfig, VelopackAsset };
export { loadVelopack };

function normalizePath(p: string): string {
  return resolve(p).replace(/\\/g, "/");
}

function resolveLocatorPaths(
  locator: VelopackLocatorConfig,
): VelopackLocatorConfig {
  return {
    RootAppDir: locator.RootAppDir ? normalizePath(locator.RootAppDir) : "",
    UpdateExePath: locator.UpdateExePath
      ? normalizePath(locator.UpdateExePath)
      : "",
    PackagesDir: locator.PackagesDir ? normalizePath(locator.PackagesDir) : "",
    ManifestPath: locator.ManifestPath
      ? normalizePath(locator.ManifestPath)
      : "",
    CurrentBinaryDir: locator.CurrentBinaryDir
      ? normalizePath(locator.CurrentBinaryDir)
      : "",
    IsPortable: locator.IsPortable,
  };
}

function resolveSourcePath(urlOrPath: string): string {
  if (urlOrPath.startsWith("http://") || urlOrPath.startsWith("https://")) {
    return urlOrPath;
  }
  return normalizePath(urlOrPath);
}

type VelopackHookType =
  | "after-install"
  | "before-uninstall"
  | "before-update"
  | "after-update"
  | "restarted"
  | "first-run";

type VelopackHook = (version: string) => void;

type LogLevel = "info" | "warn" | "error" | "debug" | "trace";

/**
 * VelopackApp helps you to handle app activation events correctly.
 * This should be used as early as possible in your application startup code.
 * (eg. the beginning of main() or wherever your entry point is)
 */
export class VelopackApp {
  private _hooks = new Map<VelopackHookType, VelopackHook>();
  private _customArgs: string[] | null = null;
  private _customLocator: VelopackLocatorConfig | null = null;
  private _autoApply = true;

  /**
   * Build a new VelopackApp instance.
   */
  static build(): VelopackApp {
    return new VelopackApp();
  }

  /**
   * WARNING: FastCallback hooks are run during critical stages of Velopack operations.
   * Your code will be run and then the process will exit.
   * If your code has not completed within 30 seconds, it will be terminated.
   * Only supported on windows; On other operating systems, this will never be called.
   */
  onAfterInstallFastCallback(callback: VelopackHook): VelopackApp {
    this._hooks.set("after-install", callback);
    return this;
  }

  /**
   * WARNING: FastCallback hooks are run during critical stages of Velopack operations.
   * Your code will be run and then the process will exit.
   * If your code has not completed within 30 seconds, it will be terminated.
   * Only supported on windows; On other operating systems, this will never be called.
   */
  onBeforeUninstallFastCallback(callback: VelopackHook): VelopackApp {
    this._hooks.set("before-uninstall", callback);
    return this;
  }

  /**
   * WARNING: FastCallback hooks are run during critical stages of Velopack operations.
   * Your code will be run and then the process will exit.
   * If your code has not completed within 15 seconds, it will be terminated.
   * Only supported on windows; On other operating systems, this will never be called.
   */
  onBeforeUpdateFastCallback(callback: VelopackHook): VelopackApp {
    this._hooks.set("before-update", callback);
    return this;
  }

  /**
   * WARNING: FastCallback hooks are run during critical stages of Velopack operations.
   * Your code will be run and then the process will exit.
   * If your code has not completed within 15 seconds, it will be terminated.
   * Only supported on windows; On other operating systems, this will never be called.
   */
  onAfterUpdateFastCallback(callback: VelopackHook): VelopackApp {
    this._hooks.set("after-update", callback);
    return this;
  }

  /**
   * This hook is triggered when the application is restarted by Velopack after installing updates.
   */
  onRestarted(callback: VelopackHook): VelopackApp {
    this._hooks.set("restarted", callback);
    return this;
  }

  /**
   * This hook is triggered when the application is started for the first time after installation.
   */
  onFirstRun(callback: VelopackHook): VelopackApp {
    this._hooks.set("first-run", callback);
    return this;
  }

  /**
   * Override the command line arguments used by VelopackApp. (by default this is env::args().skip(1))
   */
  setArgs(args: string[]): VelopackApp {
    this._customArgs = args;
    return this;
  }

  /**
   * VelopackLocator provides some utility functions for locating the current app important paths (eg. path to packages, update binary, and so forth).
   */
  setLocator(locator: VelopackLocatorConfig): VelopackApp {
    this._customLocator = locator;
    return this;
  }

  /**
   * Set a callback to receive log messages from VelopackApp.
   */
  setLogger(
    callback: (loglevel: LogLevel, msg: string) => void,
  ): VelopackApp {
    setLoggerCallback(callback as (level: string, msg: string) => void);
    return this;
  }

  /**
   * Set whether to automatically apply downloaded updates on startup. This is ON by default.
   */
  setAutoApplyOnStartup(autoApply: boolean): VelopackApp {
    this._autoApply = autoApply;
    return this;
  }

  /**
   * Runs the Velopack startup logic. This should be the first thing to run in your app.
   * In some circumstances it may terminate/restart the process to perform tasks.
   */
  run(): void {
    const wasm = getWasm();
    const args = this._customArgs ?? [];
    const resolvedLocator = this._customLocator
      ? resolveLocatorPaths(this._customLocator)
      : null;
    const locatorJson = resolvedLocator
      ? JSON.stringify(resolvedLocator)
      : null;

    const resultJson = wasm.appRun(args, locatorJson, this._autoApply);

    if (resultJson) {
      const result = JSON.parse(resultJson);

      if (result.hook) {
        const [hookName, version] = result.hook;
        const hook = this._hooks.get(hookName as VelopackHookType);
        if (hook) {
          hook(version);
        }
      }

      if (result.firstRun) {
        const hook = this._hooks.get("first-run");
        if (hook) {
          hook(result.firstRun);
        }
      }

      if (result.restarted) {
        const hook = this._hooks.get("restarted");
        if (hook) {
          hook(result.restarted);
        }
      }
    }
  }
}

/**
 * Provides functionality for checking for updates, downloading updates, and applying updates to the current application.
 */
export class UpdateManager {
  private readonly _token: string;

  /**
   * Create a new UpdateManager instance.
   * @param urlOrPath Location of the update server or path to the local update directory.
   * @param options Optional extra configuration for update manager.
   * @param locator Override the default locator configuration (usually used for testing / mocks).
   */
  constructor(
    urlOrPath: string,
    options?: UpdateOptions,
    locator?: VelopackLocatorConfig,
  ) {
    const wasm = getWasm();
    const resolvedLocator = locator ? resolveLocatorPaths(locator) : undefined;
    this._token = wasm.createUpdateManager(
      resolveSourcePath(urlOrPath),
      options ? JSON.stringify(options) : null,
      resolvedLocator ? JSON.stringify(resolvedLocator) : null,
    );
  }

  /**
   * Disposes of the update manager, freeing resources in the WASM module.
   */
  dispose(): void {
    const wasm = getWasm();
    wasm.destroyUpdateManager(this._token);
  }

  /**
   * Returns the currently installed version of the app.
   */
  getCurrentVersion(): string {
    const wasm = getWasm();
    return wasm.getCurrentVersion(this._token);
  }

  /**
   * Returns the currently installed app id.
   */
  getAppId(): string {
    const wasm = getWasm();
    return wasm.getAppId(this._token);
  }

  /**
   * Returns whether the app is in portable mode. On Windows this can be true or false.
   * On MacOS and Linux this will always be true.
   */
  isPortable(): boolean {
    const wasm = getWasm();
    return wasm.getIsPortable(this._token);
  }

  /**
   * Returns an VelopackAsset object if there is an update downloaded which still needs to be applied.
   * You can pass the VelopackAsset object to waitExitThenApplyUpdate to apply the update.
   */
  getUpdatePendingRestart(): VelopackAsset | null {
    const wasm = getWasm();
    const json: string | undefined = wasm.getUpdatePendingRestart(this._token);
    if (json && json.length > 0) {
      return JSON.parse(json);
    }
    return null;
  }

  /**
   * Checks for updates, returning None if there are none available. If there are updates available, this method will return an
   * UpdateInfo object containing the latest available release, and any delta updates that can be applied if they are available.
   */
  async checkForUpdatesAsync(): Promise<UpdateInfo | null> {
    await new Promise(resolve => setTimeout(resolve, 0));
    const wasm = getWasm();
    const json: string | undefined = wasm.checkForUpdates(this._token);
    if (json && json.length > 0) {
      return JSON.parse(json);
    }
    return null;
  }

  /**
   * Downloads the specified updates to the local app packages directory. Progress is reported back to the caller via an optional Sender.
   * This function will acquire a global update lock so may fail if there is already another update operation in progress.
   * - If the update contains delta packages and the delta feature is enabled
   *   this method will attempt to unpack and prepare them.
   * - If there is no delta update available, or there is an error preparing delta
   *   packages, this method will fall back to downloading the full version of the update.
   */
  async downloadUpdateAsync(
    update: UpdateInfo,
    progress?: (perc: number) => void,
  ): Promise<void> {
    if (!update) {
      throw new Error("update is required");
    }
    await new Promise(resolve => setTimeout(resolve, 0));
    const wasm = getWasm();
    setProgressCallback(progress ?? null);
    try {
      wasm.downloadUpdates(this._token, JSON.stringify(update));
    } finally {
      setProgressCallback(null);
    }
  }

  /**
   * This will launch the Velopack updater and tell it to wait for this program to exit gracefully.
   * You should then clean up any state and exit your app. The updater will apply updates and then
   * optionally restart your app. The updater will only wait for 60 seconds before giving up.
   */
  waitExitThenApplyUpdate(
    update: UpdateInfo | VelopackAsset,
    silent: boolean = false,
    restart: boolean = true,
    restartArgs: string[] = [],
  ): void {
    if (!update) {
      throw new Error("update is required");
    }

    if (
      "TargetFullRelease" in update &&
      typeof update.TargetFullRelease === "object"
    ) {
      update = update.TargetFullRelease;
    }

    const wasm = getWasm();
    wasm.waitExitThenApplyUpdate(
      this._token,
      JSON.stringify(update),
      silent,
      restart,
      restartArgs,
    );
  }
}
