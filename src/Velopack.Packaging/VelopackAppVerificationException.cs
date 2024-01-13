using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Velopack.Packaging
{
    [ExcludeFromCodeCoverage]
    public class VelopackAppVerificationException : UserInfoException
    {
        public VelopackAppVerificationException(string message)
            : base(
                  $"Failed to verify VelopackApp ({message}). " +
                  $"Ensure you have added the startup code to the beginning of your Program.Main(): VelopackApp.Build().Run(); " +
                  $"and then re-compile/re-publish your application.")
        {
        }
    }
}
