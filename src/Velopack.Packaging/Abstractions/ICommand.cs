using Microsoft.Extensions.Logging;

namespace Velopack.Packaging.Abstractions
{
    public interface ICommand<TOpt> where TOpt : class
    {
        Task Run(TOpt options);
    }
}
