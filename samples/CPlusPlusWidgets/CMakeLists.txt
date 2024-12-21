cmake_minimum_required(VERSION 3.14 FATAL_ERROR)

project(VelopackCppWidgetsSample LANGUAGES CXX)

include(FetchContent)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

set(wxBUILD_SHARED OFF)

if(IS_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/wxWidgets")
    set(wxWidgets_ROOT_DIR "${CMAKE_CURRENT_SOURCE_DIR}/wxWidgets")
    set(wxWidgets_LIB_DIR "${CMAKE_CURRENT_SOURCE_DIR}/wxWidgets/lib/vc14x_x64_dll")
    set(wxWidgets_INCLUDE_DIRS "${CMAKE_CURRENT_SOURCE_DIR}/wxWidgets/include")
endif()

find_package(wxWidgets COMPONENTS net core base)
if(wxWidgets_USE_FILE) # not defined in CONFIG mode
    include(${wxWidgets_USE_FILE})
endif()

if(NOT wxWidgets_FOUND)
    message(STATUS "wxWidgets not found. Fetching wxWidgets from git...")
    FetchContent_Declare(
        wxWidgets
        GIT_REPOSITORY https://github.com/wxWidgets/wxWidgets.git
        GIT_SHALLOW ON
    )
    FetchContent_MakeAvailable(wxWidgets)
    set(wxWidgets_LIBRARIES wxcore wxnet)
endif()

# include the Velopack library (headers + precompiled lib)
include_directories("${CMAKE_CURRENT_SOURCE_DIR}/../../src/lib-cpp/include")

set(VELOPACK_CONFIG "debug")

if(WIN32)
    set(VELOPACK_LIB_PATH "${CMAKE_CURRENT_SOURCE_DIR}/../../target/${VELOPACK_CONFIG}/velopack_libc.dll.lib")
    set(VELOPACK_DLL_PATH "${CMAKE_CURRENT_SOURCE_DIR}/../../target/${VELOPACK_CONFIG}/velopack_libc.dll")
elseif(APPLE)
    set(VELOPACK_LIB_PATH "${CMAKE_CURRENT_SOURCE_DIR}/../../target/${VELOPACK_CONFIG}/libvelopack_libc.dylib")
    set(VELOPACK_DLL_PATH "${CMAKE_CURRENT_SOURCE_DIR}/../../target/${VELOPACK_CONFIG}/libvelopack_libc.dylib")
else()
    set(VELOPACK_LIB_PATH "${CMAKE_CURRENT_SOURCE_DIR}/../../target/${VELOPACK_CONFIG}/libvelopack_libc.so")
    set(VELOPACK_DLL_PATH "${CMAKE_CURRENT_SOURCE_DIR}/../../target/${VELOPACK_CONFIG}/libvelopack_libc.so")
endif()

if(WIN32)
    # the WIN32 is needed for Windows in order for it to look for WinMain
    # instead of the main function. This is ignored on other systems,
    # so it works on all platforms
    add_executable(main WIN32 main.cpp main.exe.manifest)
else()
    set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/Release")
    add_executable(main main.cpp)
endif()

target_compile_definitions(main PRIVATE RELEASES_DIR="${CMAKE_CURRENT_SOURCE_DIR}/releases")
target_link_libraries(main PRIVATE ${wxWidgets_LIBRARIES} "${VELOPACK_LIB_PATH}")

# copy velopack dll to output directory
add_custom_command(TARGET main POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
    "${VELOPACK_DLL_PATH}"
    "$<TARGET_FILE_DIR:main>"
)