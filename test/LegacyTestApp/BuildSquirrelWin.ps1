

Set-Alias Squirrel ($env:USERPROFILE + "\.nuget\packages\squirrel.windows\2.0.1\tools\Squirrel.com");

dotnet build -c Release

./nuget pack LegacyTestApp.nuspec -OutputDirectory bin -BasePath bin/Release/net48

Squirrel --releasify bin/LegacyTestApp.1.0.0.nupkg --no-msi