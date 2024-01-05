| [docs](.) / compiling.md |
|:---|

# Compiling Velopack

Velopack is made up of some Rust binaries which are re-distributed with installed apps, a .NET NuGet package, and a .NET command line tool. In order to test the project, you need to build the Rust binaries before compiling dotnet.   

### Prerequisites
 - [.NET 6 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)
 - [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
 - [Rust / Cargo](https://www.rust-lang.org/tools/install)
 - `dotnet tool install -g dotnet-coverage`
 - `dotnet tool install -g nbgv`

### Debug / Test
On windows, you need to build the Rust binaries using the `windows` feature before running tests. On OSX, you should run `cargo build` instead. 

```shell
git clone https://github.com/velopack/velopack.git
cd velopack/src/Rust
cargo build --features windows
cd ../../
dotnet build
dotnet test --no-build
```

### Release / Build
This is slightly complicated, because you will need to compile Rust on x64 OSX and x64 Windows before creating the final packages.

On OSX:
```shell
git clone https://github.com/velopack/velopack.git
cd velopack/src/Rust
cargo build --release
```

On Windows:
```shell
git clone https://github.com/velopack/velopack.git
cd velopack/src/Rust
cargo build --release --features windows
copy {path_to_osx_update} target/release/updatemac
dotnet build -c Release /p:PackRustAssets=true
```