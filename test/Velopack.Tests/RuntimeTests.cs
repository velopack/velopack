using System.Net.Http;
using Velopack.Windows;

namespace Velopack.Tests
{
    public class RuntimeTests
    {
        [Theory]
        [InlineData("net6", "net6-x64-desktop")]
        [InlineData("net6.0", "net6-x64-desktop")]
        [InlineData("net6-x64", "net6-x64-desktop")]
        [InlineData("net6-x86", "net6-x86-desktop")]
        [InlineData("net3.1", "netcoreapp3.1-x64-desktop")]
        [InlineData("netcoreapp3.1", "netcoreapp3.1-x64-desktop")]
        [InlineData("net3.1-x86", "netcoreapp3.1-x86-desktop")]
        [InlineData("net6.0.2", "net6.0.2-x64-desktop")]
        [InlineData("net6.0.2-x86", "net6.0.2-x86-desktop")]
        [InlineData("net6.0.1-x86", "net6.0.1-x86-desktop")]
        [InlineData("net6.0.0", "net6-x64-desktop")]
        [InlineData("net6.0-x64-desktop", "net6-x64-desktop")]
        [InlineData("net7.0-x64-runtime", "net7-x64-runtime")]
        [InlineData("net7.0-x64-asp", "net7-x64-asp")]
        [InlineData("net7.0-desktop", "net7-x64-desktop")]
        [InlineData("net7.0-runtime", "net7-x64-runtime")]
        public void DotnetParsesValidVersions(string input, string expected)
        {
            var p = Runtimes.DotnetInfo.Parse(input);
            Assert.Equal(expected, p.Id);
        }

        [Theory]
        [InlineData("net3.2")]
        [InlineData("net4.9")]
        [InlineData("net6.0.0.4")]
        [InlineData("net7.0-x64-base")]
        [InlineData("net6-basd")]
        [InlineData("net6-x64-aakaka")]
        public void DotnetParseThrowsInvalidVersion(string input)
        {
            Assert.ThrowsAny<Exception>(() => Runtimes.DotnetInfo.Parse(input));
        }

        [Theory]
        [InlineData("net6", true)]
        [InlineData("net20.0", true)]
        [InlineData("net5.0.14-x86", true)]
        [InlineData("netcoreapp3.1-x86", true)]
        [InlineData("net48", true)]
        [InlineData("netcoreapp4.8", false)]
        [InlineData("net4.8", false)]
        [InlineData("net2.5", false)]
        [InlineData("vcredist110-x64", true)]
        [InlineData("vcredist110-x86", true)]
        [InlineData("vcredist110", true)]
        [InlineData("vcredist143", true)]
        [InlineData("asd", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("net6-x64", true)]
        [InlineData("net6-x64-runtime", true)]
        [InlineData("net6-x64-desktop", true)]
        public void GetRuntimeTests(string input, bool expected)
        {
            var dn = Runtimes.GetRuntimeByName(input);
            Assert.Equal(expected, dn != null);
        }

        [Theory(Skip = "Only run when needed")]
        [InlineData("3.1", RuntimeCpu.x86, Runtimes.DotnetRuntimeType.WindowsDesktop)]
        [InlineData("3.1", RuntimeCpu.x86, Runtimes.DotnetRuntimeType.Runtime)]
        [InlineData("3.1", RuntimeCpu.x86, Runtimes.DotnetRuntimeType.AspNetCore)]
        [InlineData("3.1", RuntimeCpu.x64, Runtimes.DotnetRuntimeType.WindowsDesktop)]
        [InlineData("3.1", RuntimeCpu.x64, Runtimes.DotnetRuntimeType.Runtime)]
        [InlineData("3.1", RuntimeCpu.x64, Runtimes.DotnetRuntimeType.AspNetCore)]
        [InlineData("5.0", RuntimeCpu.x86, Runtimes.DotnetRuntimeType.WindowsDesktop)]
        [InlineData("5.0", RuntimeCpu.x86, Runtimes.DotnetRuntimeType.Runtime)]
        [InlineData("5.0", RuntimeCpu.x86, Runtimes.DotnetRuntimeType.AspNetCore)]
        [InlineData("5.0", RuntimeCpu.x64, Runtimes.DotnetRuntimeType.WindowsDesktop)]
        [InlineData("5.0", RuntimeCpu.x64, Runtimes.DotnetRuntimeType.Runtime)]
        [InlineData("5.0", RuntimeCpu.x64, Runtimes.DotnetRuntimeType.AspNetCore)]
        [InlineData("7.0", RuntimeCpu.x86, Runtimes.DotnetRuntimeType.WindowsDesktop)]
        [InlineData("7.0", RuntimeCpu.x86, Runtimes.DotnetRuntimeType.Runtime)]
        [InlineData("7.0", RuntimeCpu.x86, Runtimes.DotnetRuntimeType.AspNetCore)]
        [InlineData("7.0", RuntimeCpu.x64, Runtimes.DotnetRuntimeType.WindowsDesktop)]
        [InlineData("7.0", RuntimeCpu.x64, Runtimes.DotnetRuntimeType.Runtime)]
        [InlineData("7.0", RuntimeCpu.x64, Runtimes.DotnetRuntimeType.AspNetCore)]
        public async Task MicrosoftReturnsValidDotnetDownload(string minversion, RuntimeCpu architecture, Runtimes.DotnetRuntimeType runtimeType)
        {
            var dni = new Runtimes.DotnetInfo(minversion, architecture, runtimeType);
            var url = await dni.GetDownloadUrl();

            Assert.Contains(minversion, url, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(architecture.ToString(), url, StringComparison.OrdinalIgnoreCase);

            if (runtimeType == Runtimes.DotnetRuntimeType.Runtime)
                Assert.Matches(@"/dotnet-runtime-\d", url);
            else if (runtimeType == Runtimes.DotnetRuntimeType.AspNetCore)
                Assert.Matches(@"/aspnetcore-runtime-\d", url);
            else if (runtimeType == Runtimes.DotnetRuntimeType.WindowsDesktop)
                Assert.Matches(@"/windowsdesktop-runtime-\d", url);

            using var hc = new HttpClient();
            var result = await hc.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            result.EnsureSuccessStatusCode();
        }
    }
}