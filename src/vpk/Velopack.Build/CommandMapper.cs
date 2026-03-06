#if NET10_0_OR_GREATER
using Riok.Mapperly.Abstractions;
using Velopack.Packaging.Compression;
using Velopack.Vpk.Commands.Packaging;

namespace Velopack.Build;

[Mapper(
    RequiredMappingStrategy = RequiredMappingStrategy.Target,
    EnabledConversions = MappingConversionType.None)]
public static partial class CommandMapper
{
    public static string? ToEnumString(DeltaMode mode) => mode.ToString();

    [MapProperty(nameof(OsxPackCommand.DeltaMode), nameof(PackTask.DeltaMode), Use = nameof(ToEnumString))]
    // MSBuild
    [MapperIgnoreTarget(nameof(PackTask.VelopackBaseUrl))]
    [MapperIgnoreTarget(nameof(PackTask.Timeout))]
    [MapperIgnoreTarget(nameof(PackTask.HostObject))]
    [MapperIgnoreTarget(nameof(PackTask.BuildEngine))]

    //Windows
    [MapperIgnoreTarget(nameof(PackTask.InstLocation))]
    [MapperIgnoreTarget(nameof(PackTask.Runtimes))]
    [MapperIgnoreTarget(nameof(PackTask.MsiBanner))]
    [MapperIgnoreTarget(nameof(PackTask.MsiLogo))]
    [MapperIgnoreTarget(nameof(PackTask.MsiVersionOverride))]
    [MapperIgnoreTarget(nameof(PackTask.BuildMsi))]
    [MapperIgnoreTarget(nameof(PackTask.SplashImage))]
    [MapperIgnoreTarget(nameof(PackTask.SplashProgressColor))]
    [MapperIgnoreTarget(nameof(PackTask.InstLicenseRtf))]
    [MapperIgnoreTarget(nameof(PackTask.SkipVelopackAppCheck))]
    [MapperIgnoreTarget(nameof(PackTask.AzureTrustedSignFile))]
    [MapperIgnoreTarget(nameof(PackTask.SignParameters))]
    [MapperIgnoreTarget(nameof(PackTask.SignExclude))]
    [MapperIgnoreTarget(nameof(PackTask.SignParallel))]
    [MapperIgnoreTarget(nameof(PackTask.SignTemplate))]
    [MapperIgnoreTarget(nameof(PackTask.Shortcuts))]

    //Linux
    [MapperIgnoreTarget(nameof(PackTask.Categories))]
    [MapperIgnoreTarget(nameof(PackTask.Compression))]
    public static partial PackTask ToCommand(this OsxPackCommand cmd);


    // MSBuild
    [MapperIgnoreTarget(nameof(PackTask.VelopackBaseUrl))]
    [MapperIgnoreTarget(nameof(PackTask.Timeout))]
    [MapperIgnoreTarget(nameof(PackTask.HostObject))]
    [MapperIgnoreTarget(nameof(PackTask.BuildEngine))]

    //OSX
    [MapperIgnoreTarget(nameof(PackTask.SignAppIdentity))]
    [MapperIgnoreTarget(nameof(PackTask.SignInstallIdentity))]
    [MapperIgnoreTarget(nameof(PackTask.SignEntitlements))]
    [MapperIgnoreTarget(nameof(PackTask.SignDisableDeep))]
    [MapperIgnoreTarget(nameof(PackTask.NotaryProfile))]
    [MapperIgnoreTarget(nameof(PackTask.Keychain))]
    [MapperIgnoreTarget(nameof(PackTask.BundleId))]
    [MapperIgnoreTarget(nameof(PackTask.InfoPlistPath))]

    //Linux
    [MapperIgnoreTarget(nameof(PackTask.Categories))]
    [MapperIgnoreTarget(nameof(PackTask.Compression))]
    public static partial PackTask ToCommand(this WindowsPackCommand cmd);


    // MSBuild
    [MapperIgnoreTarget(nameof(PackTask.VelopackBaseUrl))]
    [MapperIgnoreTarget(nameof(PackTask.Timeout))]
    [MapperIgnoreTarget(nameof(PackTask.HostObject))]
    [MapperIgnoreTarget(nameof(PackTask.BuildEngine))]

    //OSX
    [MapperIgnoreTarget(nameof(PackTask.SignAppIdentity))]
    [MapperIgnoreTarget(nameof(PackTask.SignInstallIdentity))]
    [MapperIgnoreTarget(nameof(PackTask.SignEntitlements))]
    [MapperIgnoreTarget(nameof(PackTask.SignDisableDeep))]
    [MapperIgnoreTarget(nameof(PackTask.NotaryProfile))]
    [MapperIgnoreTarget(nameof(PackTask.Keychain))]
    [MapperIgnoreTarget(nameof(PackTask.BundleId))]
    [MapperIgnoreTarget(nameof(PackTask.InfoPlistPath))]

    //Windows
    [MapperIgnoreTarget(nameof(PackTask.InstLocation))]
    [MapperIgnoreTarget(nameof(PackTask.Runtimes))]
    [MapperIgnoreTarget(nameof(PackTask.MsiBanner))]
    [MapperIgnoreTarget(nameof(PackTask.MsiLogo))]
    [MapperIgnoreTarget(nameof(PackTask.MsiVersionOverride))]
    [MapperIgnoreTarget(nameof(PackTask.BuildMsi))]
    [MapperIgnoreTarget(nameof(PackTask.SplashImage))]
    [MapperIgnoreTarget(nameof(PackTask.SplashProgressColor))]
    [MapperIgnoreTarget(nameof(PackTask.InstLicenseRtf))]
    [MapperIgnoreTarget(nameof(PackTask.SkipVelopackAppCheck))]
    [MapperIgnoreTarget(nameof(PackTask.AzureTrustedSignFile))]
    [MapperIgnoreTarget(nameof(PackTask.SignParameters))]
    [MapperIgnoreTarget(nameof(PackTask.SignExclude))]
    [MapperIgnoreTarget(nameof(PackTask.SignParallel))]
    [MapperIgnoreTarget(nameof(PackTask.SignTemplate))]
    [MapperIgnoreTarget(nameof(PackTask.Shortcuts))]

    // Both Windows and OSX
    [MapperIgnoreTarget(nameof(PackTask.InstWelcome))]
    [MapperIgnoreTarget(nameof(PackTask.InstReadme))]
    [MapperIgnoreTarget(nameof(PackTask.InstLicense))]
    [MapperIgnoreTarget(nameof(PackTask.InstConclusion))]
    public static partial PackTask ToCommand(this LinuxPackCommand cmd);
}
#endif