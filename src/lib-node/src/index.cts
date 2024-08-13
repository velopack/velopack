import * as addon from './load.cjs';

type UpdateManagerOpaque = {};
declare module "./load.cjs" {
  function js_new_update_manager(urlOrPath: string, options?: string): UpdateManagerOpaque;
  function js_get_current_version(um: UpdateManagerOpaque): string;
  // function js_get_app_id(um: UpdateManagerOpaque): string;
  // function js_is_portable(um: UpdateManagerOpaque): boolean;
  // function js_is_installed(um: UpdateManagerOpaque): boolean;
  // function js_is_update_pending_restart(um: UpdateManagerOpaque): boolean;
  function js_check_for_updates_async(um: UpdateManagerOpaque): Promise<string | null>;
  function js_download_update_async(um: UpdateManagerOpaque, update: string, progress: (perc: number) => void, ignoreDeltas: boolean): Promise<void>;
  function js_wait_then_apply_update_async(um: UpdateManagerOpaque, update?: string): Promise<void>;
}

export type UpdateOptions = {
  AllowVersionDowngrade: boolean;
  ExplicitChannel: string;
}

/** An individual Velopack asset, could refer to an asset on-disk or in a remote package feed. */
export type VelopackAsset = {
  FileName: string;
  Version: string;
  NotesHtml: string;
  NotesMarkdown: string;
  PackageId: string;
  SHA1: string;
  SHA256: string;
  Size: number;
  Type: "Full" | "Delta";
}

export type UpdateInfo = {
  BaseRelease: VelopackAsset;
  DeltasToTarget: VelopackAsset[];
  IsDowngrade: boolean;
  TargetFullRelease: VelopackAsset;
}

export class UpdateManager {

  private opaque: UpdateManagerOpaque;

  constructor(urlOrPath: string, options?: UpdateOptions) {
    this.opaque = addon.js_new_update_manager(urlOrPath, options ? JSON.stringify(options) : "");
  }

  getCurrentVersion(): string {
    return addon.js_get_current_version.call(this.opaque);
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

  checkForUpdatesAsync(): Promise<UpdateInfo | null> {
    let json: Promise<string> = addon.js_check_for_updates_async.call(this.opaque);
    return json.then((json) => {
      if (json && json.length > 0) {
        return JSON.parse(json);
      }
      return null;
    });
  }

  downloadUpdateAsync(update: UpdateInfo, progress: (perc: number) => void, ignoreDeltas = false): Promise<void> {
    return addon.js_download_update_async.call(this.opaque, JSON.stringify(update), progress, ignoreDeltas);
  }

  waitExitThenApplyUpdateAsync(update: UpdateInfo): Promise<void> {
    return addon.js_wait_then_apply_update_async.call(this.opaque, JSON.stringify(update));
  }
}