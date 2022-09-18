using System;
using System.Collections.Generic;

namespace Squirrel.CommandLine.Windows
{
    internal class SigningOptions : BaseOptions
    {
        public const int SignParallelDefault = 10;

        public string signParams { get; set; }
        public string signTemplate { get; set; }
        public bool signSkipDll { get; set; }
        public int signParallel { get; set; }

        public void SignFiles(string rootDir, params string[] filePaths)
        {
            if (String.IsNullOrEmpty(signParams) && String.IsNullOrEmpty(signTemplate)) {
                Log.Debug($"No signing paramaters provided, {filePaths.Length} file(s) will not be signed.");
                return;
            }

            if (!String.IsNullOrEmpty(signTemplate)) {
                Log.Info($"Preparing to sign {filePaths.Length} files with custom signing template");
                foreach (var f in filePaths) {
                    HelperExe.SignPEFileWithTemplate(f, signTemplate);
                }
                return;
            }

            // signtool.exe does not work if we're not on windows.
            if (!SquirrelRuntimeInfo.IsWindows) return;

            if (!String.IsNullOrEmpty(signParams)) {
                Log.Info($"Preparing to sign {filePaths.Length} files with embedded signtool.exe with parallelism of {signParallel}");
                HelperExe.SignPEFilesWithSignTool(rootDir, filePaths, signParams, signParallel);
            }
        }
    }

    internal class ReleasifyOptions : SigningOptions
    {
        public string package { get; set; }
        public string baseUrl { get; set; }
        public string framework { get; set; }
        public string splashImage { get; set; }
        public string icon { get; set; }
        public string appIcon { get; set; }
        public bool noDelta { get; set; }
        public string msi { get; set; }
        public string debugSetupExe { get; set; }

        public List<string> mainExes { get; } = new();
    }

    internal class PackOptions : ReleasifyOptions
    {
        public string packId { get; set; }
        public string packTitle { get; set; }
        public string packVersion { get; set; }
        public string packAuthors { get; set; }
        public string packDirectory { get; set; }
        public bool includePdb { get; set; }
        public string releaseNotes { get; set; }
    }
}