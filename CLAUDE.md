# Claude Development Notes for Velopack

This file contains important technical information about the Velopack codebase that may be useful for future development tasks.

## What is Velopack

Velopack is an application deployment and auto-update framework. It creates installer packages and handles automatic updates for desktop applications across Windows, macOS, and Linux.

## The "Pack" Process

The pack process creates releases and updates for applications:

1. **Input**: Application files and metadata (version, architecture, etc.)
2. **Output**: 
   - `.nupkg` file: Contains the application update package
   - `Setup.exe` (Windows): Installer that contains embedded binaries and the update package
   - `releases.json`: Metadata about available releases

## Update Package Structure (.nupkg)

Update packages are ZIP files with a specific structure:

```
MyApp-1.0.0-full.nupkg
├── MyApp.nuspec                 # Package metadata (version, architecture, etc.)
├── [Content_Types].xml          # ZIP metadata
├── _rels/                       # ZIP metadata
└── lib/
    └── app/
        ├── Squirrel.exe         # Update binary (handles app updates)
        ├── myapp.exe           # Main application executable
        ├── myapp_ExecutionStub.exe # Stub executable
        ├── sq.version          # Version information
        └── [other app files]
```

### Key Discoveries

- **Squirrel.exe IS the Update.exe binary**: The update binary is packaged as `Squirrel.exe` in `lib/app/`, not as a separate vendor file
- **Architecture-specific updates**: Each package contains the appropriate architecture version of Squirrel.exe for the target platform
- **Setup.exe vs .nupkg**: The installer (Setup.exe) contains embedded binaries, while the update package (.nupkg) contains the application files and update binary

## Binary Locations and Architecture

### Platform Binary Naming
- **Windows**: `UpdateWin_x86.exe`, `UpdateWin_x64.exe`, `UpdateWin_arm64.exe`
- **Linux**: `UpdateNix_x64`, `UpdateNix_arm64`  
- **macOS**: `UpdateMac` (universal binary)

### Build Locations
- **Debug builds**: `target/debug/` with generic names (`update.exe`, `setup.exe`, `stub.exe`)
- **Release builds**: `target/release/` with architecture-specific names

### Debug vs Release Mode
- **HelperFile.cs** contains `#if DEBUG` conditionals to handle different binary naming between debug and release modes
- Debug mode uses generic binary names for local development
- Release mode uses architecture-specific names for production builds

## Testing Package Contents

To verify package contents in tests:
1. Extract the `.nupkg` file (it's a ZIP archive)
2. Check `lib/app/Squirrel.exe` for the update binary
3. Use AsmResolver (`PEFile.FromFile()` and `peFile.FileHeader.Machine`) to verify binary architecture
4. Parse the `.nuspec` file for metadata verification

## Build Instructions

### Rust Binaries
Build the core Rust binaries (update.exe, setup.exe, stub.exe):

```bash
# Build for Windows (debug)
cargo build --features windows

# Build for Windows (release)
cargo build --features windows --release

# Build for specific Windows architecture (release)
cargo build --features windows --release --target x86_64-pc-windows-msvc
cargo build --features windows --release --target i686-pc-windows-msvc
cargo build --features windows --release --target aarch64-pc-windows-msvc
```

### .NET Build
Build the packaging and tooling:

```bash
# Build in debug mode
dotnet build

# Build in release mode  
dotnet build -c Release

# Run specific test with detailed output
dotnet test --filter "TestName" --logger "console;verbosity=normal" path/to/test.csproj
```

---

## Instructions for Future Development

**⚠️ Important for AI Agents**: When working on this codebase, please update this CLAUDE.md file as you discover new generic technical knowledge that would benefit future development tasks. 

### What to Add:
- ✅ **General architecture and design patterns**
- ✅ **Build processes and commands** 
- ✅ **File structures and important discoveries**
- ✅ **Testing approaches and utilities**
- ✅ **Cross-platform differences**

### What NOT to Add:
- ❌ **Task-specific implementation details**
- ❌ **Temporary status updates**
- ❌ **Personal development notes**

### How to Update:
1. Add new sections or expand existing ones with discovered knowledge
2. Keep information generic and applicable to various future tasks
3. Focus on technical insights that aren't immediately obvious from the code
4. Use clear examples and structure for easy reference

This file should serve as a growing knowledge base for understanding Velopack's architecture and development processes.