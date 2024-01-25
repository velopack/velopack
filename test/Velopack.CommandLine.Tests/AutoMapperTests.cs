using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Velopack.Vpk;

namespace Velopack.CommandLine.Tests
{
    public class AutoMapperTests
    {
        [Fact]
        public void AutoMapperConfigIsValid()
        {
            CommandMapper.Validate();
        }
    }
}
