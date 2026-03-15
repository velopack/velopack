using System.Runtime.CompilerServices;
using Velopack.TestCommon;

namespace Velopack.CrossCompile.Tests;

internal static class Init
{
    [ModuleInitializer]
    internal static void Initialize() => TestsInit.Init();
}
