using Velopack.Packaging.Unix.Commands;
using Velopack.Util;

namespace Velopack.Packaging.Tests;

/// <summary>
/// The vpk [osx] pack option rules: which signing routes (codesign, rcodesign) and outputs combine, and what changes
/// off macOS, where Apple's tools and the .pkg installer are unavailable.
/// </summary>
public class OsxPackValidatorTests
{
    private static string TempFile(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    private static OsxPackOptions ValidOptions(string dir) => new() {
        PackId = "My.App",
        PackVersion = "1.0.0",
        PackDirectory = dir,
    };

    private static OsxPackOptions WithP12(OsxPackOptions options, string dir)
    {
        options.SignP12File = TempFile(dir, "id.p12");
        options.SignP12PasswordFile = TempFile(dir, "password.txt");
        return options;
    }

    private static FluentValidation.Results.ValidationResult Validate(OsxPackOptions options) =>
        new OsxPackOptionsValidator().Validate(options);

    private static void AssertSingleError(OsxPackOptions options, string property)
    {
        var error = Assert.Single(Validate(options).Errors);
        Assert.Equal(property, error.PropertyName);
    }

    // Off macOS the Apple-tool options are refused outright (AppleToolOptionIsRejectedOffMacOS), so the rules for
    // combining them with rcodesign only apply on macOS.
    private static void SkipUnlessMacOS() =>
        Assert.SkipUnless(VelopackRuntimeInfo.IsOSX, "Apple's tools and the .pkg installer only exist on macOS");

    [Fact]
    public void P12WithoutPasswordIsRejected()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var options = ValidOptions(dir);
        options.SignP12File = TempFile(dir, "id.p12");

        AssertSingleError(options, nameof(OsxPackOptions.SignP12PasswordFile));
    }

    [Fact]
    public void PasswordWithoutP12IsRejected()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var options = ValidOptions(dir);
        options.SignP12PasswordFile = TempFile(dir, "password.txt");

