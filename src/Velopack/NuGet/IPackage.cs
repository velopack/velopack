#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
using System;
using System.Collections.Generic;
using NuGet.Versioning;

namespace Velopack.NuGet
{
    public interface IPackage
    {
        string Id { get; }
        string ProductName { get; }
        string ProductDescription { get; }
        string ProductCompany { get; }
        string ProductCopyright { get; }
        string Language { get; }
        SemanticVersion Version { get; }
        Uri ProjectUrl { get; }
        string ReleaseNotes { get; }
        Uri IconUrl { get; }
        IEnumerable<string> Tags { get; }
        IEnumerable<FrameworkAssemblyReference> FrameworkAssemblies { get; }
        IEnumerable<PackageDependencySet> DependencySets { get; }
        IEnumerable<string> RuntimeDependencies { get; }
    }
}
