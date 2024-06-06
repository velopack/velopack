using AsmResolver.PE;
using AsmResolver.PE.File;
using AsmResolver.PE.File.Headers;
using AsmResolver.PE.Win32Resources;
using AsmResolver.PE.Win32Resources.Builder;
using AsmResolver.PE.Win32Resources.Icon;
using AsmResolver.PE.Win32Resources.Version;
using Microsoft.Extensions.Logging;
using Velopack.NuGet;

namespace Velopack.Packaging.Windows;

public class ResourceEdit
{
    const ushort kLangNeutral = 0;
    const ushort kCodePageUtf16 = 1200;

    private readonly string _exePath;
    private readonly ILogger _logger;
    private readonly PEFile _file;
    private readonly ushort _langId = kLangNeutral;

    IResourceDirectory _resources;
    private bool _disposed;

    public ResourceEdit(string exeFile, ILogger logger)
    {
        _exePath = exeFile;
        _logger = logger;
        _file = PEFile.FromBytes(File.ReadAllBytes(exeFile));
        var image = PEImage.FromFile(_file);
        _resources = image.Resources ?? new ResourceDirectory((uint) 0);

        // if there is already a manifest, we want to keep the existing language ID
        try {
            var existingInfo = VersionInfoResource.FromDirectory(_resources);
            if (existingInfo != null) {
                _langId = (ushort) existingInfo.Lcid;
            }
        } catch { }
    }

    public void SetExeIcon(string iconPath)
    {
        ThrowIfDisposed();

        // should use this commented code once these issues are fixed in AsmResolver 
        // https://github.com/Washi1337/AsmResolver/issues/532
        // https://github.com/Washi1337/AsmResolver/issues/533
        // var iconResource = new IconResource();
        // var group = new IconGroupDirectory();
        // iconResource.AddEntry(1, group);
        // iconResource.WriteToDirectory(_resources);

        var group = new IconGroupDirectory() {
            Type = 1,
        };

        var extractor = new IcoExtract(_logger);
        var frames = extractor.ExtractFrames(new FileInfo(iconPath));

        for (var p = 0; p < frames.Count; p++) {
            var f = frames[p];

            var dictEntry = new IconGroupDirectoryEntry() {
                BytesInRes = (uint) f.RawData.Length,
                Height = (byte) f.CookedData.Height,
                Width = (byte) f.CookedData.Width,
                Id = (ushort) (p + 1),
                Reserved = 0,
                PixelBitCount = (ushort) f.CookedData.PixelType.BitsPerPixel,
                ColorCount = (byte) f.Encoding.PaletteSize,
                ColorPlanes = 1,
            };

            var iconEntry = new IconEntry() {
                RawIcon = f.RawData,
            };

            group.AddEntry(dictEntry, iconEntry);
            group.Count++;
        }

        WriteToDirectory(_resources, new Dictionary<uint, IconGroupDirectory> { { 1, group } });
    }

    private void WriteToDirectory(IResourceDirectory rootDirectory, Dictionary<uint, IconGroupDirectory> _entries)
    {
        ThrowIfDisposed();

        // this function can be removed once these issues are fixed in AsmResolver 
        // https://github.com/Washi1337/AsmResolver/issues/532
        // https://github.com/Washi1337/AsmResolver/issues/533

        var newIconDirectory = new ResourceDirectory(ResourceType.Icon);
        foreach (var entry in _entries) {
            foreach (var (groupEntry, iconEntry) in entry.Value.GetIconEntries()) {
                newIconDirectory.Entries.Add(new ResourceDirectory(groupEntry.Id) { Entries = { new ResourceData(_langId, iconEntry) } });
            }
        }

        var newGroupIconDirectory = new ResourceDirectory(ResourceType.GroupIcon);
        foreach (var entry in _entries) {
            newGroupIconDirectory.Entries.Add(new ResourceDirectory(entry.Key) { Entries = { new ResourceData(_langId, entry.Value) } });
        }

        rootDirectory.AddOrReplaceEntry(newIconDirectory);
        rootDirectory.AddOrReplaceEntry(newGroupIconDirectory);
    }

    public void SetVersionInfo(PackageManifest package)
    {
        ThrowIfDisposed();

        // We just replace the entire VersionInfo section, so we know that the
        // VarFileInfo languages will be correct.
        var fileVersion = new Version(package.Version.Major, package.Version.Minor, package.Version.Patch, 0);

        var versionInfo = new VersionInfoResource(_langId);
        versionInfo.FixedVersionInfo.FileOS = FileOS.NT;
        versionInfo.FixedVersionInfo.FileType = FileType.App;
        versionInfo.FixedVersionInfo.FileVersion = fileVersion;
        versionInfo.FixedVersionInfo.ProductVersion = fileVersion;

        StringFileInfo stringInfo = new StringFileInfo();
        versionInfo.AddEntry(stringInfo);

        VarFileInfo varInfo = new VarFileInfo();
        versionInfo.AddEntry(varInfo);

        var stringTable = new StringTable(_langId, kCodePageUtf16);
        stringTable[StringTable.CompanyNameKey] = package.ProductCompany;
        stringTable[StringTable.FileDescriptionKey] = package.ProductDescription;
        stringTable[StringTable.FileVersionKey] = package.Version.ToFullString();
        stringTable[StringTable.LegalCopyrightKey] = package.ProductCopyright;
        stringTable[StringTable.ProductNameKey] = package.ProductName;
        stringTable[StringTable.ProductVersionKey] = package.Version.ToFullString();
        stringTable[StringTable.CommentsKey] = $"Generated by Velopack {VelopackRuntimeInfo.VelopackNugetVersion}";
        stringInfo.Tables.Add(stringTable);

        var varTable = new VarTable();
        varTable.Values.Add(((uint) kCodePageUtf16 << 16) | _langId);
        varInfo.Tables.Add(varTable);

        versionInfo.WriteToDirectory(_resources);
    }

    public void CopyResourcesFrom(string otherExeFile)
    {
        ThrowIfDisposed();

        var file = PEFile.FromBytes(File.ReadAllBytes(otherExeFile));
        var image = PEImage.FromFile(file);
        _resources = image.Resources;
    }

    public void Commit()
    {
        ThrowIfDisposed();
        _disposed = true;

        var sortedResources = new ResourceDirectory((uint) 0);
        foreach (var entry in _resources.Entries.OrderBy(e => e.Id).ToArray()) {
            _resources.RemoveEntry(entry.Id);
            sortedResources.Entries.Add(entry);
        }

        var resourceBuffer = new ResourceDirectoryBuffer();
        resourceBuffer.AddDirectory(sortedResources);

        var resourceDirectory = _file.OptionalHeader.GetDataDirectory(DataDirectoryIndex.ResourceDirectory);

        if (resourceDirectory.IsPresentInPE) {
            var section = _file.GetSectionContainingRva(resourceDirectory.VirtualAddress);
            section.Contents = resourceBuffer;
        } else {
            _file.Sections.Add(new PESection(".rsrc", SectionFlags.MemoryRead | SectionFlags.ContentInitializedData, resourceBuffer));
        }

        _file.UpdateHeaders();
        _file.OptionalHeader.SetDataDirectory(DataDirectoryIndex.ResourceDirectory,
            new DataDirectory(resourceBuffer.Rva, resourceBuffer.GetPhysicalSize()));

        Utility.Retry(() => {
            using var fs = File.Create(_exePath);
            _file.Write(fs);
        });
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ResourceEdit));
    }
}
