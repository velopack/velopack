using AutoMapper;
using AutoMapper.Internal;
using Microsoft.Extensions.DependencyInjection;
using Velopack.Deployment;
using Velopack.Packaging.Abstractions;
using Velopack.Packaging.Commands;
using Velopack.Packaging.Exceptions;
using Velopack.Packaging.Unix.Commands;
using Velopack.Packaging.Windows.Commands;
using Velopack.Vpk.Commands;
using Velopack.Vpk.Updates;

namespace Velopack.Vpk;

public static class CommandMapper
{
    private static readonly List<TypePair> RequiredCommandMaps = new();

    public static void Validate()
    {
        var config = GetMapperConfig();
        config.AssertConfigurationIsValid();

        var rootCommand = new CliRootCommand();
        rootCommand.PopulateVelopackCommands(null);

        var global = (IGlobalConfiguration) config;
        foreach (var pair in RequiredCommandMaps) {
            var map = global.FindTypeMapFor(pair);
            if (map == null) {
                throw new Exception($"Missing map for {pair.SourceType.Name} -> {pair.DestinationType.Name}");
            }
        }
    }

    public static void PopulateVelopackCommands(this CliRootCommand rootCommand, IServiceProvider provider)
    {
        if (VelopackRuntimeInfo.IsWindows) {
            rootCommand.AddCommand<WindowsPackCommand, WindowsPackCommandRunner, WindowsPackOptions>(provider);
        } else if (VelopackRuntimeInfo.IsOSX) {
            rootCommand.AddCommand<OsxBundleCommand, OsxBundleCommandRunner, OsxBundleOptions>(provider);
            rootCommand.AddCommand<OsxPackCommand, OsxPackCommandRunner, OsxPackOptions>(provider);
        } else if (VelopackRuntimeInfo.IsLinux) {
            rootCommand.AddCommand<LinuxPackCommand, LinuxPackCommandRunner, LinuxPackOptions>(provider);
        } else {
            throw new NotSupportedException("Unsupported OS platform: " + VelopackRuntimeInfo.SystemOs.GetOsLongName());
        }

        var downloadCommand = new CliCommand("download", "Download's the latest release from a remote update source.");
        downloadCommand.AddRepositoryDownload<GitHubDownloadCommand, GitHubRepository, GitHubDownloadOptions>(provider);
        downloadCommand.AddRepositoryDownload<S3DownloadCommand, S3Repository, S3DownloadOptions>(provider);
        downloadCommand.AddRepositoryDownload<HttpDownloadCommand, HttpRepository, HttpDownloadOptions>(provider);
        rootCommand.Add(downloadCommand);

        var uploadCommand = new CliCommand("upload", "Upload local package(s) to a remote update source.");
        uploadCommand.AddRepositoryUpload<GitHubUploadCommand, GitHubRepository, GitHubUploadOptions>(provider);
        uploadCommand.AddRepositoryUpload<S3UploadCommand, S3Repository, S3UploadOptions>(provider);
        rootCommand.Add(uploadCommand);

        var deltaCommand = new CliCommand("delta", "Utilities for creating or applying delta packages.");
        deltaCommand.AddCommand<DeltaGenCommand, DeltaGenCommandRunner, DeltaGenOptions>(provider);
        deltaCommand.AddCommand<DeltaPatchCommand, DeltaPatchCommandRunner, DeltaPatchOptions>(provider);
        rootCommand.Add(deltaCommand);
    }

    private static MapperConfiguration GetMapperConfig()
    {
        return new MapperConfiguration(cfg => {
            cfg.CreatePlatformMap<OsxPackCommand, OsxPackOptions>();
            cfg.CreatePlatformMap<WindowsPackCommand, WindowsPackOptions>();
            cfg.CreatePlatformMap<LinuxPackCommand, LinuxPackOptions>();
            cfg.CreateOutputMap<OsxBundleCommand, OsxBundleOptions>();
            cfg.CreateOutputMap<GitHubDownloadCommand, GitHubDownloadOptions>();
            cfg.CreateOutputMap<GitHubUploadCommand, GitHubUploadOptions>();
            cfg.CreateOutputMap<HttpDownloadCommand, HttpDownloadOptions>();
            cfg.CreateOutputMap<S3DownloadCommand, S3DownloadOptions>();
            cfg.CreateOutputMap<S3UploadCommand, S3UploadOptions>();
            cfg.CreateMap<DeltaGenCommand, DeltaGenOptions>();
            cfg.CreateMap<DeltaPatchCommand, DeltaPatchOptions>();
        });
    }

