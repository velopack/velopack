#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <VersionHelpers.h>
#include <string>
#include <fstream>
#include <filesystem>
#include "bundle_marker.h"
#include "simple_zip.h"
#include "platform_util.h"

using namespace std;

int WINAPI wWinMain(_In_ HINSTANCE hInstance, _In_opt_ HINSTANCE hPrevInstance, _In_ PWSTR pCmdLine, _In_ int nCmdShow)
{
    if (!IsWindows7SP1OrGreater()) {
        util::show_error_dialog(L"This installer requires Windows 7 SP1 or later and cannot run.");
        return 0;
    }

    wstring myPath = util::get_current_process_path();
    wstring updaterPath = util::get_temp_file_path(L"exe");
    uint8_t* memMap = 0;
    simple_zip* zip = 0;

    try {
        // locate and load nupkg (embedded or from file)
        int64_t packageOffset, packageLength;
        bundle_marker_t::header_offset(&packageOffset, &packageLength);
        if (packageOffset > 0 && packageLength > 0) {
            memMap = util::mmap_read(util::get_current_process_path(), 0);
            if (!memMap) {
                throw wstring(L"Unable to memmap current executable. Is there enough available system memory?");
            }
            uint8_t* pkgStart = memMap + packageOffset;
            zip = new simple_zip(pkgStart, (size_t)packageLength);
        }

#if _DEBUG
        // in debug builds, allow the nupkg to be passed in as the only command line argument
        wstring arguments(pCmdLine);
        if (!zip && !arguments.empty() && std::filesystem::exists(arguments)) {
            zip = new simple_zip(arguments);
            packageOffset = 0;
            myPath = arguments;
        }
#endif

        if (!zip) {
            throw wstring(L"The embedded package containing the application to install was not found. Please contact the application author.");
        }

        // do we have enough disk space?
        int64_t requiredSpace = (50 * 1000 * 1000) + (zip->compressed_size * 2) + zip->uncompressed_size; // archive + squirrel overhead
        if (!util::check_diskspace(requiredSpace)) {
            throw wstring(L"Insufficient disk space. This application requires at least " + util::pretty_bytes(requiredSpace) + L" free space to be installed.");
        }

        // run installer and forward our command line arguments
        zip->extract_updater_to_file(updaterPath);
        wstring cmd = L"\"" + updaterPath + L"\" --setup \"" + myPath + L"\" --setupOffset " + to_wstring(packageOffset) + L" " + pCmdLine;
        util::wexec(cmd.c_str());
    }
    catch (wstring wsx) {
        util::show_error_dialog(L"An error occurred while running setup. " + wsx);
    }
    catch (...) {
        util::show_error_dialog(L"An unknown error occurred while running setup. Please contact the application author.");
    }

    // clean-up resources
    DeleteFile(updaterPath.c_str());
    if (zip) delete zip;
    if (memMap) util::munmap(memMap);
    return 0;
}