cmake_minimum_required(VERSION 3.21)

project(VelopackQtSample VERSION 0.1 LANGUAGES CXX)

set(CMAKE_AUTOMOC ON)
set(CMAKE_CXX_STANDARD_REQUIRED ON)


# Disable C and C++ compiler extensions.
# C/CXX_EXTENSIONS are ON by default to allow the compilers to use extended
# variants of the C/CXX language.
# However, this could expose cross-platform bugs in user code or in the headers
# of third-party dependencies and thus it is strongly suggested to turn
# extensions off.
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_C_EXTENSIONS OFF)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

find_package(Qt6 6.2 REQUIRED COMPONENTS Quick)


qt_add_executable(appVelopackQtSample
    main.cpp
    constants.h
    Velopack.hpp
    Velopack.cpp
    autoupdater.h
    autoupdater.cpp
)

qt_add_qml_module(appVelopackQtSample
    URI VelopackQtSample
    VERSION 1.0
    QML_FILES Main.qml
)

# Qt for iOS sets MACOSX_BUNDLE_GUI_IDENTIFIER automatically since Qt 6.1.
# If you are developing for iOS or macOS you should consider setting an
# explicit, fixed bundle identifier manually though.
set_target_properties(appVelopackQtSample PROPERTIES
#    MACOSX_BUNDLE_GUI_IDENTIFIER com.example.appVelopackQtSample
    MACOSX_BUNDLE_BUNDLE_VERSION ${PROJECT_VERSION}
    MACOSX_BUNDLE_SHORT_VERSION_STRING ${PROJECT_VERSION_MAJOR}.${PROJECT_VERSION_MINOR}
    MACOSX_BUNDLE TRUE
    WIN32_EXECUTABLE TRUE
)

target_link_libraries(appVelopackQtSample
    PRIVATE Qt6::Quick
)

include(GNUInstallDirs)
install(TARGETS appVelopackQtSample
    BUNDLE DESTINATION .
    LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR}
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
)

if (APPLE)
    # Copy macOS updater/installer binary that needs to be next to our binary
    install(FILES ${PROJECT_SOURCE_DIR}/platforms/macos/VfusionMac DESTINATION ${CMAKE_INSTALL_BINDIR})
elseif(WIN32)
    # Copy Windows updater/installer binary that needs to be next to our binary
    install(FILES ${PROJECT_SOURCE_DIR}/platforms/windows/Vfusion.exe DESTINATION ${CMAKE_INSTALL_BINDIR})
endif()

qt_generate_deploy_qml_app_script(
    TARGET appVelopackQtSample
    OUTPUT_SCRIPT deploy_script
)

install(SCRIPT ${deploy_script})
