// This file was generated by [ts-rs](https://github.com/Aleph-Alpha/ts-rs). Do not edit this file manually.

/**
 * Options to customise the behaviour of UpdateManager.
 */
export type UpdateOptions = {
  /**
   * Allows UpdateManager to update to a version that's lower than the current version (i.e. downgrading).
   * This could happen if a release has bugs and was retracted from the release feed, or if you're using
   * ExplicitChannel to switch channels to another channel where the latest version on that
   * channel is lower than the current version.
   */
  AllowVersionDowngrade: boolean;
  /**
   * **This option should usually be left None**. <br/>
   * Overrides the default channel used to fetch updates.
   * The default channel will be whatever channel was specified on the command line when building this release.
   * For example, if the current release was packaged with '--channel beta', then the default channel will be 'beta'.
   * This allows users to automatically receive updates from the same channel they installed from. This options
   * allows you to explicitly switch channels, for example if the user wished to switch back to the 'stable' channel
   * without having to reinstall the application.
   */
  ExplicitChannel: string | null;
};
