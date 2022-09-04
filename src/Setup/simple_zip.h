#pragma once

#ifndef HEADER_SIMPLEZIP_H
#define HEADER_SIMPLEZIP_H

#include "pugixml.hpp"
#include "miniz.h"
#include <vector>
#include <string>

class simple_zip
{

private:
    mz_zip_archive zip_archive;
    std::vector<mz_zip_archive_file_stat> entries;
    pugi::xml_document manifest;
    void load_manifest();
    const mz_zip_archive_file_stat* find_zip_entry(std::string endsWith);
    FILE* init_file;

public:
    bool has_manifest;
    int64_t uncompressed_size;
    int64_t compressed_size;
    simple_zip(void* zipBuf, size_t cZipBuf);
    simple_zip(std::wstring filePath);
    ~simple_zip();
    void extract_updater_to_file(std::wstring filePath);

};

#endif