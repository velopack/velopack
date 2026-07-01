import * as addon from "./load";

import type { HttpHeader, HttpOptions, UpdateInfo, UpdateOptions, VelopackLocatorConfig, VelopackAsset } from "./types";
export { HttpHeader, HttpOptions, UpdateInfo, UpdateOptions, VelopackLocatorConfig, VelopackAsset };

type UpdateManagerOpaque = {};
declare module "./load" {
  function js_new_update_manager(source: string, options: string | null, locator: string | null): UpdateManagerOpaque;

  function js_get_current_version(um: UpdateManagerOpaque): string;

  function js_get_app_id(um: UpdateManagerOpaque): string;

  function js_is_portable(um: UpdateManagerOpaque): boolean;

  function js_update_pending_restart(um: UpdateManagerOpaque): string | null;

  function js_check_for_updates_async(um: UpdateManagerOpaque): Promise<string | null>;

  function js_download_update_async(um: UpdateManagerOpaque, update: string, progress: (perc: number) => void): Promise<void>;

  function js_wait_exit_then_apply_update(um: UpdateManagerOpaque, update: string, silent?: boolean, restart?: boolean, restartArgs?: string[]): void;

  function js_appbuilder_run(
    cb: (hook_name: string, current_version: string) => void,
    customArgs: string[] | null,
    locator: string | null,
    autoApply: boolean,
  ): void;

  function js_set_logger_callback(cb: (loglevel: LogLevel, msg: string) => void): void;
}

type VelopackHookType = "after-install" | "before-uninstall" | "before-update" | "after-update" | "restarted" | "first-run";

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
  setLogger(callback: (loglevel: LogLevel, msg: string) => void): VelopackApp {
    addon.js_set_logger_callback(callback);
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
    addon.js_appbuilder_run(
      (hook_name: string, current_version: string) => {
        let hook = this._hooks.get(hook_name as VelopackHookType);
        if (hook) {
          hook(current_version);
        }
      },
      this._customArgs,
      this._customLocator ? JSON.stringify(this._customLocator) : null,
      this._autoApply,
    );
  }
}

/**
 * A built-in update source that retrieves release feeds and downloads assets from a local or network directory.
 */
export class FileSource {
  /**
   * Create a new FileSource.
   * @param path The path to the directory containing the releases.
   */
  constructor(public readonly path: string) {}
}

/**
 * Retrieves updates from a static file host or other web server.
 */
export class HttpSource {
  /**
   * Create a new HttpSource.
   * @param url The base URL of the releases feed.
   * @param options Optional HTTP options (custom headers, timeout) applied to all requests made by this source.
   */
  constructor(
    public readonly url: string,
    public readonly options?: Partial<HttpOptions>,
  ) {}
}

/**
 * Retrieves available releases from a GitHub repository. Supports both github.com
 * and GitHub Enterprise instances.
 */
export class GithubSource {
  /**
   * Create a new GithubSource.
   * @param repoUrl The URL of the GitHub repository (e.g. "https://github.com/myuser/myrepo").
   * @param accessToken Optional GitHub access token. Without one, requests are rate limited to 60/hr per IP.
   * @param prerelease If true, pre-releases will also be searched/downloaded.
   */
  constructor(
    public readonly repoUrl: string,
    public readonly accessToken?: string,
    public readonly prerelease: boolean = false,
  ) {}
}

/**
 * Retrieves available releases from a GitLab repository.
 */
export class GitlabSource {
  /**
   * Create a new GitlabSource.
   * @param repoUrl The GitLab API URL (e.g. "https://gitlab.com/api/v4/projects/ProjectId").
   * @param accessToken Optional GitLab access token (sent as PRIVATE-TOKEN header).
   * @param prerelease If true, upcoming/pre-releases will also be searched.
   */
  constructor(
    public readonly repoUrl: string,
    public readonly accessToken?: string,
    public readonly prerelease: boolean = false,
  ) {}
}

/**
 * Retrieves available releases from a Gitea repository.
 */
export class GiteaSource {
  /**
   * Create a new GiteaSource.
   * @param repoUrl The URL of the Gitea repository (e.g. "https://gitea.com/myuser/myrepo").
   * @param accessToken Optional Gitea access token (sent as `Authorization: token {token}`).
   * @param prerelease If true, pre-releases will also be searched/downloaded.
   */
  constructor(
    public readonly repoUrl: string,
    public readonly accessToken?: string,
    public readonly prerelease: boolean = false,
  ) {}
}

/**
 * A source that UpdateManager can retrieve updates from. A plain string is auto-detected:
 * a local path uses FileSource, a github.com/gitlab.com/gitea.com URL uses the matching
 * git source, and any other URL uses HttpSource.
 */
