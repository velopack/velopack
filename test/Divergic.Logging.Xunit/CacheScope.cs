namespace Divergic.Logging.Xunit
{
    using System;

    internal class CacheScope : IDisposable
    {
        private readonly Action _onScopeEnd;
        private readonly IDisposable _scope;

        public CacheScope(IDisposable scope, object? state, Action onScopeEnd)
        {
            _scope = scope;
            State = state;
            _onScopeEnd = onScopeEnd;
        }

        public void Dispose()
        {
            // Pass on the end scope request
            _scope.Dispose();

            // Clean up the scope in the cache logger
            _onScopeEnd.Invoke();
        }

        public object? State { get; }
    }
}