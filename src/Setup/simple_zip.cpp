#include "simple_zip.h"
#include <functional>

using namespace std;

// https://stackoverflow.com/a/874160/184746
bool hasEnding(std::string const& fullString, std::string const& ending)
{
    if (fullString.length() >= ending.length()) {
        return (0 == fullString.compare(fullString.length() - ending.length(), ending.length(), ending));
    }
    return false;
}

bool find_zip_entry(mz_zip_archive* zip_archive, int numFiles, mz_zip_archive_file_stat* file_stat, std::function<bool(mz_zip_archive_file_stat*)>& predicate)
{
    bool foundItem = false;

    for (int i = 0; i < numFiles; i++) {
        if (!mz_zip_reader_file_stat(zip_archive, i, file_stat)) {
            // unable to read this file
            continue;
        }

        if (file_stat->m_is_directory) {
            // ignore directories
            continue;
        }

        if (predicate(file_stat)) {
            foundItem = true;
            break;
        }
    }

    return foundItem;
}

bool find_zip_entry(mz_zip_archive* zip_archive, int numFiles, mz_zip_archive_file_stat* file_stat, string entryEndsWith)
{
    std::function<bool(mz_zip_archive_file_stat*)> endsWithSquirrel([&](mz_zip_archive_file_stat* z) {
        return hasEnding(z->m_filename, entryEndsWith);
    });
    return find_zip_entry(zip_archive, numFiles, file_stat, endsWithSquirrel);
}

void throwLastMzError(mz_zip_archive* archive, wstring message)
{
    int errCode = mz_zip_get_last_error(archive);
    if (errCode == MZ_ZIP_NO_ERROR)
        return;

    throw wstring(L"MZ Error Code: " + to_wstring(errCode) + L". " + message);
}

void simple_zip::load_manifest()
{
    mz_zip_archive_file_stat manifest_entry;
    if (!find_zip_entry(&zip_archive, num_entries, &manifest_entry, ".nuspec")) {
        return;
    }

    auto bufferSize = (size_t)manifest_entry.m_uncomp_size;
    void* buffer = pugi::allocation_function(bufferSize);

    if (!mz_zip_reader_extract_to_mem(&zip_archive, manifest_entry.m_file_index, buffer, bufferSize, 0)) {
        pugi::deallocation_function(buffer);
        return;
    }

    pugi::xml_parse_result result = manifest.load_buffer_inplace_own(buffer, bufferSize);
    if (!result) {
        pugi::deallocation_function(buffer);
        return;
    }

    has_manifest = true;
}

simple_zip::simple_zip(void* zipBuf, size_t cZipBuf)
{
    has_manifest = false;
    memset(&zip_archive, 0, sizeof(zip_archive));

    if (!mz_zip_reader_init_mem(&zip_archive, zipBuf, cZipBuf, 0))
        throwLastMzError(&zip_archive, L"Unable to open archive.");

    num_entries = (int)mz_zip_reader_get_num_files(&zip_archive);
    load_manifest();
}

simple_zip::~simple_zip()
{
    mz_zip_reader_end(&zip_archive);
}

void simple_zip::extract_updater_to_file(std::wstring filePath)
{
    mz_zip_archive_file_stat updater_entry;
    if (!find_zip_entry(&zip_archive, num_entries, &updater_entry, "Squirrel.exe")) {
        return;
    }

    FILE* pFile = NULL;

    _wfopen_s(&pFile, filePath.c_str(), L"wb");
    if (!pFile)
        throw wstring(L"Unable to open temp file for writing.");

    auto mzResult = mz_zip_reader_extract_to_cfile(&zip_archive, updater_entry.m_file_index, pFile, 0);

    fclose(pFile);

    if (!mzResult) {
        throwLastMzError(&zip_archive, L"Unable to extract updater from archive.");
    }
}