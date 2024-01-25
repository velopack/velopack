using AsmResolver.DotNet;
using AsmResolver.DotNet.Bundles;
using AsmResolver.DotNet.Serialized;
using AsmResolver.PE;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.Win32Resources.Version;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Velopack.Packaging.Exceptions;

namespace Velopack.Packaging.Windows
{
    public class DotnetUtil
    {
        public static NuGetVersion VerifyVelopackApp(string exeFile, ILogger log)
        {
            try {
                NuGetVersion velopackVersion = null;
                IPEImage velopackDll = null;
                AssemblyDefinition mainAssy = null;
                mainAssy ??= LoadFullFramework(exeFile, ref velopackDll);
                mainAssy ??= LoadDncBundle(exeFile, ref velopackDll);

                if (mainAssy == null) {
                    // not a dotnet binary
                    return null;
                }

                if (velopackDll != null) {
                    try {
                        var versionInfo = VersionInfoResource.FromDirectory(velopackDll.Resources);
                        var actualInfo = versionInfo.GetChild<StringFileInfo>(StringFileInfo.StringFileInfoKey);
                        var versionTable = actualInfo.Tables[0];
                        var productVersion = versionTable.Where(v => v.Key == StringTable.ProductVersionKey).FirstOrDefault();
                        velopackVersion = NuGetVersion.Parse(productVersion.Value);
                    } catch (Exception ex) {
                        // don't really care
                        log.Debug(ex, "Unable to read Velopack.dll version info.");
                    }
                }

                var mainModule = mainAssy.Modules.Single();
                var entryPoint = mainModule.ManagedEntryPointMethod;

                foreach (var instr in entryPoint.CilMethodBody.Instructions) {
                    if (instr.OpCode.Code is CilCode.Call or CilCode.Callvirt or CilCode.Calli) {
                        SerializedMemberReference operand = instr.Operand as SerializedMemberReference;
                        if (operand != null && operand.IsMethod) {
                            if (operand.Name == "Run" && operand.DeclaringType.FullName == "Velopack.VelopackApp") {
                                // success!
                                if (velopackVersion != null) {
                                    log.Info($"Verified VelopackApp.Run() in '{entryPoint.FullName}', version {velopackVersion}.");
                                    if (velopackVersion != VelopackRuntimeInfo.VelopackProductVersion) {
                                        log.Warn(exeFile + " was built with a different version of Velopack than this tool. " +
                                            $"This may cause compatibility issues. Expected {VelopackRuntimeInfo.VelopackProductVersion}, " +
                                            $"but found {velopackVersion}.");
                                    }
                                    return velopackVersion;
                                } else {
                                    log.Warn("VelopackApp verified at entry point, but ProductVersion could not be checked.");
                                    return null;
                                }
                            }
                        }
                    }
                }

                // if we've iterated the whole main method and not found the call, then the velopack builder is missing
                throw new UserInfoException($"Unable to verify VelopackApp, in application main method '{entryPoint.FullName}'. " +
                    "Please ensure that 'VelopackApp.Build().Run()' is present in your Program.Main().");

            } catch (Exception ex) when (ex is not UserInfoException) {
                log.Error("Unable to verify VelopackApp: " + ex.Message);
            }

            return null;
        }

        private static AssemblyDefinition LoadFullFramework(string exeFile, ref IPEImage velopackDll)
        {
            try {
                var assy = AssemblyDefinition.FromFile(exeFile);
                var versionFile = Path.Combine(Path.GetDirectoryName(exeFile), "Velopack.dll");
                if (File.Exists(versionFile)) {
                    velopackDll = PEImage.FromFile(versionFile);
                }
                return assy;
            } catch (BadImageFormatException) {
                // not a .Net Framework binary
                return null;
            }
        }

        private static AssemblyDefinition LoadDncBundle(string exeFile, ref IPEImage velopackDll)
        {
            try {
                var bundle = BundleManifest.FromFile(exeFile);
                IList<BundleFile> embeddedFiles = null;

                try {
                    embeddedFiles = bundle.Files;
                } catch {
                    // not a SingleFileHost binary, so we'll search on disk
                    var parentDir = Path.GetDirectoryName(exeFile);
                    var versionFile = Path.Combine(parentDir, "Velopack.dll");
                    if (File.Exists(versionFile)) {
                        velopackDll = PEImage.FromFile(versionFile);
                    }

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

                var velopackEmbedded = embeddedFiles.SingleOrDefault(f => f.Type == BundleFileType.Assembly && f.RelativePath == "Velopack.dll");
                if (velopackEmbedded != null && velopackEmbedded.TryGetReader(out var readerVel)) {
                    velopackDll = PEImage.FromReader(readerVel);
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
}
