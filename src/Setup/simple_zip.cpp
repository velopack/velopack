#include "simple_zip.h"

using namespace std;

// https://stackoverflow.com/a/874160/184746
bool hasEnding(std::string const& fullString, std::string const& ending)
{
    if (fullString.length() >= ending.length()) {
        return (0 == fullString.compare(fullString.length() - ending.length(), ending.length(), ending));
    }
    return false;
}

const mz_zip_archive_file_stat* simple_zip::find_zip_entry(std::string endsWith)
{
    for (mz_zip_archive_file_stat& e : entries) {
        if (hasEnding(e.m_filename, endsWith)) {
            return &e;
        }
    }
    return nullptr;
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
    auto num_entries = (int)mz_zip_reader_get_num_files(&zip_archive);
    int64_t c = 0;
    mz_zip_archive_file_stat file_stat;
    for (int i = 0; i < num_entries; i++) {
        if (!mz_zip_reader_file_stat(&zip_archive, i, &file_stat)) {
            // unable to read this file
            continue;
        }

        if (file_stat.m_is_directory) {
            // ignore directories
            continue;
        }

        c += file_stat.m_uncomp_size;
        entries.push_back(file_stat);
    }

    uncompressed_size = c;

    auto manifest_entry = find_zip_entry(".nuspec");
    if (!manifest_entry) {
        return;
    }

    auto bufferSize = (size_t)manifest_entry->m_uncomp_size;
    void* buffer = malloc(bufferSize);
    if (buffer && mz_zip_reader_extract_to_mem(&zip_archive, manifest_entry->m_file_index, buffer, bufferSize, 0)) {
        if (manifest.load_buffer(buffer, bufferSize).status == pugi::xml_parse_status::status_ok) {
            has_manifest = true;
        }
    }

    if (buffer) free(buffer);
}

simple_zip::simple_zip(void* zipBuf, size_t cZipBuf)
{
    init_file = 0;
    uncompressed_size = 0;
    has_manifest = false;
    memset(&zip_archive, 0, sizeof(zip_archive));

    if (!mz_zip_reader_init_mem(&zip_archive, zipBuf, cZipBuf, 0))
        throwLastMzError(&zip_archive, L"Unable to read archive from memory handle.");

    compressed_size = zip_archive.m_archive_size;
    load_manifest();
}

simple_zip::simple_zip(std::wstring filePath)
{
    uncompressed_size = 0;
    has_manifest = false;
    memset(&zip_archive, 0, sizeof(zip_archive));

    FILE* pFile = NULL;
    _wfopen_s(&pFile, filePath.c_str(), L"rb");
    if (!pFile)
        throw wstring(L"Unable to file for reading.");
    init_file = pFile;

    if (!mz_zip_reader_init_cfile(&zip_archive, pFile, 0, 0))
        throwLastMzError(&zip_archive, L"Unable to read archive from file handle.");

    compressed_size = zip_archive.m_archive_size;
    load_manifest();
}

simple_zip::~simple_zip()
{
    mz_zip_reader_end(&zip_archive);
    if (init_file) fclose(init_file);
}

void simple_zip::extract_updater_to_file(std::wstring filePath)
{
    const mz_zip_archive_file_stat* updater_entry = find_zip_entry("Squirrel.exe");
    if (!updater_entry) {
        return;
    }

    FILE* pFile = NULL;

    _wfopen_s(&pFile, filePath.c_str(), L"wb");
    if (!pFile)
        throw wstring(L"Unable to open temp file for writing.");

    auto mzResult = mz_zip_reader_extract_to_cfile(&zip_archive, updater_entry->m_file_index, pFile, 0);

    fclose(pFile);

    if (!mzResult) {
        throwLastMzError(&zip_archive, L"Unable to extract updater from archive.");
    }
}

std::wstring simple_zip::get_machine_architecture()
{
    if (has_manifest) {
        auto select = manifest.select_node(L"//machineArchitecture/text()");
        if (select != nullptr) {
            auto node = select.node();
            if (node != nullptr) {
                return node.value();
            }
        }
    }
    return L"";
}

std::wstring simple_zip::get_minimum_windows_version()
{
    if (has_manifest) {
        auto select = manifest.select_node(L"//minimumWindowsVersion/text()");
        if (select != nullptr) {
            auto node = select.node();
            if (node != nullptr) {
                return node.value();
            }
        }
    }
    return L"";
}
