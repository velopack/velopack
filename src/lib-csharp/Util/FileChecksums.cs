namespace Velopack.Util
{
    internal sealed class FileChecksums
    {
        public string SHA1 { get; }
        public string SHA256 { get; }

        public FileChecksums(string sha1, string sha256)
        {
            SHA1 = sha1;
            SHA256 = sha256;
        }

        public void Deconstruct(out string sha1, out string sha256)
        {
            sha1 = SHA1;
            sha256 = SHA256;
        }
    }
}
