namespace Velopack.Core.Abstractions;

public interface ICommand<TOpt> where TOpt : class
{
    Task Run(TOpt options);
}
