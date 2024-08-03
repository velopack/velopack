﻿using Velopack.Packaging.Abstractions;

namespace Velopack.Packaging.Unix.Commands;

public class OsxBundleOptions : IOutputOptions
{
    public DirectoryInfo ReleaseDir { get; set; }

    public string PackId { get; set; }

    public string PackVersion { get; set; }

    public string PackDirectory { get; set; }

    public string PackAuthors { get; set; }

    public string PackTitle { get; set; }

    public string EntryExecutableName { get; set; }

    public string Icon { get; set; }

    public string BundleId { get; set; }

    public string InfoPlistPath { get; set; }
}
