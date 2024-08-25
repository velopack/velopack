import * as addon from './load.cjs';
import { UpdateInfo } from './bindings/UpdateInfo';
import { UpdateOptions } from './bindings/UpdateOptions';

export { UpdateInfo, UpdateOptions };

type UpdateManagerOpaque = {};
declare module "./load.cjs" {
  function js_new_update_manager(urlOrPath: string, options?: string): UpdateManagerOpaque;
  function js_get_current_version(um: UpdateManagerOpaque): string;
  // function js_get_app_id(um: UpdateManagerOpaque): string;
  // function js_is_portable(um: UpdateManagerOpaque): boolean;
  // function js_is_installed(um: UpdateManagerOpaque): boolean;
  // function js_is_update_pending_restart(um: UpdateManagerOpaque): boolean;
  function js_check_for_updates_async(um: UpdateManagerOpaque): Promise<string | null>;
  function js_download_update_async(um: UpdateManagerOpaque, update: string, progress: (perc: number) => void): Promise<void>;
  function js_wait_exit_then_apply_update(um: UpdateManagerOpaque, update: string, silent?: boolean, restart?: boolean, restartArgs?: string[]): void;
  function js_appbuilder_run(cb: (hook_name: string, current_version: string) => void): void;
}

type VelopackHookType = "after-install" | "before-uninstall" | "before-update" | "after-update" | "restarted" | "first-run";
type VelopackHook = (version: string) => void;

class VelopackAppBuilder {
  private hooks = new Map<VelopackHookType, VelopackHook>();

  /** 
  WARNING: FastCallback hooks are run during critical stages of Velopack operations.
  Your code will be run and then the process will exit.
  If your code has not completed within 30 seconds, it will be terminated.
  Only supported on windows; On other operating systems, this will never be called.
  */
  onAfterInstallFastCallback(callback: VelopackHook): VelopackAppBuilder {
    this.hooks.set("after-install", callback);
    return this;
  }

  /** 
  WARNING: FastCallback hooks are run during critical stages of Velopack operations.
  Your code will be run and then the process will exit.
  If your code has not completed within 30 seconds, it will be terminated.
  Only supported on windows; On other operating systems, this will never be called.
  */
  onBeforeUninstallFastCallback(callback: VelopackHook): VelopackAppBuilder {
    this.hooks.set("before-uninstall", callback);
    return this;
  }

  /** 
  WARNING: FastCallback hooks are run during critical stages of Velopack operations.
  Your code will be run and then the process will exit.
  If your code has not completed within 15 seconds, it will be terminated.
  Only supported on windows; On other operating systems, this will never be called.
  */
  onBeforeUpdateFastCallback(callback: VelopackHook): VelopackAppBuilder {
    this.hooks.set("before-update", callback);
    return this;
  }

  /** 
  WARNING: FastCallback hooks are run during critical stages of Velopack operations.
  Your code will be run and then the process will exit.
  If your code has not completed within 15 seconds, it will be terminated.
  Only supported on windows; On other operating systems, this will never be called.
  */
  onAfterUpdateFastCallback(callback: VelopackHook): VelopackAppBuilder {
    this.hooks.set("after-update", callback);
    return this;
  }

  /** 
  This hook is triggered when the application is restarted by Velopack after installing updates.
  */
  onRestarted(callback: VelopackHook): VelopackAppBuilder {
    this.hooks.set("restarted", callback);
    return this;
  }

  /** 
  This hook is triggered when the application is started for the first time after installation.
  */
  onFirstRun(callback: VelopackHook): VelopackAppBuilder {
    this.hooks.set("first-run", callback);
    return this;
  }

  /** 
  Runs the Velopack startup logic. This should be the first thing to run in your app.
  In some circumstances it may terminate/restart the process to perform tasks.
  */
  run(): void {
    addon.js_appbuilder_run((hook_name: string, current_version: string) => {
      let hook = this.hooks.get(hook_name as VelopackHookType);
      if (hook) {
        hook(current_version);
      }
    });
  }
}

export const VelopackApp = {
  build: () => {
    return new VelopackAppBuilder();
  }
}

export class UpdateManager {
  private opaque: UpdateManagerOpaque;

  constructor(urlOrPath: string, options?: UpdateOptions) {
    this.opaque = addon.js_new_update_manager(urlOrPath, options ? JSON.stringify(options) : "");
  }

  getCurrentVersion(): string {
    return addon.js_get_current_version(this.opaque);
  }

  // getAppId(): string {
  //   return addon.js_get_app_id.call(this.opaque);
  // }

  // isInstalled(): boolean {
  //   return addon.js_is_installed.call(this.opaque);
  // }

  // isPortable(): boolean {
  //   return addon.js_is_portable.call(this.opaque);
  // }

  // isUpdatePendingRestart(): boolean {
  //   return addon.js_is_update_pending_restart.call(this.opaque);
  // }

  /**
  Checks for updates, returning None if there are none available. If there are updates available, this method will return an
  UpdateInfo object containing the latest available release, and any delta updates that can be applied if they are available.
  */
  checkForUpdatesAsync(): Promise<UpdateInfo | null> {
    let json: Promise<string | null> = addon.js_check_for_updates_async(this.opaque);
    return json.then((json) => {
      if (json && json.length > 0) {
        return JSON.parse(json);
      }
      return null;
    });
  }

  /**
  Downloads the specified updates to the local app packages directory. Progress is reported back to the caller via an optional Sender.
  This function will acquire a global update lock so may fail if there is already another update operation in progress.
  - If the update contains delta packages and the delta feature is enabled
    this method will attempt to unpack and prepare them.
  - If there is no delta update available, or there is an error preparing delta
    packages, this method will fall back to downloading the full version of the update.
  */
  downloadUpdateAsync(update: UpdateInfo, progress: (perc: number) => void): Promise<void> {
    if (!update) {
      throw new Error("update is required");
    }
    return addon.js_download_update_async(this.opaque, JSON.stringify(update), progress ?? (() => { }));
  }

  /**
  This will launch the Velopack updater and tell it to wait for this program to exit gracefully.
  You should then clean up any state and exit your app. The updater will apply updates and then
  optionally restart your app. The updater will only wait for 60 seconds before giving up.
  */
  waitExitThenApplyUpdate(update: UpdateInfo, silent: boolean = false, restart: boolean = true, restartArgs: string[] = []): void {
    if (!update) {
      throw new Error("update is required");
    }
    addon.js_wait_exit_then_apply_update(this.opaque, JSON.stringify(update), silent, restart, restartArgs);
  }
}