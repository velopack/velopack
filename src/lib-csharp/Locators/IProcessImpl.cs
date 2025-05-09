#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

using System.Collections;
using System.Collections.Generic;

namespace Velopack.Locators
{
    public interface IProcessImpl
    {
        public string GetCurrentProcessPath();
        public uint GetCurrentProcessId();
        public void StartProcess(string exePath, IEnumerable<string> args, string workDir, bool showWindow);
    }
}