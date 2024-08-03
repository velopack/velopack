#pragma once

#if BUILDING_PLAYER_DLL
#define UNITY_API __declspec(dllexport)
#else
#define UNITY_API __declspec(dllimport)
#endif

extern "C" UNITY_API int UnityMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPWSTR lpCmdLine, int nShowCmd);
