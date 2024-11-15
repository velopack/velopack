#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>
#include <stdlib.h>
#include <malloc.h>
#include <memory.h>
#include <tchar.h>
#include <iostream>

#include "constants.h"
#include "Velopack.hpp"

#pragma comment(linker, \
  "\"/manifestdependency:type='Win32' "\
  "name='Microsoft.Windows.Common-Controls' "\
  "version='6.0.0.0' "\
  "processorArchitecture='*' "\
  "publicKeyToken='6595b64144ccf1df' "\
  "language='*'\"")
#pragma comment(lib, "ComCtl32.lib")

using namespace Velopack;

HINSTANCE hInst;
const WCHAR szTitle[] = L"Velopack C++ Sample App";
const WCHAR szWindowClass[] = L"VeloCppWinSample";

std::unique_ptr<UpdateManager> manager;
std::optional<UpdateInfo> updInfo = std::nullopt;
bool downloaded = false;

// Forward declarations of functions included in this code module:
int					MessageBoxCentered(HWND hWnd, LPCTSTR lpText, LPCTSTR lpCaption, UINT uType);
ATOM                MyRegisterClass(HINSTANCE hInstance);
BOOL                InitInstance(HINSTANCE, int);
LRESULT CALLBACK    WndProc(HWND, UINT, WPARAM, LPARAM);
INT_PTR CALLBACK    About(HWND, UINT, WPARAM, LPARAM);
std::wstring        Utf8ToWString(std::string const& str);
std::string         WStringToUtf8(std::wstring const& wstr);

void handle_vpkc_log(void* pUserData, const char* pszLevel, const char* pszMessage)
{
    std::cout << pszLevel << ": " << pszMessage << std::endl;
}

int APIENTRY wWinMain(_In_ HINSTANCE hInstance,
    _In_opt_ HINSTANCE hPrevInstance,
    _In_ LPWSTR    lpCmdLine,
    _In_ int       nCmdShow)
{
    // Redirect StdOut to console window
    AllocConsole();
    FILE* fp;
    _wfreopen_s(&fp, L"CONOUT$", L"w", stdout);
    _wfreopen_s(&fp, L"CONOUT$", L"w", stderr);
    _wfreopen_s(&fp, L"CONIN$", L"r", stdin);

    // Initialize Velopack log capture
    std::cout << "Velopack C++ Sample App" << std::endl;
    vpkc_set_logger(handle_vpkc_log, 0);

    // This should run as early as possible in the main method.
    // Velopack may exit / restart the app at this point. 
    // See VelopackApp class for more options/configuration.
    VelopackApp::Build()
        .Run();

    try {
        // If the app is not installed, creating an UpdateManager will throw an exception.
        manager = std::make_unique<UpdateManager>(UPDATE_URL);
    }
    catch (std::exception& e) {
        std::wstring what = Utf8ToWString(e.what());
        MessageBoxCentered(nullptr, what.c_str(), szTitle, MB_OK | MB_ICONERROR);
        return 1;
    }

    MyRegisterClass(hInstance);
    if (!InitInstance(hInstance, nCmdShow)) {
        return FALSE;
    }

    // Main message loop:
    MSG msg;
    while (GetMessage(&msg, nullptr, 0, 0)) {
        TranslateMessage(&msg);
        DispatchMessage(&msg);
    }

    return (int)msg.wParam;
}

ATOM MyRegisterClass(HINSTANCE hInstance)
{
    WNDCLASSEXW wcex;
    wcex.cbSize = sizeof(WNDCLASSEX);
    wcex.style = CS_HREDRAW | CS_VREDRAW;
    wcex.lpfnWndProc = WndProc;
    wcex.cbClsExtra = 0;
    wcex.cbWndExtra = 0;
    wcex.hInstance = hInstance;
    wcex.hCursor = LoadCursor(nullptr, IDC_ARROW);
    wcex.hbrBackground = (HBRUSH)(COLOR_WINDOW + 1);
    wcex.lpszClassName = szWindowClass;
    wcex.hIcon = 0;
    wcex.hIconSm = 0;
    wcex.lpszMenuName = 0;
    return RegisterClassExW(&wcex);
}

HWND hCheckButton;
HWND hDownloadButton;
HWND hRestartButton;

BOOL InitInstance(HINSTANCE hInstance, int nCmdShow)
{
    hInst = hInstance; // Store instance handle in our global variable

    HWND hWnd = CreateWindowW(szWindowClass, szTitle, WS_OVERLAPPEDWINDOW,
        CW_USEDEFAULT, 0, 300, 260, nullptr, nullptr, hInstance, nullptr);

    hCheckButton = CreateWindowW(L"BUTTON", L"Check for updates",
        WS_TABSTOP | WS_VISIBLE | WS_CHILD | BS_DEFPUSHBUTTON,
        40, 50, 200, 40,
        hWnd, NULL, (HINSTANCE)GetWindowLongPtr(hWnd, GWLP_HINSTANCE), NULL);

    hDownloadButton = CreateWindowW(L"BUTTON", L"Download update",
        WS_TABSTOP | WS_VISIBLE | WS_CHILD | BS_DEFPUSHBUTTON,
        40, 100, 200, 40,
        hWnd, NULL, (HINSTANCE)GetWindowLongPtr(hWnd, GWLP_HINSTANCE), NULL);

    hRestartButton = CreateWindowW(L"BUTTON", L"Apply / Restart",
        WS_TABSTOP | WS_VISIBLE | WS_CHILD | BS_DEFPUSHBUTTON,
        40, 150, 200, 40,
        hWnd, NULL, (HINSTANCE)GetWindowLongPtr(hWnd, GWLP_HINSTANCE), NULL);

    if (!hWnd) {
        return FALSE;
    }

    ShowWindow(hWnd, nCmdShow);
    UpdateWindow(hWnd);

    return TRUE;
}

