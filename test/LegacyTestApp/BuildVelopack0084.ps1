

dotnet build -c Release -p:UseVelopack=0.0.84

dotnet tool install vpk --version 0.0.84 --tool-path bin/vpk

./bin/vpk/vpk.exe pack -u LegacyTestApp -v 1.0.0 -p bin/Release/net48