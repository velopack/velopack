set(CARGO_CMD "cargo")
set(RUST_TARGET "")
set(RUSTC "rustc")
get_filename_component(ROOT_DIR ${CMAKE_CURRENT_SOURCE_DIR} DIRECTORY)
set(RUST_BUILD_CWD "${ROOT_DIR}/rust-part")

function (find_cargo_target_directory CACHEVAR)
  if (DEFINED ${CACHEVAR})
    message(STATUS "Cashed target path: ${${CACHEVAR}}")
    return ()
  endif ()
  if(DEFINED ENV{CARGO_TARGET_DIR})
    message(STATUS "Found CARGO_TARGET_DIR: $ENV{CARGO_TARGET_DIR}")
    set(${CACHEVAR} "$ENV{CARGO_TARGET_DIR}" CACHE PATH "Path to cargo's target directory with build artifacts" FORCE)
    return ()
  endif ()
  set(CUR_DIR "${RUST_BUILD_CWD}/src")
  while (True)
    get_filename_component(NEW_DIR ${CUR_DIR}  DIRECTORY)
    set(TARGET_PATH "${NEW_DIR}/target")
    if (EXISTS "${TARGET_PATH}" AND IS_DIRECTORY "${TARGET_PATH}")
      message(STATUS "Found cargo's target directory: ${TARGET_PATH}")
      set(${CACHEVAR} "${TARGET_PATH}" CACHE PATH "Path to cargo's target directory with build artifacts" FORCE)
      break()
    endif ()

    if ("${NEW_DIR}" STREQUAL "${CUR_DIR}")
      message(FATAL_ERROR "Can not find cargo's target directory")
    endif ()

    set(CUR_DIR "${NEW_DIR}")
  endwhile()
endfunction(find_cargo_target_directory)

set(RUST_SWIG_SRCS "${RUST_BUILD_CWD}/src/cpp_glue.rs.in")
configure_file(cmake/flapigen_gen.cmake.in ${CMAKE_BINARY_DIR}/flapigen_gen.cmake @ONLY)
file(MAKE_DIRECTORY ${CMAKE_BINARY_DIR}/flapigen_gen)
if (NOT EXISTS ${CMAKE_BINARY_DIR}/flapigen_regen_headers.cmake)
  file(WRITE ${CMAKE_BINARY_DIR}/flapigen_regen_headers.cmake "")
endif()

execute_process(COMMAND ${CMAKE_COMMAND}
  -DSRCDIR=${CMAKE_SOURCE_DIR}
  -DRUST_BUILD_CWD=${RUST_BUILD_CWD}
    -DBINDIR=${CMAKE_BINARY_DIR}
    -P ${CMAKE_BINARY_DIR}/flapigen_gen.cmake
    WORKING_DIRECTORY ${CMAKE_BINARY_DIR}/flapigen_gen
    RESULT_VARIABLE retcode)
if(NOT "${retcode}" STREQUAL "0")
  message(FATAL_ERROR "run of flapigen_gen.cmake failed")
endif()

add_custom_target(flapigen_gen_headers ${CMAKE_COMMAND}
    -DSRCDIR=${CMAKE_SOURCE_DIR}
    -DBINDIR=${CMAKE_BINARY_DIR}
    -DRUST_BUILD_CWD=${RUST_BUILD_CWD}
    -P ${CMAKE_BINARY_DIR}/flapigen_gen.cmake
    WORKING_DIRECTORY ${CMAKE_BINARY_DIR}/flapigen_gen)
include(${CMAKE_BINARY_DIR}/flapigen_regen_headers.cmake)

find_cargo_target_directory(TARGET_PATH)

if (NOT RUST_DEBUG_BUILD)
  set(CARGO_BUILD ${CARGO_CMD} "build" "${RUST_TARGET}" "--release")
else()
  set(CARGO_BUILD ${CARGO_CMD} "build" "${RUST_TARGET}")
endif()

if (WIN32)
  set(RUST_PART_LIB_NAME "cpp_example_rust_part.dll.lib")
elseif (${CMAKE_SYSTEM_NAME} MATCHES "Darwin")
  set(RUST_PART_LIB_NAME "libcpp_example_rust_part.dylib")
else ()
  set(RUST_PART_LIB_NAME "libcpp_example_rust_part.so")
endif ()

set(CARGO_BUILD_DIR "${TARGET_PATH}")
if (DEFINED ENV{CARGO_BUILD_TARGET})
  set(CARGO_BUILD_DIR "${CARGO_BUILD_DIR}/$ENV{CARGO_BUILD_TARGET}")
endif ()

if (NOT RUST_DEBUG_BUILD)
  set(RUST_PART_LIB_PATH "${CARGO_BUILD_DIR}/release/${RUST_PART_LIB_NAME}")
else()
  set(RUST_PART_LIB_PATH "${CARGO_BUILD_DIR}/debug/${RUST_PART_LIB_NAME}")
endif()

add_custom_command(OUTPUT ${RUST_PART_LIB_PATH}
  COMMAND ${CARGO_BUILD}
  WORKING_DIRECTORY "${RUST_BUILD_CWD}")
add_custom_target(rust_part_lib_target DEPENDS ${RUST_PART_LIB_PATH})
if (MSVC)
  add_library(rust_part_lib UNKNOWN IMPORTED GLOBAL)
else ()
  add_library(rust_part_lib SHARED IMPORTED GLOBAL)
endif ()
add_dependencies(rust_part_lib rust_part_lib_target)
add_dependencies(rust_part_lib flapigen_gen_headers)
set_target_properties(rust_part_lib
    PROPERTIES
    IMPORTED_LOCATION ${RUST_PART_LIB_PATH}
# becase of https://github.com/rust-lang/cargo/issues/5045
    IMPORTED_NO_SONAME True
    INTERFACE_INCLUDE_DIRECTORIES "${CMAKE_CURRENT_SOURCE_DIR}/rust-api")
