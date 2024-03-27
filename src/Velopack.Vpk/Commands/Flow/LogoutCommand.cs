namespace Velopack.Vpk.Commands.Flow;
public class LogoutCommand : VelopackServiceCommand
{
    public LogoutCommand()
        : base("logout", "Remove stored credentials for Velopack Flow.")
    {
    }
}
