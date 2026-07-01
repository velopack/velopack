cmake_minimum_required(VERSION 3.15 FATAL_ERROR)

project(velopack_deployment_test_harness LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

include(FetchContent)

# The C# HarnessRunner overrides these cache variables; the defaults resolve relative to this
# harness directory (harnesses/cpp -> repo root is four levels up).
set(VELOPACK_INCLUDE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../../../src/lib-cpp/include"
    CACHE PATH "Directory containing Velopack.hpp / Velopack.h")
set(VELOPACK_LIBC_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../../../target/debug"
    CACHE PATH "Directory containing the built velopack_libc dynamic library and import lib")

# The velopack_libc crate produces both a dynamic library and (on Windows) an import lib.
if(WIN32)
    set(VELOPACK_LINK_LIB "${VELOPACK_LIBC_DIR}/velopack_libc.dll.lib")
    set(VELOPACK_SHARED_LIB "${VELOPACK_LIBC_DIR}/velopack_libc.dll")
elseif(APPLE)
    set(VELOPACK_LINK_LIB "${VELOPACK_LIBC_DIR}/libvelopack_libc.dylib")
    set(VELOPACK_SHARED_LIB "${VELOPACK_LIBC_DIR}/libvelopack_libc.dylib")
else()
    set(VELOPACK_LINK_LIB "${VELOPACK_LIBC_DIR}/libvelopack_libc.so")
    set(VELOPACK_SHARED_LIB "${VELOPACK_LIBC_DIR}/libvelopack_libc.so")
endif()

FetchContent_Declare(
    nlohmann_json
    GIT_REPOSITORY https://github.com/nlohmann/json.git
    GIT_TAG v3.11.3
    GIT_SHALLOW ON
)
FetchContent_MakeAvailable(nlohmann_json)

add_executable(harness main.cpp)

target_include_directories(harness PRIVATE "${VELOPACK_INCLUDE_DIR}")
target_link_libraries(harness PRIVATE nlohmann_json::nlohmann_json "${VELOPACK_LINK_LIB}")

# Ensure the dynamic library sits next to the harness executable so it can be located at runtime.
add_custom_command(TARGET harness POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
    "${VELOPACK_SHARED_LIB}"
    "$<TARGET_FILE_DIR:harness>"
)
