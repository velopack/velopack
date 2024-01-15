#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NuGet.Versioning;

namespace Velopack.NuGet
{
    public class NuspecManifest : IPackage
    {
        public string ProductName => Title ?? Id;
        public string ProductDescription => Description ?? Summary ?? Title ?? Id;
        public string ProductCompany => (Authors.Any() ? String.Join(", ", Authors) : Owners) ?? ProductName;
        public string ProductCopyright => Copyright ?? "Copyright © " + DateTime.Now.Year.ToString() + " " + ProductCompany;

        //public string FilePath { get; private set; }

        public string Id { get; private set; }
        public SemanticVersion Version { get; private set; }
        public Uri ProjectUrl { get; private set; }
        public string ReleaseNotes { get; private set; }
        public string ReleaseNotesHtml { get; private set; }
        public Uri IconUrl { get; private set; }
        public string Language { get; private set; }
        public IEnumerable<string> Tags { get; private set; } = Enumerable.Empty<string>();
        public IEnumerable<string> RuntimeDependencies { get; private set; } = Enumerable.Empty<string>();
        public IEnumerable<FrameworkAssemblyReference> FrameworkAssemblies { get; private set; } = Enumerable.Empty<FrameworkAssemblyReference>();
        public IEnumerable<PackageDependencySet> DependencySets { get; private set; } = Enumerable.Empty<PackageDependencySet>();
        public RID Rid { get; private set; }
        public string Channel { get; private set; }

        protected string Description { get; private set; }
        protected IEnumerable<string> Authors { get; private set; } = Enumerable.Empty<string>();
        protected string Owners { get; private set; }
        protected string Title { get; private set; }
        protected string Summary { get; private set; }
        protected string Copyright { get; private set; }

        private static readonly string[] ExcludePaths = new[] { "_rels", "package" };

        protected NuspecManifest() { }

        public static NuspecManifest ParseFromFile(string filePath)
        {
            using var fs = File.OpenRead(filePath);
            var nu = new NuspecManifest();
            nu.ReadManifest(fs);
            //nu.FilePath = filePath;
            return nu;
        }

        public static bool TryParseFromFile(string filePath, out NuspecManifest manifest)
        {
            try {
                manifest = ParseFromFile(filePath);
                return true;
            } catch {
                manifest = null;
                return false;
            }
        }

        protected void ReadManifest(Stream manifestStream)
        {
            var document = NugetUtil.LoadSafe(manifestStream, ignoreWhiteSpace: true);

            var metadataElement = document.Root.ElementsNoNamespace("metadata").FirstOrDefault();
            if (metadataElement == null) {
                throw new InvalidDataException("Invalid nuspec xml. Required element 'metadata' missing.");
            }

            var allElements = new HashSet<string>();

            XNode node = metadataElement.FirstNode;
            while (node != null) {
                var element = node as XElement;
                if (element != null) {
                    ReadMetadataValue(element, allElements);
                }
                node = node.NextNode;
            }
        }

        private void ReadMetadataValue(XElement element, HashSet<string> allElements)
        {
            if (element.Value == null) {
                return;
            }

            allElements.Add(element.Name.LocalName);

            IEnumerable<string> getCommaDelimitedValue(string v)
            {
                return v?.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()) ?? Enumerable.Empty<string>();
            }

