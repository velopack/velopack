cmake_minimum_required(VERSION 3.9 FATAL_ERROR)
project(cpp-example)
include(CheckIncludeFileCXX)

set(CMAKE_CXX_STANDARD 11)
set(CMAKE_CXX_STANDARD_REQUIRED True)
set(CMAKE_CXX_EXTENSIONS False)

set(CMAKE_MODULE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/cmake" ${CMAKE_MODULE_PATH})

find_package(Boost 1.66.0 REQUIRED)
include_directories(${Boost_INCLUDE_DIRS})
set(CMAKE_REQUIRED_INCLUDES "${Boost_INCLUDE_DIRS}")

check_include_file_cxx(boost/optional.hpp HAVE_BOOST_OPTIONAL_HPP)
if (NOT HAVE_BOOST_OPTIONAL_HPP)
  message(FATAL_ERROR "Can not find boost/optional.hpp")
endif ()

check_include_file_cxx(boost/variant.hpp HAVE_BOOST_VARIANT_HPP)
if (NOT HAVE_BOOST_VARIANT_HPP)
  message(FATAL_ERROR "Can not find boost/variant.hpp")
endif ()

check_include_file_cxx(boost/utility/string_view.hpp HAS_BOOST_STRING_VIEW_HPP)
if (NOT HAS_BOOST_STRING_VIEW_HPP)
  message(FATAL_ERROR "Can not find boost/utility/string_view.hpp")
endif ()

include(rust_part_lib)

add_executable(app main.cpp)
target_link_libraries(app rust_part_lib)
