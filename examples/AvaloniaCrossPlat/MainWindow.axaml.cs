using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia.Controls;
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
        _um = new UpdateManager(Const.RELEASES_DIR, logger: new TextBoxLogger(Log));
        UpdateStatus();
    }

    private async void BtnCheckUpdateClick(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Working();
        try {
            _update = await _um.CheckForUpdatesAsync();
        } catch (Exception ex) {
            Log("ERROR: " + ex.Message);
        }
        UpdateStatus();
    }

    private async void BtnDownloadUpdateClick(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Working();
        try {
            await _um.DownloadUpdatesAsync(_update, Progress);
        } catch (Exception ex) {
            Log("ERROR: " + ex.Message);
        }
        await Task.Delay(10);
        UpdateStatus();
    }

    private void Log(string text)
    {
        TextLog.Text += text + Environment.NewLine;
        ScrollLog.ScrollToEnd();
    }

    private void BtnRestartApplyClick(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _um.ApplyUpdatesAndRestart();
    }

    private void Progress(int percent)
    {
        Dispatcher.UIThread.Post(() => {
            TextStatus.Text = $"Downloading ({percent}%)...";
        });
    }

    private void Working()
    {
        Log("");
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

    private class TextBoxLogger : ILogger
    {
        private readonly Action<string> _textBox;

        public TextBoxLogger(Action<string> textBox)
        {
            _textBox = textBox;
        }

        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                       Exception exception, Func<TState, Exception, string> formatter)
        {
            if (logLevel < LogLevel.Information) return;
            var text = formatter(state, exception);
            Dispatcher.UIThread.Post(() => {
                _textBox(text);
            });
        }
    }
}