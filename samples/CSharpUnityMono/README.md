# UnitySample(Mono)
This project integrates Velopack into unity by way of a [nuget for Unity](https://github.com/GlitchEnzo/NuGetForUnity) package.

## How to use
1. Clone this repository
2. Open the project in Unity
3. Build the project to \Build folder
4. Open BuildVisualStudioSolution\UnityMonoSample.sln
5. Build the solution
6. copy BuildVisualStudioSolution\build\bin\x64\Master\UnityMonoSample.exe to \Build folder
7. use vpk cli tool to pack your project `vpk pack -u UnityMonoSample -v 0.0.1 -p .\Build -e UnityMonoSample.exe`

## Requirements for your project
install the nuget package `Velopack` in your project
you can use tool like [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity)
```
openupm add com.github-glitchenzo.nugetforunity
```

Currently, Velopack is only support for Unity mono runtime, it does not support for IL2CPP, due to the following reasons:

https://docs.unity3d.com/2022.3/Documentation/Manual/ScriptingRestrictions.html

### System.Diagnostics.Process API
IL2CPP doesn’t support the `System.Diagnostics.Process` API methods. For cases where this is required on desktop platforms, use the Mono scripting backend.

----

If you still want to use Velopack in IL2CPP, you can use the following workaround

(remember that this is not recommended and may change in the future):

1. clone the Velopack repository
2. edit the VelopackRuntimeInfo.cs file like this:
```csharp
static VelopackRuntimeInfo()
{
    //delete the following line
    //EntryExePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
    EntryExePath = Application.dataPath;
    if (!Application.isEditor)
    {
        EntryExePath = Path.Combine(EntryExePath, "..", Application.productName + ".exe");
    }
}
```
3. build the Velopack project
4. replace the Velopack.dll in your project with the new Velopack.dll

## FAQ

### Why do I need to build the project twice?

The first build is to generate the normal Unity project. 

The second build is to generate the .exe file that Velopack needs to pack. Since the Unity executable on Windows is packaged in C++, its process needs some modification to integrate Velopack's [App Hooks](https://docs.velopack.io/integrating/hooks).
you can see the following code in the Main.cpp file
```c++
#include "PrecompiledHeader.h"
#include "..\UnityPlayerStub\Exports.h"

//for Velopack
#include "..\Velopack.hpp"
#include "Windows.h"
#include <shellapi.h>
//for Velopack

// Hint that the discrete gpu should be enabled on optimus/enduro systems
// NVIDIA docs: http://developer.download.nvidia.com/devzone/devcenter/gamegraphics/files/OptimusRenderingPolicies.pdf
// AMD forum post: http://devgurus.amd.com/thread/169965
extern "C"
{
    __declspec(dllexport) DWORD NvOptimusEnablement = 0x00000001;
    __declspec(dllexport) int AmdPowerXpressRequestHighPerformance = 1;
}

int WINAPI wWinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPWSTR lpCmdLine, int nShowCmd)
{
    //for Velopack
    int pNumArgs = 0;
    wchar_t** args = CommandLineToArgvW(lpCmdLine, &pNumArgs);
    Velopack::startup(args, pNumArgs);
    //for Velopack
    return UnityMain(hInstance, hPrevInstance, lpCmdLine, nShowCmd);
}

```
## Note
Do not use `ApplyUpdatesAndExit` because Environment.Exit is not supported. Instead, use the following to apply update:

```csharp
updateManager.WaitExitThenApplyUpdates(...);
UnityEngine.Application.Quit();
```

This will work because `WaitExitThenApplyUpdates` will not call `Environment.Exit`, but it will prepare up the Velopack updater to install the update when unity closes.