﻿// https://github.com/egramtel/dotnet-bundle/blob/master/DotNet.Bundle/StructureBuilder.cs

namespace Velopack.Packaging.Unix;

public class OsxStructureBuilder
{
    private readonly string _id;
    private readonly string _outputDir;
    private readonly string _appDir;

    public OsxStructureBuilder(string appDir)
    {
        _appDir = appDir;
    }

    public OsxStructureBuilder(string id, string outputDir)
    {
        _id = id;
        _outputDir = outputDir;
    }

    public string AppDirectory => _appDir ?? Path.Combine(Path.Combine(_outputDir, _id + ".app"));

    public string ContentsDirectory => Path.Combine(AppDirectory, "Contents");

    public string MacosDirectory => Path.Combine(ContentsDirectory, "MacOS");

    public string ResourcesDirectory => Path.Combine(ContentsDirectory, "Resources");

    public void Build()
    {
        if (string.IsNullOrEmpty(_outputDir))
            throw new NotSupportedException();

        Directory.CreateDirectory(_outputDir);

        if (Directory.Exists(AppDirectory)) {
            Directory.Delete(AppDirectory, true);
        }

        Directory.CreateDirectory(AppDirectory);
        Directory.CreateDirectory(ContentsDirectory);
        Directory.CreateDirectory(MacosDirectory);
        Directory.CreateDirectory(ResourcesDirectory);
    }
}
