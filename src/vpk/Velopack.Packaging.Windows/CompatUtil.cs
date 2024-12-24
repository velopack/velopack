using AsmResolver.DotNet;
using AsmResolver.DotNet.Bundles;
using AsmResolver.DotNet.Serialized;
using AsmResolver.PE;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.Win32Resources.Version;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Velopack.Core;
using Velopack.Core.Abstractions;
using Velopack.Util;

namespace Velopack.Packaging.Windows;

public class CompatUtil
{
    private readonly ILogger _log;
    private readonly IFancyConsole _console;

    public CompatUtil(ILogger logger, IFancyConsole console)
    {
        _log = logger;
        _console = console;
    }

    public NuGetVersion Verify(string exeFile)
    {
        VerifyVelopackApp(exeFile);
        return VerifyVelopackVersion(exeFile);
    }

    public void VerifyVelopackApp(string exeFile)
    {
        try {
            AssemblyDefinition mainAssy = LoadDotnetAssembly(exeFile);
            if (mainAssy == null) {
                return;
            }

            ModuleDefinition mainModule = mainAssy.Modules.Single();
            if (!TrySearchAssemblyForVelopackApp(mainModule, out string result)) {
                if (result == null) {
                    // if we've iterated the whole main assembly and not found the call, then the velopack builder is missing
                    throw new UserInfoException($"Unable to verify VelopackApp is called. " +
                        "Please ensure that 'VelopackApp.Build().Run()' is present in your Program.Main().");
                }
                result = _console.EscapeMarkup(result);
                _log.Warn($"VelopackApp.Run() was found in method '{result}', which does not look like your application's entry point. " +
                   "It is [underline yellow]strongly recommended[/] that you move this to the very beginning of your Main() method. ");
            } else {
                _log.Info($"[green underline]Verified VelopackApp.Run()[/] in '{result}'.");
            }

        } catch (Exception ex) when (ex is not UserInfoException) {
            _log.Error("Unable to verify VelopackApp: " + ex.Message);
        }
    }

    public NuGetVersion VerifyVelopackVersion(string exeFile)
    {
        var rawVersion = GetVelopackVersion(exeFile);
        if (rawVersion == null) {
            return null;
        }

        var dllVersion = new Version(rawVersion.ToString("V", VersionFormatter.Instance));
        var myVersion = new Version(VelopackRuntimeInfo.VelopackNugetVersion.ToString("V", VersionFormatter.Instance));
        if (dllVersion == myVersion) {
            return new NuGetVersion(dllVersion);
        }

        if (dllVersion > myVersion) {
            //throw new UserInfoException($"Velopack library version is greater than vpk version ({dllVersion} > {myVersion}). This can cause compatibility issues, please update vpk first.");
            _log.Error($"Velopack library version is greater than vpk version ({dllVersion} > {myVersion}). " +
                $"This can cause compatibility issues, please update vpk first. [red underline]In a future version this will be a fatal error.[/]");
        } else {
            _log.Warn($"Velopack library version is lower than vpk version ({dllVersion} < {myVersion}). This can occasionally cause compatibility issues.");
        }
        return new NuGetVersion(dllVersion);
    }

    private static bool TrySearchAssemblyForVelopackApp(ModuleDefinition mainModule, out string velopackAppLocation)
    {
        MethodDefinition entryPoint = mainModule.ManagedEntryPointMethod;

        string SearchMethod(MethodDefinition method)
        {
            foreach (var instr in method.CilMethodBody.Instructions) {
                if (instr.OpCode.Code is CilCode.Call or CilCode.Callvirt or CilCode.Calli) {
                    var operand = instr.Operand as SerializedMemberReference;
                    if (operand != null) {
                        if (operand.Name == "Run" && operand.DeclaringType.FullName == "Velopack.VelopackApp") {
                            // success!
                            return method.FullName;
                        }
                    }
                }
            }
            return null;
        }

        string SearchType(TypeDefinition type)
        {
            // search all methods in type
            foreach (var method in type.Methods) {
                if (method.HasMethodBody) {
                    var result = SearchMethod(method);
                    if (result != null) {
                        return result;
                    }
                }
            }

            // then, search all nested types
            foreach (var nestedType in type.NestedTypes) {
                var result = SearchType(nestedType);
                if (result != null) {
                    return result;
                }
            }

            return null;
        }

        // search entry point first
        if (SearchMethod(entryPoint) is { } entryPointResult) {
            velopackAppLocation = entryPointResult;
            return true;
        }

        // then, iterate all methods in the main module
        foreach (var topType in mainModule.TopLevelTypes) {
            if (SearchType(topType) is { } topLevelTypeResult) {
                velopackAppLocation = topLevelTypeResult;
                return false;
            }
        }

        velopackAppLocation = null;
        return false;
    }

