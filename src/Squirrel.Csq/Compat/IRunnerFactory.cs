using Squirrel.Csq.Commands;

namespace Squirrel.Csq.Compat;

public interface IRunnerFactory
{
    public Task CreateAndExecuteAsync<T>(string commandName, T options) where T : BaseCommand;
    public Task<ICommandRunner> CreateAsync();
}
