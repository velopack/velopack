# Directory Layout

## Windows

### Shared

- %LOCALAPPDATA%
  - Velopack
    - [AppId]
      - Update.exe
      - packages
      - velopack.log


### MSI - All Users

- %PROGRAMFILES%
  - [AppId]
    - .portable
    - Update.exe
    - [AppId].exe (stub)
    - current
      - sq.version

### MSI - Current User

### Setup.exe