LRESULT CALLBACK WndProc(HWND hWnd, UINT message, WPARAM wParam, LPARAM lParam)
{
    switch (message) {
    case WM_COMMAND:
    {
        if (LOWORD(wParam) == BN_CLICKED) {
            if ((HWND)lParam == hCheckButton) {
                try {
                    updInfo = manager->CheckForUpdates();
                    if (updInfo.has_value()) {
                        std::string version = updInfo.value().TargetFullRelease.Version;
                        std::wstring message = L"Update available: " + Utf8ToWString(version);
                        MessageBoxCentered(hWnd, message.c_str(), szTitle, MB_OK);
                    }
                    else {
                        MessageBoxCentered(hWnd, L"No updates available.", szTitle, MB_OK);
                    }
                }
                catch (std::exception& e) {
                    std::wstring what = Utf8ToWString(e.what());
                    MessageBoxCentered(hWnd, what.c_str(), szTitle, MB_OK | MB_ICONERROR);
                }
            }
            else if ((HWND)lParam == hDownloadButton) {
                if (updInfo.has_value()) {
                    try {
                        manager->DownloadUpdates(updInfo.value());
                        downloaded = true;
                        MessageBoxCentered(hWnd, L"Download completed successfully.", szTitle, MB_OK);
                    }
                    catch (std::exception& e) {
                        std::wstring what = Utf8ToWString(e.what());
                        MessageBoxCentered(hWnd, what.c_str(), szTitle, MB_OK | MB_ICONERROR);
                    }
                }
                else {
                    MessageBoxCentered(hWnd, L"Check for updates first.", szTitle, MB_OK);
                }
            }
            else if ((HWND)lParam == hRestartButton) {
                if (!downloaded) {
                    MessageBoxCentered(hWnd, L"Download an update first.", szTitle, MB_OK);
                }
                else {
                    manager->WaitExitThenApplyUpdate(updInfo.value());
                    exit(0);
                }
            }
        }
        break;
    }
    case WM_PAINT:
    {
        PAINTSTRUCT ps;
        HDC hdc = BeginPaint(hWnd, &ps);
        RECT r{ 0, 5, ps.rcPaint.right, ps.rcPaint.bottom };

        std::string currentVersion = manager->GetCurrentVersion();
        auto ver = Utf8ToWString(currentVersion);
        std::wstring text = L"Welcome to v" + ver + L" of the\nVelopack C++ Sample App.";
        DrawText(hdc, text.c_str(), -1, &r, DT_BOTTOM | DT_CENTER);
        EndPaint(hWnd, &ps);
        break;
    }
    case WM_DESTROY:
        PostQuitMessage(0);
        break;
    default:
        return DefWindowProc(hWnd, message, wParam, lParam);
    }
    return 0;
}

int MessageBoxCentered(HWND hWnd, LPCTSTR lpText, LPCTSTR lpCaption, UINT uType)
{
    if (hWnd == nullptr) {
        return MessageBox(hWnd, lpText, lpCaption, uType);
    }
    else {
        // Center message box at its parent window
        static HHOOK hHookCBT{};
        hHookCBT = SetWindowsHookEx(WH_CBT,
            [](int nCode, WPARAM wParam, LPARAM lParam) -> LRESULT
            {
                if (nCode == HCBT_CREATEWND) {
                    if (((LPCBT_CREATEWND)lParam)->lpcs->lpszClass == (LPWSTR)(ATOM)32770)  // #32770 = dialog box class
                    {
                        RECT rcParent{};
                        GetWindowRect(((LPCBT_CREATEWND)lParam)->lpcs->hwndParent, &rcParent);
                        ((LPCBT_CREATEWND)lParam)->lpcs->x = rcParent.left + ((rcParent.right - rcParent.left) - ((LPCBT_CREATEWND)lParam)->lpcs->cx) / 2;
                        ((LPCBT_CREATEWND)lParam)->lpcs->y = rcParent.top + ((rcParent.bottom - rcParent.top) - ((LPCBT_CREATEWND)lParam)->lpcs->cy) / 2;
                    }
                }

                return CallNextHookEx(hHookCBT, nCode, wParam, lParam);
            },
            0, GetCurrentThreadId());

        int iRet{ MessageBox(hWnd, lpText, lpCaption, uType) };

        UnhookWindowsHookEx(hHookCBT);

        return iRet;
    }
}

std::string WStringToUtf8(std::wstring const& wstr)
{
    if (wstr.empty()) return std::string();
    int size_needed = WideCharToMultiByte(CP_UTF8, 0, &wstr[0], (int)wstr.size(), NULL, 0, NULL, NULL);
    std::string strTo(size_needed, 0);
    WideCharToMultiByte(CP_UTF8, 0, &wstr[0], (int)wstr.size(), &strTo[0], size_needed, NULL, NULL);
    return strTo;
}

std::wstring Utf8ToWString(std::string const& str)
{
    if (str.empty()) return std::wstring();
    int size_needed = MultiByteToWideChar(CP_UTF8, 0, &str[0], (int)str.size(), NULL, 0);
    std::wstring strTo(size_needed, 0);
    MultiByteToWideChar(CP_UTF8, 0, &str[0], (int)str.size(), &strTo[0], size_needed);
    return strTo;
}