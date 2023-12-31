using System.Threading;
using Xunit.Sdk;

namespace Squirrel.CommandLine.Tests;

public abstract class TempFileTestBase : IDisposable
{
    private readonly Lazy<DirectoryInfo> _WorkingDirectory = new(() => {
        DirectoryInfo working = new(
            Path.Combine(Path.GetTempPath(),
            typeof(TempFileTestBase).Assembly.GetName().Name!,
            Path.GetRandomFileName()));

        if (working.Exists) {
            working.Delete(recursive: true);
        }

        working.Create();
        return working;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly List<FileInfo> _TempFiles = new();
    private readonly List<DirectoryInfo> _TempDirectories = new();
    protected DirectoryInfo TempDirectory => _WorkingDirectory.Value;
    private bool _Disposed;

    public FileInfo CreateTempFile(DirectoryInfo? directory = null, string? name = null)
    {
        var tempFile = new FileInfo(GetPath(directory, name));
        tempFile.Create().Close();
        _TempFiles.Add(tempFile);
        return tempFile;
    }

    public DirectoryInfo CreateTempDirectory(DirectoryInfo? parent = null, string? name = null)
    {
        var tempDir = new DirectoryInfo(GetPath(parent, name));
        tempDir.Create();
        _TempDirectories.Add(tempDir);
        return tempDir;
    }

    private string GetPath(DirectoryInfo? parentDirectory, string? name)
    {
        var directory = parentDirectory ?? _WorkingDirectory.Value;
        var fileName = name ?? Path.GetRandomFileName();
        return Path.Combine(directory.FullName, fileName);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_Disposed || !disposing) {
            return;
        }

        _Disposed = true;

        ExceptionAggregator aggregator = new();

        var items = _TempFiles
            .Cast<FileSystemInfo>()
            .Concat(_TempDirectories)
            .Concat(_WorkingDirectory.IsValueCreated ? new[] { _WorkingDirectory.Value } : Enumerable.Empty<DirectoryInfo>());

        foreach (var fsi in items) {
            fsi.Refresh();
            if (!fsi.Exists) return;

            Action? action = fsi switch {
                FileInfo file => () => file.Delete(),
                DirectoryInfo dir => () => dir.Delete(recursive: true),
                _ => null,
            };

            if (action is null) return;

            aggregator.Run(() => {
                for (int i = 0; i < 100; i++) {
                    try {
                        action();
                        break;
                    } catch {
                        Thread.Sleep(TimeSpan.FromMilliseconds(10));
                    }
                }
            });
        }

        if (aggregator.HasExceptions) {
            throw aggregator.ToException();
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}