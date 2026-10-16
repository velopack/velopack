using Velopack.Core;
using Velopack.Packaging;

namespace Velopack.CommandLine.Tests;

public class AppAssociationsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",,")]
    [InlineData(" , ; ")]
    public void FileAssociations_EmptyInput_YieldsEmptyList(string? input)
    {
        Assert.Empty(AppAssociations.ParseFileAssociations(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",,")]
    [InlineData(" ; , ")]
    public void UrlProtocols_EmptyInput_YieldsEmptyList(string? input)
    {
        Assert.Empty(AppAssociations.ParseUrlProtocols(input));
    }

    [Fact]
    public void FileAssociations_AreNormalized()
    {
        // trimmed, lowercased, leading dot added, distinct, comma or semicolon separated, order preserved
        var result = AppAssociations.ParseFileAssociations(" .MyProj, mytpl ;.myproj,MYTPL , .a-b_c1 ");
        Assert.Equal([".myproj", ".mytpl", ".a-b_c1"], result);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..proj")]
    [InlineData(".-proj")]
    [InlineData(".my proj")]
    [InlineData(".my.proj")]
    [InlineData(".my/proj")]
    [InlineData(".myprój")]
    public void FileAssociations_InvalidExtension_Throws(string input)
    {
        var ex = Assert.Throws<UserInfoException>(() => AppAssociations.ParseFileAssociations(input));
        Assert.Contains("Invalid file association", ex.Message);
        Assert.Contains(input.Trim().ToLowerInvariant(), ex.Message);
    }

    [Fact]
    public void FileAssociations_ExtensionLength_IsBounded()
    {
        var maxLength = "." + new string('a', 64);
        Assert.Equal([maxLength], AppAssociations.ParseFileAssociations(maxLength));
        Assert.Throws<UserInfoException>(() => AppAssociations.ParseFileAssociations(maxLength + "a"));
    }

    [Fact]
    public void UrlProtocols_AreNormalized()
    {
        // trimmed, lowercased, trailing "://" or ":" stripped, distinct, order preserved
        var result = AppAssociations.ParseUrlProtocols(" MyApp://, myapp-beta: ; myapp , x+y.z ");
        Assert.Equal(["myapp", "myapp-beta", "x+y.z"], result);
    }

    [Theory]
    [InlineData("1app")]
    [InlineData("-app")]
    [InlineData("my app")]
    [InlineData("my_app")]
    [InlineData("my/app")]
    [InlineData("myapp:/")]
    [InlineData("://")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 65 chars
    public void UrlProtocols_InvalidScheme_Throws(string input)
    {
        var ex = Assert.Throws<UserInfoException>(() => AppAssociations.ParseUrlProtocols(input));
        Assert.Contains("Invalid URL protocol", ex.Message);
    }

    [Fact]
    public void UrlProtocols_SchemeLength_IsBounded()
    {
        var maxLength = new string('a', 64);
        Assert.Equal([maxLength], AppAssociations.ParseUrlProtocols(maxLength));
        Assert.Throws<UserInfoException>(() => AppAssociations.ParseUrlProtocols(maxLength + "a"));
    }

    [Theory]
    [InlineData("http")]
    [InlineData("HTTPS://")]
    [InlineData("file")]
    [InlineData("ftp")]
    [InlineData("mailto:")]
    [InlineData("javascript")]
    [InlineData("data")]
    [InlineData("about")]
    [InlineData("blob")]
    [InlineData("ws")]
    [InlineData("wss")]
    [InlineData("tel")]
    [InlineData("sms")]
    [InlineData("news")]
    [InlineData("ldap")]
    [InlineData("res")]
    [InlineData("shell")]
    [InlineData("ms-settings")]
    [InlineData("MS-anything")]
    public void UrlProtocols_ReservedScheme_Throws(string input)
    {
        var ex = Assert.Throws<UserInfoException>(() => AppAssociations.ParseUrlProtocols("myapp," + input));
        Assert.Contains("reserved", ex.Message);
    }

    [Fact]
    public void UrlProtocols_ReservedCheck_IsExactMatch()
    {
        // only exact reserved names (and the ms- prefix) are rejected, not names that merely contain them
        Assert.Equal(["httpx", "myfile", "msapp"], AppAssociations.ParseUrlProtocols("httpx,myfile,msapp"));
    }

    [Fact]
    public void Join_ProducesCommaDelimitedMetadata()
    {
        Assert.Equal(".a,.b", AppAssociations.Join(AppAssociations.ParseFileAssociations("a;b")));
    }
}
