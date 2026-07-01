using System.Runtime.CompilerServices;
using Velopack.TestCommon;

namespace Velopack.Deployment.Tests;

internal static class Init
{
    [ModuleInitializer]
    internal static void Initialize() => TestsInit.Init();
}
