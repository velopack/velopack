using System.Text;
using Microsoft.Extensions.Logging;
using Velopack;

namespace CSharpWinForms;

public partial class Form1 : Form
{
    private readonly UpdateManager _um;
    private UpdateInfo? _update;

    public Form1()
    {
        InitializeComponent();

        string updateUrl = SampleHelper.GetReleasesDir(); // replace with your update url
        _um = new UpdateManager(updateUrl, logger: Program.Log);

        txtTextLog.Text = Program.Log.ToString();
        Program.Log.LogUpdated += LogUpdated;
        UpdateStatus();
    }

    private void LogUpdated(object? sender, LogUpdatedEventArgs e)
    {
        // logs can be sent from other threads
        if (InvokeRequired) {
            Invoke(() => LogUpdated(sender, e));
            return;
        }
        txtTextLog.Text = e.Text;
        txtTextLog.SelectionStart = txtTextLog.Text.Length;
        txtTextLog.ScrollToCaret();
    }

    private void Progress(int percent)
    {
        // progress can be sent from other threads
        if (InvokeRequired) {
            Invoke(() => Progress(percent));
            return;
        }
        lblStatus.Text = $"Downloading ({percent}%)...";
    }

    private void Working()
    {
        Program.Log.LogInformation("");
        btnCheckUpdate.Enabled = false;
        btnDownloadUpdate.Enabled = false;
        btnRestartApply.Enabled = false;
        lblStatus.Text = "Working...";
    }

    private void UpdateStatus()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"Velopack version: {VelopackRuntimeInfo.VelopackNugetVersion}");
        sb.AppendLine($"This app version: {(_um.IsInstalled ? _um.CurrentVersion : "(n/a - not installed)")}");

        if (_update != null) {
            sb.AppendLine($"Update available: {_update.TargetFullRelease.Version}");
            btnDownloadUpdate.Enabled = true;
        } else {
            btnDownloadUpdate.Enabled = false;
        }

        if (_um.UpdatePendingRestart != null) {
            sb.AppendLine("Update ready, pending restart to install");
            btnRestartApply.Enabled = true;
        } else {
            btnRestartApply.Enabled = false;
        }

        lblStatus.Text = sb.ToString();
        btnCheckUpdate.Enabled = true;
    }

    private async void btnCheckUpdate_Click(object sender, EventArgs e)
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

    private async void btnDownloadUpdate_Click(object sender, EventArgs e)
    {
        Working();
        try {
            // ConfigureAwait(true) so that UpdateStatus() is called on the UI thread
            await _um.DownloadUpdatesAsync(_update!, Progress).ConfigureAwait(true);
        } catch (Exception ex) {
            Program.Log.LogError(ex, "Error downloading updates");
        }
        UpdateStatus();
    }

    private void btnRestartApply_Click(object sender, EventArgs e)
    {
        _um.ApplyUpdatesAndRestart(_update!);
    }
}
