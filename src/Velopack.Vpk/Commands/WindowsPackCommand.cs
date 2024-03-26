namespace Velopack.Vpk.Commands;

public class WindowsPackCommand : PackCommand
{
    public string Runtimes { get; private set; }

    public string SplashImage { get; private set; }

    public bool SkipVelopackAppCheck { get; private set; }

    public string SignParameters { get; private set; }

    public bool SignSkipDll { get; private set; }

    public int SignParallel { get; private set; }

    public string SignTemplate { get; private set; }

    public WindowsPackCommand()
        : base("pack", "Creates a release from a folder containing application files.")
    {
        EntryExecutableNameOption.RequiresExtension(".exe");
        IconOption.RequiresExtension(".ico");

        AddOption<string>((v) => Runtimes = v, "-f", "--framework")
            .SetDescription("List of required runtimes to install during setup. example: 'net6-x64-desktop,vcredist143'.")
            .SetArgumentHelpName("RUNTIMES");

        AddOption<FileInfo>((v) => SplashImage = v.ToFullNameOrNull(), "-s", "--splashImage")
            .SetDescription("Path to image displayed during installation.")
            .SetArgumentHelpName("PATH")
            .MustExist();

        AddOption<bool>((v) => SkipVelopackAppCheck = v, "--skipVeloAppCheck")
            .SetDescription("Skip the VelopackApp builder verification.")
            .SetHidden();

        var signTemplate = AddOption<string>((v) => SignTemplate = v, "--signTemplate")
          .SetDescription("Use a custom signing command. {{file}} will be substituted.")
          .SetArgumentHelpName("COMMAND")
          .MustContain("{{file}}");

        AddOption<bool>((v) => SignSkipDll = v, "--signSkipDll")
            .SetDescription("Only signs EXE files, and skips signing DLL files.")
            .SetHidden();

        if (VelopackRuntimeInfo.IsWindows) {
            var signParams = AddOption<string>((v) => SignParameters = v, "--signParams", "-n")
                .SetDescription("Sign files via signtool.exe using these parameters.")
                .SetArgumentHelpName("PARAMS");

            this.AreMutuallyExclusive(signTemplate, signParams);

            AddOption<int>((v) => SignParallel = v, "--signParallel")
                .SetDescription("The number of files to sign in each call to signtool.exe.")
                .SetArgumentHelpName("NUM")
                .MustBeBetween(1, 1000)
                .SetHidden()
                .SetDefault(10);
        }
    }
}