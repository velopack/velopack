namespace Velopack.Vpk.Commands;
public class LoginCommand : VelopackServiceCommand
{
    public LoginCommand()
        : base("login", "Login to Velopack's hosted service.")
    {
        //Just hiding this for now as it is not ready for mass consumption.
        Hidden = true;
    }
}
