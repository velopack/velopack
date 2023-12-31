using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Velopack.Packaging
{
    internal interface ICommand<TOpt> where TOpt : class
    {
        Task Run(TOpt options, ILogger logger);
    }
}
