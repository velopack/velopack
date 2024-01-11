namespace Divergic.Logging.Xunit
{
    using System;

    internal class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new NoopDisposable();

        public void Dispose()
        {
            // No-op
        }
    }
}