            string value = element.Value.SafeTrim();
            switch (element.Name.LocalName) {
            case "id":
                Id = value;
                break;
            case "version":
                Version = NuGetVersion.Parse(value);
                break;
            case "authors":
                Authors = getCommaDelimitedValue(value);
                break;
            case "owners":
                Owners = value;
                break;
            case "projectUrl":
                ProjectUrl = new Uri(value);
                break;
            case "iconUrl":
                IconUrl = new Uri(value);
                break;
            case "description":
                Description = value;
                break;
            case "summary":
                Summary = value;
                break;
            case "releaseNotes":
                ReleaseNotes = value;
                break;
            case "copyright":
                Copyright = value;
                break;
            case "language":
                Language = value;
                break;
            case "title":
                Title = value;
                break;
            case "tags":
                Tags = getCommaDelimitedValue(value);
                break;
            case "dependencies":
                DependencySets = ReadDependencySets(element);
                break;
            case "frameworkAssemblies":
                FrameworkAssemblies = ReadFrameworkAssemblies(element);
                break;

            // ===
            // the following metadata elements are added by velopack and are not
            // used by nuget.
            case "runtimeDependencies":
                RuntimeDependencies = getCommaDelimitedValue(value);
                break;
            case "releaseNotesHtml":
                ReleaseNotesHtml = value;
                break;
            case "rid":
                Rid = RID.Parse(value);
                break;
            case "channel":
                Channel = value;
                break;
            }
        }

        private List<FrameworkAssemblyReference> ReadFrameworkAssemblies(XElement frameworkElement)
        {
            if (!frameworkElement.HasElements) {
                return new List<FrameworkAssemblyReference>(0);
            }

            return (from element in frameworkElement.ElementsNoNamespace("frameworkAssembly")
                    let assemblyNameAttribute = element.Attribute("assemblyName")
                    where assemblyNameAttribute != null && !String.IsNullOrEmpty(assemblyNameAttribute.Value)
                    select new FrameworkAssemblyReference(
                        assemblyNameAttribute.Value.SafeTrim(),
                        ParseFrameworkNames(element.GetOptionalAttributeValue("targetFramework").SafeTrim()))
                    ).ToList();
        }

        private List<PackageDependencySet> ReadDependencySets(XElement dependenciesElement)
        {
            if (!dependenciesElement.HasElements) {
                return new List<PackageDependencySet>();
            }

            // Disallow the <dependencies> element to contain both <dependency> and 
            // <group> child elements. Unfortunately, this cannot be enforced by XSD.
            if (dependenciesElement.ElementsNoNamespace("dependency").Any() &&
                dependenciesElement.ElementsNoNamespace("group").Any()) {
                throw new InvalidDataException("Manifest_DependenciesHasMixedElements");
            }

            var dependencies = ReadDependencies(dependenciesElement);
            if (dependencies.Count > 0) {
                // old format, <dependency> is direct child of <dependencies>
                var dependencySet = new PackageDependencySet(null, dependencies);
                return new List<PackageDependencySet> { dependencySet };
            } else {
                var groups = dependenciesElement.ElementsNoNamespace("group");
                return (from element in groups
                        let fx = ParseFrameworkNames(element.GetOptionalAttributeValue("targetFramework").SafeTrim())
                        select new PackageDependencySet(
                            element.GetOptionalAttributeValue("targetFramework").SafeTrim(),
                            ReadDependencies(element))).ToList();
            }
        }

        private List<PackageDependency> ReadDependencies(XElement containerElement)
        {
            // element is <dependency>
            return (from element in containerElement.ElementsNoNamespace("dependency")
                    let idElement = element.Attribute("id")
                    where idElement != null && !String.IsNullOrEmpty(idElement.Value)
                    select new PackageDependency(
                        idElement.Value.SafeTrim(),
                        element.GetOptionalAttributeValue("version").SafeTrim()
                    )).ToList();
        }

        private IEnumerable<string> ParseFrameworkNames(string frameworkNames)
        {
            if (String.IsNullOrEmpty(frameworkNames)) {
                return Enumerable.Empty<string>();
            }

            return frameworkNames.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        }

        protected bool IsPackageFile(string partPath)
        {
            if (Path.GetFileName(partPath).Equals(NugetUtil.ContentTypeFileName, StringComparison.OrdinalIgnoreCase))
                return false;

            if (Path.GetExtension(partPath).Equals(NugetUtil.ManifestExtension, StringComparison.OrdinalIgnoreCase))
                return false;

            string directory = Path.GetDirectoryName(partPath);
            return !ExcludePaths.Any(p => directory.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }
    }
}
