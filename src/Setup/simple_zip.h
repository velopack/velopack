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
    pugi::xml_document manifest;
    int num_entries;
    void load_manifest();

public:
    bool has_manifest;
    simple_zip(void* zipBuf, size_t cZipBuf);
    ~simple_zip();
    void extract_updater_to_file(std::wstring filePath);

};

#endif