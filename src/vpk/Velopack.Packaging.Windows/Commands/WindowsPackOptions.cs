using Velopack.Packaging.Abstractions;

namespace Velopack.Packaging.Windows.Commands;

public class WindowsPackOptions : WindowsReleasifyOptions, INugetPackCommand, IPackOptions
{
    public string PackId { get; set; }

    public string PackVersion { get; set; }

    public string PackDirectory { get; set; }

    public string PackAuthors { get; set; }

    public string PackTitle { get; set; }

    public string ReleaseNotes { get; set; }

    public string Exclude { get; set; }

    public bool NoPortable { get; set; }

    public bool NoInst { get; set; }

    public string Shortcuts { get; set; }

    public string InstWelcome { get; set; }

    public string InstReadme { get; set; }

    public string InstLicense { get; set; }
    public string InstLicenseRtf { get; set; }

    public string InstConclusion { get; set; }

    public string MsiBanner { get; set; }
    public string MsiLogo { get; set; }

    public bool BuildMsi { get; set; }
    public bool BuildMsiDeploymentTool { get; set; }

    public string MsiVersionOverride { get; set; }
}
