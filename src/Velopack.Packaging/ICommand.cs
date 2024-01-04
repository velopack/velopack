using Microsoft.Extensions.Logging;

namespace Velopack.Packaging
{
    internal interface ICommand<TOpt> where TOpt : class
    {
        Task Run(TOpt options, ILogger logger);
    }
}
