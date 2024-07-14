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
