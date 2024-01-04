

Set-Alias Squirrel ($env:USERPROFILE + "\.nuget\packages\clowd.squirrel\2.11.1\tools\Squirrel.exe");

dotnet build -c Release -p:UseClowd=2.11.1

./nuget pack LegacyTestApp.nuspec -OutputDirectory bin -BasePath bin/Release/net48

Squirrel releasify -p bin/LegacyTestApp.1.0.0.nupkg