        AssertSingleError(options, nameof(OsxPackOptions.SignP12PasswordFile));
    }

    [Fact]
    public void MissingP12FileIsRejected()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var options = WithP12(ValidOptions(dir), dir);
        options.SignP12File = Path.Combine(dir, "missing.p12");

        AssertSingleError(options, nameof(OsxPackOptions.SignP12File));
    }

    [Fact]
    public void ApiKeyWithoutP12IsRejected()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var options = ValidOptions(dir);
        options.NotaryApiKeyFile = TempFile(dir, "key.json");

        AssertSingleError(options, nameof(OsxPackOptions.NotaryApiKeyFile));
    }

    [Fact]
    public void AppIdentityWithP12IsRejectedOnMacOS()
    {
        SkipUnlessMacOS();
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var options = WithP12(ValidOptions(dir), dir);
        options.SignAppIdentity = "Developer ID Application: Someone";

        AssertSingleError(options, nameof(OsxPackOptions.SignP12File));
    }

    [Fact]
    public void P12WithoutInstallerRejectsInstallIdentityOnMacOS()
    {
        SkipUnlessMacOS();
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var options = WithP12(ValidOptions(dir), dir);
        options.SignInstallIdentity = "Developer ID Installer: Someone";
        options.NoInst = true;

        AssertSingleError(options, nameof(OsxPackOptions.SignInstallIdentity));
    }

    [Fact]
    public void P12WithoutInstallerRejectsNotaryProfileOnMacOS()
    {
        SkipUnlessMacOS();
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var options = WithP12(ValidOptions(dir), dir);
        options.NotaryProfile = "profile";
        options.NoInst = true;

        AssertSingleError(options, nameof(OsxPackOptions.NotaryProfile));
    }

    [Fact]
    public void P12WithoutInstallerRejectsKeychainOnMacOS()
    {
        SkipUnlessMacOS();
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var options = WithP12(ValidOptions(dir), dir);
        options.Keychain = TempFile(dir, "login.keychain-db");
        options.NoInst = true;

        AssertSingleError(options, nameof(OsxPackOptions.Keychain));
    }

    [Fact]
    public void P12WithInstallerRejectsNotaryProfileWithoutInstallIdentityOnMacOS()
    {
        SkipUnlessMacOS();
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var options = WithP12(ValidOptions(dir), dir);
        options.NotaryProfile = "profile";

        AssertSingleError(options, nameof(OsxPackOptions.NotaryProfile));
    }

    [Fact]
    public void P12WithInstallerRejectsKeychainWithoutNotaryProfileOnMacOS()
    {
        SkipUnlessMacOS();
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var options = WithP12(ValidOptions(dir), dir);
        options.SignInstallIdentity = "Developer ID Installer: Someone";
        options.Keychain = TempFile(dir, "login.keychain-db");

        AssertSingleError(options, nameof(OsxPackOptions.Keychain));
    }

    [Theory]
    [InlineData(nameof(OsxPackOptions.SignAppIdentity))]
    [InlineData(nameof(OsxPackOptions.SignInstallIdentity))]
    [InlineData(nameof(OsxPackOptions.NotaryProfile))]
    [InlineData(nameof(OsxPackOptions.Keychain))]
    [InlineData(nameof(OsxPackOptions.InstWelcome))]
    [InlineData(nameof(OsxPackOptions.InstReadme))]
    [InlineData(nameof(OsxPackOptions.InstLicense))]
    [InlineData(nameof(OsxPackOptions.InstConclusion))]
    public void AppleToolOptionIsRejectedOffMacOS(string property)
    {
        // vpk does not offer these options off macOS, but a [json] config sets the options object directly.
        Assert.SkipWhen(VelopackRuntimeInfo.IsOSX, "Apple's tools are available on macOS");
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var options = ValidOptions(dir);
        typeof(OsxPackOptions).GetProperty(property)!.SetValue(options, TempFile(dir, "value.txt"));

        var error = Assert.Single(Validate(options).Errors);
        Assert.Equal(property, error.PropertyName);
        Assert.Contains("only available on macOS", error.ErrorMessage);
    }

    [Fact]
    public void UnsignedPackIsValid()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);

        Assert.Empty(Validate(ValidOptions(dir)).Errors);
    }

    [Fact]
    public void RcodesignRouteIsValid()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var options = WithP12(ValidOptions(dir), dir);
        options.NotaryApiKeyFile = TempFile(dir, "key.json");
        options.SignEntitlements = TempFile(dir, "app.entitlements");
        options.SignDisableDeep = true;

        Assert.Empty(Validate(options).Errors);
    }

    [Fact]
    public void RcodesignRouteWithNotarizedInstallerIsValidOnMacOS()
    {
        // The app is signed and notarized with rcodesign, the .pkg with Apple's tools (signInstallIdentity, notaryProfile).
        Assert.SkipUnless(VelopackRuntimeInfo.IsOSX, "the .pkg installer is only built on macOS");
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var options = WithP12(ValidOptions(dir), dir);
        options.NotaryApiKeyFile = TempFile(dir, "key.json");
        options.SignInstallIdentity = "Developer ID Installer: Someone";
        options.NotaryProfile = "profile";
        options.Keychain = TempFile(dir, "login.keychain-db");

        Assert.Empty(Validate(options).Errors);
    }

    [Fact]
    public void CodesignRouteIsValidOnMacOS()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsOSX, "codesign and notarytool only exist on macOS");
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var options = ValidOptions(dir);
        options.SignAppIdentity = "Developer ID Application: Someone";
        options.SignInstallIdentity = "Developer ID Installer: Someone";
        options.NotaryProfile = "profile";
        options.Keychain = TempFile(dir, "login.keychain-db");
        options.InstWelcome = TempFile(dir, "welcome.txt");

        Assert.Empty(Validate(options).Errors);
    }

    [Fact]
    public void NoPortableWithNoInstIsRejected()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var options = ValidOptions(dir);
        options.NoPortable = true;
        options.NoInst = true;

        AssertSingleError(options, nameof(OsxPackOptions.NoPortable));
    }

    [Fact]
    public void InstallerIsAlwaysSkippedOffMacOS()
    {
        Assert.SkipWhen(VelopackRuntimeInfo.IsOSX, "the .pkg installer is built on macOS");

        Assert.True(new OsxPackOptions { NoInst = false }.NoInst);
    }

    [Fact]
    public void InstallerIsBuiltByDefaultOnMacOS()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsOSX, "the .pkg installer is only built on macOS");

        Assert.False(new OsxPackOptions().NoInst);
        Assert.True(new OsxPackOptions { NoInst = true }.NoInst);
    }

    [Fact]
    public void NoPortableIsRejectedOffMacOS()
    {
        Assert.SkipWhen(VelopackRuntimeInfo.IsOSX, "the .pkg installer is built on macOS");
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var options = ValidOptions(dir);
        options.NoPortable = true;

        var error = Assert.Single(Validate(options).Errors);
        Assert.Equal(nameof(OsxPackOptions.NoPortable), error.PropertyName);
        Assert.Contains("off macOS", error.ErrorMessage);
    }

    [Fact]
    public void PkgPackDirectoryIsRejectedOffMacOS()
    {
        Assert.SkipWhen(VelopackRuntimeInfo.IsOSX, "pkgutil can extract a .pkg on macOS");
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var options = ValidOptions(dir);
        options.PackDirectory = Path.Combine(dir, "MyApp.pkg");

        AssertSingleError(options, nameof(OsxPackOptions.PackDirectory));
    }

    [Fact]
    public void PkgPackDirectoryIsAllowedOnMacOS()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsOSX, "macOS only");
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var options = ValidOptions(dir);
        options.PackDirectory = Path.Combine(dir, "MyApp.pkg");

        Assert.Empty(Validate(options).Errors);
    }
}
