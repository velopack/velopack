using System;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Velopack;

namespace CSharpAvalonia;

public partial class MainWindow : Window
{
    private UpdateManager _um;
    private UpdateInfo _update;

    public MainWindow()
    {
        InitializeComponent();

        var updateUrl = SampleHelper.GetReleasesDir(); // replace with your update path/url
        _um = new UpdateManager(updateUrl);

        UpdateStatus();
    }

    private async void BtnCheckUpdateClick(object sender, RoutedEventArgs e)
    {
        Working();
        try {
            // ConfigureAwait(true) so that UpdateStatus() is called on the UI thread
            _update = await _um.CheckForUpdatesAsync().ConfigureAwait(true);
        } catch (Exception ex) {
            LogMessage("Error checking for updates", ex);
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
            LogMessage("Error downloading updates", ex);
        }

        UpdateStatus();
    }

    private void BtnRestartApplyClick(object sender, RoutedEventArgs e)
    {
        _um.ApplyUpdatesAndRestart(_update);
    }

    private void LogMessage(string text, Exception e = null)
    {
        // logs can be sent from other threads
        Dispatcher.UIThread.InvokeAsync(
            () => {
                TextLog.Text += text + Environment.NewLine;
                if (e != null) {
                    TextLog.Text += e.ToString() + Environment.NewLine;
                }

                ScrollLog.ScrollToEnd();
            });
    }

    private void Progress(int percent)
    {
        // progress can be sent from other threads
        Dispatcher.UIThread.InvokeAsync(
            () => {
                TextStatus.Text = $"Downloading ({percent}%)...";
            });
    }

    private void Working()
    {
        LogMessage("");
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