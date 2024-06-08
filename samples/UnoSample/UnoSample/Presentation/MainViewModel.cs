using Velopack;
using Velopack.Sources;

namespace UnoSample.Presentation;

public partial class MainViewModel : ObservableObject
{
    private readonly UpdateManager _updateManager;

    public bool IsInstalled => _updateManager.IsInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    private UpdateInfo? _latestUpdate;

    public bool HasUpdate => LatestUpdate != null;

    public string CurrentVersion => _updateManager.CurrentVersion?.ToFullString() ?? "";

    [ObservableProperty]
    private string? _status;

    public MainViewModel(
        IStringLocalizer localizer,
        IOptions<AppConfig> appInfo)
    {
        Title = "Main";
        Title += $" - {localizer["ApplicationName"]}";
        Title += $" - {appInfo?.Value?.Environment}";

        _updateManager = new UpdateManager(new VelopackFlowUpdateSource());
    }
    public string? Title { get; }

    [RelayCommand(CanExecute = nameof(IsInstalled))]
    private async Task CheckForUpdates()
    {
        if (_updateManager.IsInstalled)
        {
            try
            {
                Status = "Checking for updates...";
                LatestUpdate = await _updateManager.CheckForUpdatesAsync();
                Status = LatestUpdate is null ? "No updates available" : $"{LatestUpdate.TargetFullRelease.Version} - Update available";
            }
            catch (Exception ex)
            {
                Status = ex.Message;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(IsInstalled))]
    private async Task DownloadUpdates()
    {
        if (_updateManager.IsInstalled && LatestUpdate is { } latestUpdate)
        {
            try
            {
                Status = $"Downloading {0:p}";
                
                await _updateManager.DownloadUpdatesAsync(latestUpdate, progress => Status = $"Downloading {progress / 100.0:p}");
                Status = "Restarting...";
                _updateManager.ApplyUpdatesAndRestart(latestUpdate.TargetFullRelease);
            }
            catch(Exception ex)
            {
                Status = ex.Message;
            }
        }
    }
}
