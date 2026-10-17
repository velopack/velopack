global using Velopack.TestCommon;
using System.Runtime.CompilerServices;

namespace Velopack.Deployment.Tests;

internal static class Init
{
    [ModuleInitializer]
    internal static void Initialize() => TestsInit.Init();
}
