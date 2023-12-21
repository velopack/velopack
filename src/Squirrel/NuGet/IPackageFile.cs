#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
using System;

namespace Squirrel.NuGet
{
    public interface IPackageFile
    {
        Uri Key { get; }
        string Path { get; }
        string EffectivePath { get; }
        string TargetFramework { get; }
        bool IsLibFile();
        bool IsContentFile();
    }
}