export type UpdateSource = string | FileSource | HttpSource | GithubSource | GitlabSource | GiteaSource;

function serializeSource(source: UpdateSource): string {
  if (typeof source === "string") {
    return JSON.stringify({ kind: "auto", input: source });
  } else if (source instanceof FileSource) {
    return JSON.stringify({ kind: "file", path: source.path });
  } else if (source instanceof HttpSource) {
    return JSON.stringify({
      kind: "http",
      url: source.url,
      options: source.options,
    });
  } else if (source instanceof GithubSource) {
    return JSON.stringify({
      kind: "github",
      repoUrl: source.repoUrl,
      accessToken: source.accessToken,
      prerelease: source.prerelease,
    });
  } else if (source instanceof GitlabSource) {
    return JSON.stringify({
      kind: "gitlab",
      repoUrl: source.repoUrl,
      accessToken: source.accessToken,
      prerelease: source.prerelease,
    });
  } else if (source instanceof GiteaSource) {
    return JSON.stringify({
      kind: "gitea",
      repoUrl: source.repoUrl,
      accessToken: source.accessToken,
      prerelease: source.prerelease,
    });
  }
  throw new Error("Unsupported update source type");
}

/**
 * Provides functionality for checking for updates, downloading updates, and applying updates to the current application.
 */
export class UpdateManager {
  private readonly opaque: UpdateManagerOpaque;

  /**
   * Create a new UpdateManager instance.
   * @param source The source to check for updates: one of the update source classes, or a
   * URL / local path string which will be auto-detected.
   * @param options Optional extra configuration for update manager.
   * @param locator Override the default locator configuration (usually used for testing / mocks).
   */
  constructor(source: UpdateSource, options?: UpdateOptions, locator?: VelopackLocatorConfig) {
    this.opaque = addon.js_new_update_manager(
      serializeSource(source),
      options ? JSON.stringify(options) : "",
      locator ? JSON.stringify(locator) : null,
    );
  }

  /**
   * Returns the currently installed version of the app.
   */
  getCurrentVersion(): string {
    return addon.js_get_current_version(this.opaque);
  }

  /**
   * Returns the currently installed app id.
   */
  getAppId(): string {
    return addon.js_get_app_id(this.opaque);
  }

  /**
   * Returns whether the app is in portable mode. On Windows this can be true or false.
   * On MacOS and Linux this will always be true.
   */
  isPortable(): boolean {
    return addon.js_is_portable(this.opaque);
  }

  /**
   * Returns an VelopackAsset object if there is an update downloaded which still needs to be applied.
   * You can pass the VelopackAsset object to waitExitThenApplyUpdate to apply the update.
   */
  getUpdatePendingRestart(): VelopackAsset | null {
    let json: string | null = addon.js_update_pending_restart(this.opaque);
    if (json && json.length > 0) {
      return JSON.parse(json);
    }
    return null;
  }

  /**
   * Checks for updates, returning None if there are none available. If there are updates available, this method will return an
   * UpdateInfo object containing the latest available release, and any delta updates that can be applied if they are available.
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
   * Downloads the specified updates to the local app packages directory. Progress is reported back to the caller via an optional Sender.
   * This function will acquire a global update lock so may fail if there is already another update operation in progress.
   * - If the update contains delta packages and the delta feature is enabled
   *   this method will attempt to unpack and prepare them.
   * - If there is no delta update available, or there is an error preparing delta
   *   packages, this method will fall back to downloading the full version of the update.
   */
  downloadUpdateAsync(update: UpdateInfo, progress?: (perc: number) => void): Promise<void> {
    if (!update) {
      throw new Error("update is required");
    }
    return addon.js_download_update_async(this.opaque, JSON.stringify(update), progress ?? (() => {}));
  }

  /**
   * This will launch the Velopack updater and tell it to wait for this program to exit gracefully.
   * You should then clean up any state and exit your app. The updater will apply updates and then
   * optionally restart your app. The updater will only wait for 60 seconds before giving up.
   */
  waitExitThenApplyUpdate(update: UpdateInfo | VelopackAsset, silent: boolean = false, restart: boolean = true, restartArgs: string[] = []): void {
    if (!update) {
      throw new Error("update is required");
    }

    // the backend API only accepts VelopackAsset, so we need to extract it from UpdateInfo
    if ("TargetFullRelease" in update && typeof update.TargetFullRelease === "object") {
      update = update.TargetFullRelease;
    }

    addon.js_wait_exit_then_apply_update(this.opaque, JSON.stringify(update), silent, restart, restartArgs);
  }
}
