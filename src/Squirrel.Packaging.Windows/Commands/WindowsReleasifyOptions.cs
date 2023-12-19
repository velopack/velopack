﻿namespace Squirrel.Packaging.Windows.Commands;

public class WindowsReleasifyOptions : WindowsSigningOptions
{
    public DirectoryInfo ReleaseDir { get; set; }

    public RID TargetRuntime { get; set; }

    public string Package { get; set; }

    public bool NoDelta { get; set; }

    public string Runtimes { get; set; }

    public string SplashImage { get; set; }

    public string Icon { get; set; }

    public string EntryExecutableName { get; set; }
}