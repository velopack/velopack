cmake_minimum_required(VERSION 3.14 FATAL_ERROR)

project(VelopackCppWidgetsSample LANGUAGES CXX)

include(FetchContent)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

set(wxBUILD_SHARED OFF)

message(STATUS "Fetching wxWidgets...")

FetchContent_Declare(
   wxWidgets
   GIT_REPOSITORY https://github.com/wxWidgets/wxWidgets.git
   GIT_SHALLOW ON
)
FetchContent_MakeAvailable(wxWidgets)

# include the Velopack library (headers + precompiled lib)
include_directories("${CMAKE_CURRENT_SOURCE_DIR}/../../src/lib-cpp/include")
set(VELOPACK_LIB_PATH "${CMAKE_CURRENT_SOURCE_DIR}/../../target/debug/velopack_libc.dll.lib")
set(VELOPACK_DLL_PATH "${CMAKE_CURRENT_SOURCE_DIR}/../../target/debug/velopack_libc.dll")

# add sources and executable
set(SRCS main.cpp)
if(WIN32)
    # the WIN32 is needed for Windows in order for it to look for WinMain
    # instead of the main function. This is ignored on other systems,
    # so it works on all platforms
    add_executable(main WIN32 ${SRCS} main.exe.manifest)
else()
    add_executable(main ${SRCS})
endif()

target_compile_definitions(main PRIVATE RELEASES_DIR="${CMAKE_CURRENT_SOURCE_DIR}/releases")
target_link_libraries(main PRIVATE wxcore wxnet "${VELOPACK_LIB_PATH}")

# copy velopack dll to output directory
add_custom_command(TARGET main POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
    "${VELOPACK_DLL_PATH}"
    "$<TARGET_FILE_DIR:main>"
)