    private static CliCommand AddCommand<TCli, TCmd, TOpt>(this CliCommand parent, IServiceProvider provider)
        where TCli : BaseCommand, new()
        where TCmd : ICommand<TOpt>
        where TOpt : class, new()
    {
        return parent.Add<TCli, TOpt>(provider, (options) => {
            var runner = ActivatorUtilities.CreateInstance<TCmd>(provider);
            return runner.Run(options);
        });
    }

    private static CliCommand AddRepositoryDownload<TCli, TCmd, TOpt>(this CliCommand parent, IServiceProvider provider)
        where TCli : BaseCommand, new()
        where TCmd : IRepositoryCanDownload<TOpt>
        where TOpt : RepositoryOptions, new()
    {
        return parent.Add<TCli, TOpt>(provider, (options) => {
            var runner = ActivatorUtilities.CreateInstance<TCmd>(provider);
            return runner.DownloadLatestFullPackageAsync(options);
        });
    }

    private static CliCommand AddRepositoryUpload<TCli, TCmd, TOpt>(this CliCommand parent, IServiceProvider provider)
        where TCli : BaseCommand, new()
        where TCmd : IRepositoryCanUpload<TOpt>
        where TOpt : RepositoryOptions, new()
    {
        return parent.Add<TCli, TOpt>(provider, (options) => {
            var runner = ActivatorUtilities.CreateInstance<TCmd>(provider);
            return runner.UploadMissingAssetsAsync(options);
        });
    }

    private static CliCommand Add<TCli, TOpt>(this CliCommand parent, IServiceProvider provider, Func<TOpt, Task> fn)
        where TCli : BaseCommand, new()
        where TOpt : class, new()
    {
        RequiredCommandMaps.Add(new TypePair(typeof(TCli), typeof(TOpt)));
        var command = new TCli();
        command.SetAction(async (ctx, token) => {
            var logger = provider.GetRequiredService<ILogger>();
            logger.LogInformation($"[bold]{Program.INTRO}[/]");
            var updateCheck = new UpdateChecker(logger);
            await updateCheck.CheckForUpdates();

            command.SetProperties(ctx);
            var mapper = GetMapperConfig().CreateMapper();
            var options = mapper.Map<TOpt>(command);

            try {
                await fn(options);
                return 0;
            } catch (Exception ex) when (ex is ProcessFailedException or UserInfoException) {
                // some exceptions are just user info / user error, so don't need a stack trace.
                logger.Fatal($"[bold orange3]{ex.Message}[/]");
                return -1;
            } catch (Exception ex) {
                logger.Fatal(ex, $"Command {typeof(TCli).Name} had an exception.");
                return -1;
            }
        });
        parent.Subcommands.Add(command);
        return command;
    }

    private static IMappingExpression<TSource, TDestination> CreatePlatformMap<TSource, TDestination>(
        this IMapperConfigurationExpression cfg)
        where TSource : PlatformCommand
        where TDestination : IPackOptions
    {
        return cfg.CreateMap<TSource, TDestination>()
               .ForMember(x => x.ReleaseDir, x => x.MapFrom(z => z.GetReleaseDirectory()))
               .ForMember(x => x.TargetRuntime, x => x.MapFrom(z => z.GetRid()));
    }

    private static IMappingExpression<TSource, TDestination> CreateOutputMap<TSource, TDestination>(
       this IMapperConfigurationExpression cfg)
       where TSource : OutputCommand
       where TDestination : IOutputOptions
    {
        return cfg.CreateMap<TSource, TDestination>()
               .ForMember(x => x.ReleaseDir, x => x.MapFrom(z => z.GetReleaseDirectory()));
    }
}
