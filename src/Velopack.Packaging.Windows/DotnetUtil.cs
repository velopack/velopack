using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Bundles;
using AsmResolver.PE;

namespace Velopack.Packaging.Windows
{
    public class DotnetUtil
    {
        public static void VerifyVelopackAppBuilder(string exeFile)
        {
            // if it is a full-framework binary:

            //AssemblyDefinition mainAssy;

            //try {
            //    mainAssy = AssemblyDefinition.FromFile(exeFile);
            //} catch (BadImageFormatException) {
            //    // not a .Net Framework binary
            //}

            //var bundle = BundleManifest.FromFile(exeFile);

            //var peImage1 = PEImage.FromFile(@"C:\Source\velopack\examples\AvaloniaCrossPlat\publish\AvaloniaCrossPlat-asd.exe");
            //var peImage2 = PEImage.FromFile(@"C:\Source\velopack\src\Rust\target\debug\testawareapp.exe");



            //var assembly1 = AssemblyDefinition.FromFile(@"C:\Source\velopack\examples\AvaloniaCrossPlat\publish\AvaloniaCrossPlat-asd.exe");
            //var assembly2 = AssemblyDefinition.FromFile(@"C:\Source\velopack\src\Rust\target\debug\testawareapp.exe");
            ////assembly.Modules
            //Console.WriteLine();
        }
    }
}
