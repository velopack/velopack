using System.IO.Packaging;
using NuGet.Versioning;
using Velopack.NuGet;
using Velopack.Tests.TestHelpers;
using ZipPackage = Velopack.NuGet.ZipPackage;

namespace Velopack.Tests
{
    public class ZipPackageTests
    {
        [Fact]
        public void HasSameFilesAndDependenciesAsPackaging()
        {
            using var _1 = Utility.GetTempDirectory(out var tempDir);
            var inputPackage = PathHelper.GetFixture("slack-1.1.8-full.nupkg");
            var copyPackage = Path.Combine(tempDir, "slack-1.1.8-full.nupkg");
            File.Copy(inputPackage, copyPackage);

            var zp = new ZipPackage(inputPackage);
            var zipf = zp.Files.OrderBy(f => f.Path).ToArray();
            var zipfLib = zp.Files.Where(f => f.IsLibFile()).OrderBy(f => f.Path).ToArray();

            using Package package = Package.Open(copyPackage);
            var packaging = GetFiles(package).OrderBy(f => f.Path).ToArray();
            var packagingLib = GetLibFiles(package).OrderBy(f => f.Path).ToArray();

            //for (int i = 0; i < zipf.Length; i++) {
            //    if (zipf[i] != packagingLib[i])
            //        throw new Exception();
            //}

            Assert.Equal(packaging, zipf);
            Assert.Equal(packagingLib, zipfLib);
        }

        [Fact]
        public void ParsesNuspecCorrectly()
        {
            var inputPackage = PathHelper.GetFixture("FullNuspec.1.0.0.nupkg");
            var zp = new ZipPackage(inputPackage);

            var dyn = ExposedObject.From(zp);

            Assert.Equal("FullNuspec", zp.Id);
            Assert.Equal(SemanticVersion.Parse("1.0.0"), zp.Version);
            Assert.Equal(new[] { "Anaïs Betts", "Caelan Sayler" }, dyn.Authors);
            Assert.Equal(new Uri("https://github.com/clowd/Clowd.Squirrel"), zp.ProjectUrl);
            Assert.Equal(new Uri("https://user-images.githubusercontent.com/1287295/131249078-9e131e51-0b66-4dc7-8c0a-99cbea6bcf80.png"), zp.IconUrl);
            Assert.Equal("A test description", dyn.Description);
            Assert.Equal("A summary", dyn.Summary);
            Assert.Equal("release notes\nwith multiple lines", zp.ReleaseNotes);
            Assert.Equal("Copyright ©", dyn.Copyright);
            Assert.Equal("en-US", zp.Language);
            Assert.Equal("Squirrel for Windows", dyn.Title);
        }

        IEnumerable<ZipPackageFile> GetLibFiles(Package package)
        {
            return GetFiles(package, NugetUtil.LibDirectory);
        }

        IEnumerable<ZipPackageFile> GetFiles(Package package, string directory)
        {
            string folderPrefix = directory + Path.DirectorySeparatorChar;
            return GetFiles(package).Where(file => file.Path.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase));
        }

        List<ZipPackageFile> GetFiles(Package package)
        {
            return (from part in package.GetParts()
                    where IsPackageFile(part)
                    select new ZipPackageFile(part.Uri)).ToList();
        }

        bool IsPackageFile(PackagePart part)
        {
            string path = NugetUtil.GetPath(part.Uri);
            string directory = Path.GetDirectoryName(path);
            string[] ExcludePaths = new[] { "_rels", "package" };
            return !ExcludePaths.Any(p => directory.StartsWith(p, StringComparison.OrdinalIgnoreCase)) && !IsManifest(path);
        }

        bool IsManifest(string p)
        {
            return Path.GetExtension(p).Equals(NugetUtil.ManifestExtension, StringComparison.OrdinalIgnoreCase);
        }
    }
}
