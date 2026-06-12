using Velopack.Core;
using Velopack.Packaging.Abstractions;

namespace Velopack.Deployment;

public class RepositoryOptions : IOutputOptions
{
    private string _channel;

    public RuntimeOs TargetOs { get; set; }

    public string Channel {
        get => _channel ?? DefaultName.GetDefaultChannel(TargetOs);
        set => _channel = value;
    }

    public DirectoryInfo ReleaseDir { get; set; }

    public double Timeout { get; set; } = 30d;
}
