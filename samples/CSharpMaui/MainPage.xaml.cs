using System;
using System.Text;
using Velopack;
using Velopack.Logging;

namespace CSharpMaui;

public partial class MainPage : ContentPage
{
    private UpdateManager _um;
    private UpdateInfo _update;

    public MainPage()
    {
        InitializeComponent();

        var updateUrl = SampleHelper.GetReleasesDir(); // replace with your update path/url
        _um = new UpdateManager(updateUrl);

        TextLog.Text = MauiProgram.Log.ToString();
        MauiProgram.Log.LogUpdated += LogUpdated;
        UpdateStatus();
    }

    private async void BtnCheckUpdateClick(object sender, EventArgs e)
    {
        Working();
        try {
            // ConfigureAwait(true) so that UpdateStatus() is called on the UI thread
            _update = await _um.CheckForUpdatesAsync().ConfigureAwait(true);
        } catch (Exception ex) {
            MauiProgram.Log.LogError(ex, "Error checking for updates");
        }

        UpdateStatus();
    }

    private async void BtnDownloadUpdateClick(object sender, EventArgs e)
    {
        Working();
        try {
            // ConfigureAwait(true) so that UpdateStatus() is called on the UI thread
            await _um.DownloadUpdatesAsync(_update, Progress).ConfigureAwait(true);
        } catch (Exception ex) {
            MauiProgram.Log.LogError(ex, "Error downloading updates");
        }

        UpdateStatus();
    }

    private void BtnRestartApplyClick(object sender, EventArgs e)
    {
        _um.ApplyUpdatesAndRestart(_update);
    }

    private void LogUpdated(object sender, LogUpdatedEventArgs e)
    {
        // logs can be sent from other threads
        Dispatcher.Dispatch(
            () => {
                TextLog.Text = e.Text;
                ScrollLog.ScrollToAsync(TextLog, ScrollToPosition.End, false);
            });
    }

    private void Progress(int percent)
    {
        // progress can be sent from other threads
        Dispatcher.Dispatch(
            () => {
                TextStatus.Text = $"Downloading ({percent}%)...";
            });
    }

    private void Working()
    {
        MauiProgram.Log.LogInformation("");
        BtnCheckUpdate.IsEnabled = false;
        BtnDownloadUpdate.IsEnabled = false;
        BtnRestartApply.IsEnabled = false;
        TextStatus.Text = "Working...";
    }

    private void UpdateStatus()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"Velopack version: {VelopackRuntimeInfo.VelopackNugetVersion}");
        sb.AppendLine($"This app version: {(_um.IsInstalled ? _um.CurrentVersion : "(n/a - not installed)")}");

        if (_update != null) {
            sb.AppendLine($"Update available: {_update.TargetFullRelease.Version}");
            BtnDownloadUpdate.IsEnabled = true;
        } else {
            BtnDownloadUpdate.IsEnabled = false;
        }

        if (_um.UpdatePendingRestart != null) {
            sb.AppendLine("Update ready, pending restart to install");
            BtnRestartApply.IsEnabled = true;
        } else {
            BtnRestartApply.IsEnabled = false;
        }

        TextStatus.Text = sb.ToString();
        BtnCheckUpdate.IsEnabled = true;
    }
}