    public NuGetVersion GetVelopackVersion(string exeFile)
    {
        try {
            var velopackDll = FindVelopackDll(exeFile);
            if (velopackDll == null) {
                return null;
            }

            var versionInfo = VersionInfoResource.FromDirectory(velopackDll.Resources);
            var actualInfo = versionInfo.GetChild<StringFileInfo>(StringFileInfo.StringFileInfoKey);
            var versionTable = actualInfo.Tables[0];
            var productVersion = versionTable.Where(v => v.Key == StringTable.ProductVersionKey).FirstOrDefault();
            return NuGetVersion.Parse(productVersion.Value);
        } catch (Exception ex) {
            // don't really care
            _log.Debug(ex, "Unable to read Velopack.dll version info.");
        }

        return null;
    }

    private IPEImage FindVelopackDll(string exeFile)
    {
        var versionFile = Path.Combine(Path.GetDirectoryName(exeFile), "Velopack.dll");
        if (File.Exists(versionFile)) {
            _log.Debug(exeFile + " has Velopack.dll in the same directory.");
            return PEImage.FromFile(versionFile);
        }

        try {
            var bundle = BundleManifest.FromFile(exeFile);
            IList<BundleFile> embeddedFiles = bundle.Files;
            var velopackEmbedded = embeddedFiles.SingleOrDefault(f => f.Type == BundleFileType.Assembly && f.RelativePath == "Velopack.dll");
            if (velopackEmbedded != null && velopackEmbedded.TryGetReader(out var readerVel)) {
                _log.Debug(exeFile + " has Velopack.dll embedded in a SingleFileHost.");
                return PEImage.FromReader(readerVel);
            }
        } catch (BadImageFormatException) {
            // not an AppHost / SingleFileHost binary
            return null;
        }

        return null;
    }

    private AssemblyDefinition LoadDotnetAssembly(string exeFile)
    {
        try {
            var assy = AssemblyDefinition.FromFile(exeFile);
            return assy;
        } catch (BadImageFormatException) {
            // not a .Net Framework binary
        }

        try {
            var bundle = BundleManifest.FromFile(exeFile);
            IList<BundleFile> embeddedFiles = null;

            try {
                embeddedFiles = bundle.Files;
            } catch {
                // not a SingleFileHost binary, so we'll search on disk
                var parentDir = Path.GetDirectoryName(exeFile);
                var diskFile = Path.Combine(parentDir, Path.GetFileNameWithoutExtension(exeFile) + ".dll");
                if (File.Exists(diskFile)) {
                    return AssemblyDefinition.FromFile(diskFile);
                }

                var runtimeConfigFile = Directory.EnumerateFiles(parentDir, "*.runtimeconfig.json").SingleOrDefault();
                var possNameRuntime = Path.Combine(parentDir,
                    Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(runtimeConfigFile)) + ".dll");
                if (File.Exists(possNameRuntime)) {
                    return AssemblyDefinition.FromFile(possNameRuntime);
                }

                return null;
            }

            var runtimeConfig = embeddedFiles.SingleOrDefault(f => f.Type == BundleFileType.RuntimeConfigJson);
            if (runtimeConfig != null) {
                var possName1 = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(runtimeConfig.RelativePath)) + ".dll";
                var file = embeddedFiles.SingleOrDefault(f => f.Type == BundleFileType.Assembly && f.RelativePath == possName1);
                if (file != null && file.TryGetReader(out var reader)) {
                    return AssemblyDefinition.FromReader(reader);
                }
            }

            var possName2 = Path.GetFileNameWithoutExtension(exeFile) + ".dll";
            var file2 = embeddedFiles.SingleOrDefault(f => f.Type == BundleFileType.Assembly && f.RelativePath == possName2);
            if (file2 != null && file2.TryGetReader(out var reader2)) {
                return AssemblyDefinition.FromReader(reader2);
            }

            return null;
        } catch (BadImageFormatException) {
            // not an AppHost / SingleFileHost binary
            return null;
        }
    }
}
