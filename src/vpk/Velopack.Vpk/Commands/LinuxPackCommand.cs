namespace Velopack.Vpk.Commands;

public class LinuxPackCommand : PackCommand
{
    public string Categories { get; private set; }

    public string Compression { get; private set; }

    public LinuxPackCommand()
        : base("pack", "Create a Linux .AppImage bundle from application files.", RuntimeOs.Linux)
    {
        this.RemoveOption(NoPortableOption);
        this.RemoveOption(NoInstOption);

        AddOption<string>((v) => Categories = v, "--categories")
            .SetDescription("Categories from the freedesktop.org Desktop Menu spec")
            .SetArgumentHelpName("NAMES");

        AddOption<string>((v) => Compression = v, "--compression")
            .SetDescription("Set the compression algorithm to use for the AppImage")
            .SetDefault("xz")
            .SetArgumentHelpName("ALGO")
            .MustBeOneOfStringValues(["gzip", "lzo", "lzma", "xz", "lz4", "zstd"]);
    }
}