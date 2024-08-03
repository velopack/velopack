﻿using System;
using System.IO;
using Riok.Mapperly.Abstractions;
using Velopack.Packaging;
using Velopack.Packaging.Unix.Commands;
using Velopack.Packaging.Windows.Commands;

namespace Velopack.Build;

[Mapper(
    RequiredMappingStrategy = RequiredMappingStrategy.Target,
    EnabledConversions = MappingConversionType.None)]
public static partial class TaskOptionsMapper
{
    public static partial WindowsPackOptions ToWinPackOptions(this PackTask cmd);
    public static partial LinuxPackOptions ToLinuxPackOptions(this PackTask cmd);
    public static partial OsxPackOptions ToOsxPackOptions(this PackTask cmd);

    private static DirectoryInfo StringToDirectoryInfo(string t)
    {
        var di = new DirectoryInfo(t);
        if (!di.Exists) di.Create();
        return di;
    }

    private static RID StringToRID(string t) => RID.Parse(t);

    private static DeltaMode StringToDeltaMode(string t) => (DeltaMode) Enum.Parse(typeof(DeltaMode), t, true);
}
