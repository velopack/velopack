

dotnet build -c Release -p:UseClowd=3.0.210-g5f9f594

./nuget pack LegacyTestApp.nuspec -OutputDirectory bin -BasePath bin/Release/net48

dotnet tool install csq --version 3.0.210-g5f9f594 --tool-path bin/csq

./bin/csq/csq.exe --csq-version 3.0.210-g5f9f594 releasify -p bin/LegacyTestApp.1.0.0.nupkg