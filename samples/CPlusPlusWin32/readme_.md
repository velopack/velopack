# VelopackCppWin32Sample
_Prerequisites: Rust/Cargo, Msbuild_

This app is purely a proof of concept at this time, `velopack.hpp` currently only works on windows and needs testing / fixes for other operating systems, and probably also needs fixing for unicode/strings. This sample is made up of a simple Win32 desktop app, generated via a Visual Studio template, and it includes `velopack.hpp` and [`subprocess.h`](https://github.com/sheredom/subprocess.h). 

Also, this is using command line features not currently exposed in the main Velopack releases, so you can only build releases from source by using the `dev-build.bat [version]` script. Trying to build/use this sample with the mainstream `vpk` tool is not guarenteed to work at this time.

If you are interested in using Velopack for your C++ project and are willing to help with the design of `velopack.hpp` (either with suggestions, or code), please drop in our Discord and I would be happy to discuss / work with you to polish this up.