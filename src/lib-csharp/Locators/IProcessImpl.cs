#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

using System.Collections.Generic;

namespace Velopack.Locators
{
    public interface IProcessImpl
    {
        string GetCurrentProcessPath();
        uint GetCurrentProcessId();
        void StartProcess(string exePath, IEnumerable<string> args, string workDir, bool showWindow);
    }
}