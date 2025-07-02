using System.Globalization;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using HandlebarsDotNet;
using Markdig;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Velopack.Core;
using Velopack.Packaging.Rtf;
using Velopack.Packaging.Windows.Commands;
using Velopack.Util;
using Velopack.Windows;

namespace Velopack.Packaging.Windows.Msi;

public static class MsiBuilder
{
    public static (string mainTemplate, string enLocale) GenerateWixTemplate(MsiTemplateData data)
    {
        if (data is null)
            throw new ArgumentNullException(nameof(data));

        var templateContent = GetResourceContent("MsiTemplate.hbs");
        var localeContent = GetResourceContent("MsiLocale_en_US.hbs");

        var template = Handlebars.Compile(templateContent);
        var locale = Handlebars.Compile(localeContent);

        return (template(data), locale(data));
    }

    private static string GetPlainTextMessage(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return "";

        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found", filePath);

        var extension = Path.GetExtension(filePath);
        var content = File.ReadAllText(filePath, Encoding.UTF8);

        // if extension is .md render it to plain text
        if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase)) {
            content = Markdown.ToPlainText(content);
        } else if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)) {
            // do nothing but it's valid
        } else {
            throw new ArgumentException("Installer plain-text messages must be .md or .txt", nameof(filePath));
        }

        return FormatXmlMessage(content);
    }

    private static string GetLicenseRtfPath(string licensePath, DirectoryInfo tempDir)
    {
        if (string.IsNullOrWhiteSpace(licensePath))
            return "";

        if (!File.Exists(licensePath))
            throw new FileNotFoundException("File not found", licensePath);

        var extension = Path.GetExtension(licensePath);
        var content = File.ReadAllText(licensePath, Encoding.UTF8);

        // if extension is .md, render it to rtf
        if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)) {
            licensePath = Path.Combine(tempDir.FullName, "rendered_license.rtf");
            using var writer = new StreamWriter(licensePath);
            var renderer = new RtfRenderer(writer);
            renderer.WriteRtfStart();
            _ = Markdown.Convert(content, renderer);
            renderer.WriteRtfEnd();
        } else if (extension.Equals(".rtf", StringComparison.OrdinalIgnoreCase)) {
            // do nothing but it's valid
        } else {
            throw new ArgumentException("Installer license must be .txt, .md, or .rtf", nameof(licensePath));
        }

        return licensePath;
    }

    public static string SanitizeDirectoryString(string name)
        => string.Join("_", name.Split(Path.GetInvalidPathChars()));

    public static string FormatXmlMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "";

        StringBuilder sb = new();
        XmlWriterSettings settings = new() {
            ConformanceLevel = ConformanceLevel.Fragment,
            NewLineHandling = NewLineHandling.None,
        };
        using XmlWriter writer = XmlWriter.Create(sb, settings);
        writer.WriteString(message);
        writer.Flush();
        var rv = sb.ToString();
        rv = rv.Replace("\r", "&#10;").Replace("\n", "&#13;");
        return rv;
    }

    public static MsiTemplateData ConvertOptionsToTemplateData(DirectoryInfo portableDir, ShortcutLocation shortcuts,
        string runtimeDeps, WindowsPackOptions options)
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
            InstallForAllUsers = options.InstLocation.HasFlag(InstallLocation.PerMachine),
            InstallForCurrentUser = options.InstLocation.HasFlag(InstallLocation.PerUser),
            UpgradeCodeGuid = GuidUtil.CreateGuidFromHash($"{options.PackId}:UpgradeCode").ToString(),
            ComponentGenerationSeedGuid = GuidUtil.CreateGuidFromHash($"{options.PackId}:INSTALLFOLDER").ToString(),
            IconPath = options.Icon,
            StubFileName = (options.PackTitle ?? options.PackId) + ".exe",
            DesktopShortcut = shortcuts.HasFlag(ShortcutLocation.Desktop),
            StartMenuShortcut = shortcuts.HasFlag(ShortcutLocation.StartMenu),
            StartMenuRootShortcut = shortcuts.HasFlag(ShortcutLocation.StartMenuRoot),
            RustNativeModulePath = HelperFile.GetWixNativeModulePath(options.TargetRuntime),
            SideBannerImagePath = options.MsiBanner ?? HelperFile.WixAssetsDialogBackground,
            TopBannerImagePath = options.MsiLogo ?? HelperFile.WixAssetsTopBanner,
            RuntimeDependencies = runtimeDeps,
            ConclusionMessage = GetPlainTextMessage(options.InstConclusion),
            ReadmeMessage = GetPlainTextMessage(options.InstReadme),
            WelcomeMessage = GetPlainTextMessage(options.InstWelcome),
            LicenseRtfFilePath = GetLicenseRtfPath(options.InstLicense, portableDir.Parent),
        };
    }

    [SupportedOSPlatform("windows")]
    public static void CompileWixMsi(ILogger Log, MsiTemplateData data, Action<int> progress, string outputFilePath)
    {
        var wixArch = data.IsArm64 ? "arm64" : data.Is64Bit ? "x64" : "x86";
        Log.Info($"Configuring WiX in {wixArch} mode");

        using var _1 = TempUtil.GetTempDirectory(out var outputDir);
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