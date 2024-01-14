using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Velopack.Packaging.Unix.Commands
{
    public class LinuxPackOptions : IPackOptions
    {
        public DirectoryInfo ReleaseDir { get; set; }

        public string PackId { get; set; }

        public string PackVersion { get; set; }

        public string PackDirectory { get; set; }

        public string PackAuthors { get; set; }

        public string PackTitle { get; set; }

        public string EntryExecutableName { get; set; }

        public string Icon { get; set; }

        public RID TargetRuntime { get; set; }

        public string ReleaseNotes { get; set; }

        public DeltaMode DeltaMode { get; set; } = DeltaMode.BestSpeed;

        public string Channel { get; set; }

        public bool PackIsAppDir { get; set; }
    }
}
