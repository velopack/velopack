namespace Velopack.Packaging.Commands
{
    public class DeltaPatchOptions
    {
        public string BasePackage { get; set; }

        public FileInfo[] PatchFiles { get; set; }

        public string OutputFile { get; set; }
    }
}
