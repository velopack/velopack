using System.Collections.Generic;

namespace Squirrel.CommandLine.OSX
{
    internal class PackOptions : BaseOptions
    {
        public string packId { get; set; }
        public string packTitle { get; set; }
        public string packVersion { get; set; }
        public string packAuthors { get; set; }
        public string packDirectory { get; set; }
        public bool includePdb { get; set; }
        public string releaseNotes { get; set; }
        public string icon { get; set; }
        public string mainExe { get; set; }
        public bool noDelta { get; set; }
        public string signAppIdentity { get; set; }
        public string signInstallIdentity { get; set; }
        public string signEntitlements { get; set; }
        public string notaryProfile { get; set; }
        public string bundleId { get; set; }
        public bool noPkg { get; set; }
        public Dictionary<string, string> pkgContent { get; } = new();
    }
}