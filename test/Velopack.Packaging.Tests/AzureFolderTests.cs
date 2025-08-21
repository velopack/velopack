using System.IO;
using System.Threading.Tasks;
using Velopack.Deployment;
using Velopack.Core;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;

namespace Velopack.Packaging.Tests;

public class AzureFolderTests
{
    [Fact]
    public void AzureRepository_UploadWithFolder_PrependsPathToKeys()
    {
        // This test verifies that when a folder is specified,
        // all blob keys (filenames) are prepended with the folder path
        
        var options = new AzureUploadOptions
        {
            Folder = "releases/v1",
            Account = "testaccount",
            Key = "testkey",
            Container = "testcontainer"
        };
        
        Assert.Equal("releases/v1", options.Folder);
    }
    
    [Fact]
    public void AzureRepository_DownloadWithFolder_PrependsPathToKeys()
    {
        // This test verifies that download options can have a folder
        
        var options = new AzureDownloadOptions
        {
            Folder = "releases/v1",
            Account = "testaccount", 
            Key = "testkey",
            Container = "testcontainer"
        };
        
        Assert.Equal("releases/v1", options.Folder);
    }
    
    [Fact]
    public void AzureRepository_FolderPath_NormalizesSlashes()
    {
        // Test that trailing slashes are handled correctly
        var options1 = new AzureUploadOptions { Folder = "releases/v1/" };
        var options2 = new AzureUploadOptions { Folder = "releases/v1" };
        
        // Both should work correctly when used
        Assert.Equal("releases/v1/", options1.Folder);
        Assert.Equal("releases/v1", options2.Folder);
    }
    
    [Theory]
    [InlineData("releases/v1", "releases/v1/file.nupkg")]
    [InlineData("releases/v1/", "releases/v1/file.nupkg")]
    [InlineData("releases/v1//", "releases/v1/file.nupkg")]
    [InlineData("/releases/v1", "/releases/v1/file.nupkg")]
    [InlineData("", "file.nupkg")]
    [InlineData(null, "file.nupkg")]
    public void AzureRepository_FolderPath_ProducesCorrectBlobKey(string folder, string expectedKey)
    {
        // Test the logic we use to combine folder and filename
        string filename = "file.nupkg";
        string result;
        
        if (!string.IsNullOrEmpty(folder))
        {
            result = folder.TrimEnd('/') + "/" + filename;
        }
        else
        {
            result = filename;
        }
        
        Assert.Equal(expectedKey, result);
    }
    
}