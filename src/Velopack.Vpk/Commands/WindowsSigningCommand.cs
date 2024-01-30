namespace Velopack.Vpk.Commands;

public class WindowsSigningCommand : PlatformCommand
{
    public string SignParameters { get; private set; }

    public bool SignSkipDll { get; private set; }

    public int SignParallel { get; private set; }

    public string SignTemplate { get; private set; }

    protected WindowsSigningCommand(string name, string description)
        : base(name, description)
    {
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
