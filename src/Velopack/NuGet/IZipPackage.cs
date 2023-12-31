#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
using System.Collections.Generic;

namespace Squirrel.NuGet
{
    public interface IZipPackage : IPackage
    {
        IEnumerable<string> Frameworks { get; }
        IEnumerable<ZipPackageFile> Files { get; }
    }
}