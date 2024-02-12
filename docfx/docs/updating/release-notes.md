*Applies to: Windows, MacOS, Linux*

# Release Notes
It is possible to store release notes (Markdown) in update packages and access it while Updating. This could be useful, for example, to show a user a list of changes before downloading a update.

- Write your release notes to a file (eg. releasenotes.md).
- While packing your release, provide these to Velopack with `--releaseNotes {path/to/releasenotes.md}`

The Velopack builder will render this to HTML for your convenience, and store both the HTML and the markdown into your update package.

Now, release notes will be available while checking for updates, for example:

```cs
private static async Task UpdateMyApp()
{
    var mgr = new UpdateManager("https://the.place/you-host/updates");

    var newVersion = await mgr.CheckForUpdatesAsync();
    if (newVersion != null) {
        new ReleaseNotesHtmlWindow(newVersion.TargetFullRelease.ReleaseNotesHtml).Show();
    }
}
```