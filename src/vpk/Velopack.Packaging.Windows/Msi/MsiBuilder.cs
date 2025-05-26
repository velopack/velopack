using System.Globalization;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using HandlebarsDotNet;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Velopack.Core;
using Velopack.Packaging.Windows.Commands;
using Velopack.Util;
using Velopack.Windows;

namespace Velopack.Packaging.Windows.Msi;

public static class MsiBuilder
{
    public static (string mainTemplate, string enLocale) GenerateWixTemplate(MsiTemplateData data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        var templateContent = GetResourceContent("MsiTemplate.hbs");
        var localeContent = GetResourceContent("MsiLocale_en_US.hbs");

        var template = Handlebars.Compile(templateContent);
        var locale = Handlebars.Compile(localeContent);

        return (template(data), locale(data));
    }

    public static MsiTemplateData ConvertOptionsToTemplateData(DirectoryInfo portableDir, ShortcutLocation shortcuts, string licenseRtfPath,
        string runtimeDeps,
        WindowsPackOptions options)
    {
        // WiX Identifiers may contain ASCII characters A-Z, a-z, digits, underscores (_), or
        // periods(.). Every identifier must begin with either a letter or an underscore.
        var wixId = Regex.Replace(options.PackId, @"[^\w\.]", "_");
        if (char.GetUnicodeCategory(wixId[0]) == UnicodeCategory.DecimalDigitNumber)
            wixId = "_" + wixId;

        var parsedVersion = SemanticVersion.Parse(options.PackVersion);
        var msiVersion = options.MsiVersionOverride;
        if (string.IsNullOrWhiteSpace(msiVersion)) {
            msiVersion = $"{parsedVersion.Major}.{parsedVersion.Minor}.{parsedVersion.Patch}.0";
        }

        string welcomeMessage = MsiUtil.FormatXmlMessage(MsiUtil.RenderMarkdownAsPlainText(MsiUtil.GetFileContent(options.InstWelcome)));
        string readmeMessage = MsiUtil.FormatXmlMessage(MsiUtil.RenderMarkdownAsPlainText(MsiUtil.GetFileContent(options.InstReadme)));
        string conclusionMessage = MsiUtil.FormatXmlMessage(MsiUtil.RenderMarkdownAsPlainText(MsiUtil.GetFileContent(options.InstConclusion)));

        return new MsiTemplateData() {
            WixId = wixId,
            AppId = options.PackId,
            AppPublisher = options.PackAuthors ?? options.PackId,
            AppTitle = options.PackTitle ?? options.PackId,
            AppMsiVersion = msiVersion,
            AppVersion = parsedVersion.ToFullString(),
            SourceDirectoryPath = portableDir.FullName,
            Is64Bit = options.TargetRuntime.Architecture is not RuntimeCpu.x86 and not RuntimeCpu.Unknown,
            IsArm64 = options.TargetRuntime.Architecture is RuntimeCpu.arm64,
            CultureLCID = CultureInfo.GetCultureInfo("en-US").TextInfo.ANSICodePage,
            InstallForAllUsers = options.InstLocation.HasFlag(InstallLocation.PerMachine),
            InstallForCurrentUser = options.InstLocation.HasFlag(InstallLocation.PerUser),
            UpgradeCodeGuid = GuidUtil.CreateGuidFromHash($"{options.PackId}:UpgradeCode").ToString(),
            ComponentGenerationSeedGuid = GuidUtil.CreateGuidFromHash($"{options.PackId}:INSTALLFOLDER").ToString(),
            IconPath = options.Icon,
            StubFileName = (options.PackTitle ?? options.PackId) + ".exe",
            DesktopShortcut = shortcuts.HasFlag(ShortcutLocation.Desktop),
            StartMenuShortcut = shortcuts.HasFlag(ShortcutLocation.StartMenu),
            RustNativeModulePath = HelperFile.GetWixNativeModulePath(options.TargetRuntime),
            SideBannerImagePath = options.MsiBanner ?? HelperFile.WixAssetsDialogBackground,
            TopBannerImagePath = options.MsiLogo ?? HelperFile.WixAssetsTopBanner,
            RuntimeDependencies = runtimeDeps,
            ConclusionMessage = conclusionMessage,
            ReadmeMessage = readmeMessage,
            WelcomeMessage = welcomeMessage,
            LicenseRtfFilePath = licenseRtfPath,
        };
    }

    [SupportedOSPlatform("windows")]
    public static void CompileWixMsi(ILogger Log, MsiTemplateData data, Action<int> progress, string outputFilePath)
    {
        var wixArch = data.IsArm64 ? "arm64" : data.Is64Bit ? "x64" : "x86";
        Log.Info($"Configuring WiX in {wixArch} mode");

        var _1 = TempUtil.GetTempDirectory(out var outputDir);
        var wixId = data.WixId;
        var wxsPath = Path.Combine(outputDir, wixId + ".wxs");
        var localizationPath = Path.Combine(outputDir, wixId + "_en-US.wxs");

        var (wxsContent, localizationContent) = GenerateWixTemplate(data);

        // File.WriteAllText(@"C:\Source\velopack\samples\CSharpAvalonia\releases\test.wxs", wxsContent, Encoding.UTF8);
        File.WriteAllText(wxsPath, wxsContent, Encoding.UTF8);
        File.WriteAllText(localizationPath, localizationContent, Encoding.UTF8);

        progress(30);

        Log.Info("Compiling WiX Template");

        List<string> wixExtensions = [HelperFile.WixUiExtPath];

        //When localization is supported in Velopack, we will need to add -culture here:
        //https://docs.firegiant.com/wix/tools/wixext/wixui/
        var buildCommand =
            $"\"{HelperFile.WixPath}\" build -arch {wixArch} -outputType Package " +
            $"-pdbType none {string.Join(" ", wixExtensions.Select(x => $"-ext \"{x}\""))} -loc \"{localizationPath}\" -out \"{outputFilePath}\" \"{wxsPath}\"";

        _ = Exe.RunHostedCommand(buildCommand);

        progress(100);
    }

    private static string GetResourceContent(string resourceName)
    {
        var assy = Assembly.GetExecutingAssembly();

        string[] manifestResourceNames = assy.GetManifestResourceNames();
        string resourceNameFull = manifestResourceNames.SingleOrDefault(name => name.EndsWith(resourceName));
        if (string.IsNullOrEmpty(resourceNameFull))
            throw new InvalidOperationException(
                $"Resource '{resourceName}' not found in assembly. Available resources: {string.Join(", ", manifestResourceNames)}");

        using var stream = assy.GetManifestResourceStream(resourceNameFull);
        if (stream == null)
            throw new InvalidOperationException($"Resource '{resourceName}' not found in assembly.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}