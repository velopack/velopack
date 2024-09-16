#nullable enable

using System.Net.Http;
using Serilog.Core;

namespace Velopack.Vpk.Commands.Flow;
public class ApiCommand : VelopackServiceCommand
{
    public string Method { get; private set; } = "";

    public string Endpoint { get; private set; } = "";

    public string? Body { get; private set; }

    public ApiCommand()
        : base("api", "Invoke velopack flow API endpoints")
    {
        AddOption<string>(v => Method = v, "--method", "-m")
            .SetDescription("The HTTP method for the endpoint")
            .SetArgumentHelpName("METHOD")
            .SetRequired()
            .SetDefault(HttpMethod.Get.Method);

        AddOption<string>(v => Endpoint = v, "--endpoint", "-e")
            .SetDescription("The relative URI for the endpoint")
            .SetArgumentHelpName("URI")
            .SetRequired();

        AddOption<string>(v => Body = v, "--body", "-b")
            .SetDescription("The body of the HTTP message")
            .SetArgumentHelpName("BODY");
    }

    public override void Initialize(LoggingLevelSwitch logLevelSwitch)
    {
        base.Initialize(logLevelSwitch);
        logLevelSwitch.MinimumLevel = Serilog.Events.LogEventLevel.Warning;
    }
}
