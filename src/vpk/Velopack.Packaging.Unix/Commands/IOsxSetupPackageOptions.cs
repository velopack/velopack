using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Velopack.Packaging.Unix.Commands;
public interface IOsxSetupPackageOptions
{
    public string PackId { get; }

    public string PackTitle { get; }

    public string InstWelcome { get; }

    public string InstReadme { get; }

    public string InstLicense { get; }

    public string InstConclusion { get; }

    public string SignInstallIdentity { get; }

    public string NotaryProfile { get; }

    public string Keychain { get; }
}
