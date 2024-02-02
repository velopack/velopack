using System;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Velopack;

namespace AvaloniaCrossPlat;

public partial class MainWindow : Window
{
    private UpdateManager _um;
    private UpdateInfo _update;

    public MainWindow()
    {
        InitializeComponent();
        _um = new UpdateManager(Program.UpdateUrl, logger: Program.Log);
        TextLog.Text = Program.Log.ToString();
        Program.Log.LogUpdated += LogUpdated;
        UpdateStatus();
    }

    private async void BtnCheckUpdateClick(object sender, RoutedEventArgs e)
    {
        Working();
        try {
            // ConfigureAwait(true) so that UpdateStatus() is called on the UI thread
            _update = await _um.CheckForUpdatesAsync().ConfigureAwait(true);
        } catch (Exception ex) {
            Program.Log.LogError(ex, "Error checking for updates");
        }
        UpdateStatus();
    }

    private async void BtnDownloadUpdateClick(object sender, RoutedEventArgs e)
    {
        Working();
        try {
            // ConfigureAwait(true) so that UpdateStatus() is called on the UI thread
            await _um.DownloadUpdatesAsync(_update, Progress).ConfigureAwait(true);
        } catch (Exception ex) {
            Program.Log.LogError(ex, "Error downloading updates");
        }
        UpdateStatus();
    }

    private void BtnRestartApplyClick(object sender, RoutedEventArgs e)
    {
        _um.ApplyUpdatesAndRestart();
    }

    private void LogUpdated(object sender, LogUpdatedEventArgs e)
    {
        // logs can be sent from other threads
        Dispatcher.UIThread.InvokeAsync(() => {
            TextLog.Text = e.Text;
            ScrollLog.ScrollToEnd();
        });
    }

    private void Progress(int percent)
    {
        // progress can be sent from other threads
        Dispatcher.UIThread.InvokeAsync(() => {
            TextStatus.Text = $"Downloading ({percent}%)...";
        });
    }

    private void Working()
    {
        Program.Log.LogInformation("");
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

        if (_um.IsUpdatePendingRestart) {
            sb.AppendLine("Update ready, pending restart to install");
            BtnRestartApply.IsEnabled = true;
        } else {
            BtnRestartApply.IsEnabled = false;
        }

        TextStatus.Text = sb.ToString();
        BtnCheckUpdate.IsEnabled = true;
    }
}