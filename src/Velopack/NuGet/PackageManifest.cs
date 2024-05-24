#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NuGet.Versioning;

namespace Velopack.NuGet
{
    public class PackageManifest
    {
        public string? ProductName => Title ?? Id;
        public string? ProductDescription => Description ?? Summary ?? Title ?? Id;
        public string? ProductCompany => (Authors.Any() ? String.Join(", ", Authors) : Owners) ?? ProductName;
        public string? ProductCopyright => Copyright ?? "Copyright © " + DateTime.Now.Year.ToString() + " " + ProductCompany;
        public string? Id { get; private set; }
        public SemanticVersion? Version { get; private set; }
        public Uri? ProjectUrl { get; private set; }
        public string? ReleaseNotes { get; private set; }
        public string? ReleaseNotesHtml { get; private set; }
        public Uri? IconUrl { get; private set; }
        public string? Language { get; private set; }
        public string? Channel { get; private set; }
        public string? Description { get; private set; }
        public string? Owners { get; private set; }
        public string? Title { get; private set; }
        public string? Summary { get; private set; }
        public string? Copyright { get; private set; }
        public string? ShortcutAmuid { get; private set; }
        public IEnumerable<string> ShortcutLocations { get; private set; } = Enumerable.Empty<string>();
        public IEnumerable<string> Authors { get; private set; } = Enumerable.Empty<string>();
        public IEnumerable<string> RuntimeDependencies { get; private set; } = Enumerable.Empty<string>();

        private static readonly string[] ExcludePaths = new[] { "_rels", "package" };

        protected PackageManifest() { }

        public static PackageManifest ParseFromFile(string filePath)
        {
            using var fs = File.OpenRead(filePath);
            var nu = new PackageManifest();
            nu.ReadManifest(fs);
            //nu.FilePath = filePath;
            return nu;
        }

        public static bool TryParseFromFile(string filePath, out PackageManifest manifest)
        {
            try {
                manifest = ParseFromFile(filePath);
                return true;
            } catch {
                manifest = null!;
                return false;
            }
        }

        protected void ReadManifest(Stream manifestStream)
        {
            var document = NugetUtil.LoadSafe(manifestStream, ignoreWhiteSpace: true);

            var metadataElement = document.Root.ElementsNoNamespace("metadata").FirstOrDefault()
                ?? throw new InvalidDataException("Invalid nuspec xml. Required element 'metadata' missing.");
            var allElements = new HashSet<string>();

            XNode? node = metadataElement.FirstNode;
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

            // ===
            // the following metadata elements are added by velopack and are not
            // used by nuget.
            case "runtimeDependencies":
                RuntimeDependencies = getCommaDelimitedValue(value);
                break;
            case "releaseNotesHtml":
                ReleaseNotesHtml = value;
                break;
            case "channel":
                Channel = value;
                break;
            case "shortcutLocations":
                ShortcutLocations = getCommaDelimitedValue(value);
                break;
            case "shortcutAmuid":
                ShortcutAmuid = value;
                break;
            }
        }

        protected bool IsPackageFile(string partPath)
        {
            if (Path.GetFileName(partPath).Equals(NugetUtil.ContentTypeFileName, StringComparison.OrdinalIgnoreCase))
                return false;

            if (Path.GetExtension(partPath).Equals(NugetUtil.ManifestExtension, StringComparison.OrdinalIgnoreCase))
                return false;

            string directory = Path.GetDirectoryName(partPath)!;
            return !ExcludePaths.Any(p => directory.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }
    }
}
