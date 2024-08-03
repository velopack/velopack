cmake_minimum_required(VERSION 3.9 FATAL_ERROR)
project(velopack_cpp)
include(CheckIncludeFileCXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED True)

include_directories(${CMAKE_SOURCE_DIR}/api)

if(WIN32)
    set(RUST_STATIC_LIB "velopack_cpp_bridge.lib")
    link_libraries(ntdll)
    foreach(flag_var
            CMAKE_CXX_FLAGS CMAKE_CXX_FLAGS_DEBUG CMAKE_CXX_FLAGS_RELEASE
            CMAKE_CXX_FLAGS_MINSIZEREL CMAKE_CXX_FLAGS_RELWITHDEBINFO)
        if(${flag_var} MATCHES "/MD")
            string(REGEX REPLACE "/MD" "/MT" ${flag_var} "${${flag_var}}")
        endif()
    endforeach()
elseif(UNIX AND NOT APPLE)
    set(RUST_STATIC_LIB "velopack_cpp_bridge.a")
    set(CMAKE_EXE_LINKER_FLAGS "${CMAKE_EXE_LINKER_FLAGS} -static-libgcc -static-libstdc++")
elseif(APPLE)
    set(RUST_STATIC_LIB "velopack_cpp_bridge.a")
    set(CMAKE_EXE_LINKER_FLAGS "${CMAKE_EXE_LINKER_FLAGS} -static-libstdc++ -static-libgcc")
endif()

# Full path for the library
set(RUST_STATIC_LIB_PATH "${CMAKE_SOURCE_DIR}/../../target/release/${RUST_STATIC_LIB}")

# Custom command to build the library
add_custom_command(OUTPUT ${RUST_STATIC_LIB_PATH}
                   COMMAND cargo build --release
                   WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}/generator
                   COMMENT "Running pre-build command")

# Custom target to handle the build of the library
add_custom_target(build_rust_lib DEPENDS ${RUST_STATIC_LIB_PATH})

# Main executable
add_executable(${PROJECT_NAME} main.cpp)

# Ensure the library is built before the main project
add_dependencies(${PROJECT_NAME} build_rust_lib)

# Link the library after it has been built
target_link_libraries(${PROJECT_NAME} ${RUST_STATIC_LIB_PATH})
