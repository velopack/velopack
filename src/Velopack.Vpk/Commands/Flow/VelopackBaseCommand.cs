namespace Velopack.Vpk.Commands.Flow;
#nullable enable

public abstract class VelopackBaseCommand : OutputCommand
{
    public string? TeamName { get; private set; }

    public string? ProjectName { get; private set; }

    protected VelopackBaseCommand(string name, string description)
        : base(name, description)
    {
        AddOption<string>((v) => TeamName = v, "--team-name", "-t")
            .SetDescription("The name of the team")
            .SetRequired();

        AddOption<string>((v) => ProjectName = v, "--project-name", "-p")
            .SetDescription("The name of the project")
            .SetRequired();
    }
}
