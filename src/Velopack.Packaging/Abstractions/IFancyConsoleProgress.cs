namespace Velopack.Packaging.Abstractions
{
    public interface IFancyConsoleProgress
    {
        Task RunTask(string name, Func<Action<int>, Task> fn);
    }
}
