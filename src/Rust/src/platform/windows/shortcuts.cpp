#include "windows.h"
#include "winnls.h"
#include "shobjidl.h"
#include "objbase.h"
#include "objidl.h"
#include "shlguid.h"
#include "strsafe.h"

// all ripped from the following link and then modified
// https://learn.microsoft.com/en-us/windows/win32/shell/links

extern "C" HRESULT CreateLink(LPCWSTR lpszPathObj, LPCWSTR lpszPathLink, LPCWSTR lpszWorkDir)
{
    HRESULT hres;
    IShellLink* psl;
    hres = CoCreateInstance(CLSID_ShellLink, NULL, CLSCTX_INPROC_SERVER, IID_IShellLink, (LPVOID*)&psl);
    if (SUCCEEDED(hres)) {
        IPersistFile* ppf;
        psl->SetPath(lpszPathObj);
        psl->SetWorkingDirectory(lpszWorkDir);
        hres = psl->QueryInterface(IID_IPersistFile, (LPVOID*)&ppf);
        if (SUCCEEDED(hres)) {
            hres = ppf->Save(lpszPathLink, TRUE);
            ppf->Release();
        }
        psl->Release();
    }
    return hres;
}

extern "C" HRESULT ResolveLink(LPWSTR lpszLinkFile, LPWSTR lpszPath, int iPathBufferSize, LPWSTR lpszWorkDir, int iWorkDirBufferSize)
{
    HRESULT hres;
    IShellLink* psl;
    WCHAR szGotPath[MAX_PATH];
    WCHAR szWorkDir[MAX_PATH];
    WIN32_FIND_DATA wfd;
    *lpszPath = 0; // Assume failure 
    hres = CoCreateInstance(CLSID_ShellLink, NULL, CLSCTX_INPROC_SERVER, IID_IShellLink, (LPVOID*)&psl);
    if (SUCCEEDED(hres)) {
        IPersistFile* ppf;
        hres = psl->QueryInterface(IID_IPersistFile, (void**)&ppf);
        if (SUCCEEDED(hres)) {
            hres = ppf->Load(lpszLinkFile, STGM_READ);
            if (SUCCEEDED(hres)) {
                hres = psl->Resolve(0, 0x2 | 0x1 | (1 << 16));
                if (SUCCEEDED(hres)) {
                    hres = psl->GetPath(szGotPath, MAX_PATH, (WIN32_FIND_DATA*)&wfd, SLGP_UNCPRIORITY);
                    if (SUCCEEDED(hres)) {
                        hres = psl->GetWorkingDirectory(szWorkDir, MAX_PATH);
                        if (SUCCEEDED(hres)) {
                            hres = StringCbCopy(lpszPath, iPathBufferSize, szGotPath);
                            if (SUCCEEDED(hres)) {
                                hres = StringCbCopy(lpszWorkDir, iWorkDirBufferSize, szWorkDir);
                            }
                        }
                    }
                }
            }
            ppf->Release();
        }
        psl->Release();
    }
    return hres;
}