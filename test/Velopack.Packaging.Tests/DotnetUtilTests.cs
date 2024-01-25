using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Velopack.Packaging.Exceptions;
using Velopack.Packaging.Windows;
using Velopack.Packaging.Windows.Commands;

namespace Velopack.Packaging.Tests
{
    public class DotnetUtilTests
    {
        private readonly ITestOutputHelper _output;

        public DotnetUtilTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [SkippableFact]
        public void NonDotnetBinaryPasses()
        {
            Skip.IfNot(VelopackRuntimeInfo.IsWindows);
            using var logger = _output.BuildLoggerFor<DotnetUtilTests>();
            Assert.Null(DotnetUtil.VerifyVelopackApp(PathHelper.GetRustAsset("testapp.exe"), logger));
        }

        [SkippableFact]
        public void PublishSingleFilePasses()
        {
            Skip.IfNot(VelopackRuntimeInfo.IsWindows);
            using var logger = _output.BuildLoggerFor<DotnetUtilTests>();
            using var _1 = Utility.GetTempDirectory(out var dir);
            var sample = PathHelper.GetAvaloniaSample();
            Exe.InvokeAndThrowIfNonZero(
                "dotnet",
                new string[] { "publish", "--no-self-contained", "-r", "win-x64", "-o", dir,
                    "-p:UseLocalVelopack=true", "-p:PublishSingleFile=true" },
                sample);

            var path = Path.Combine(dir, "AvaloniaCrossPlat.exe");
            Assert.Equal(VelopackRuntimeInfo.VelopackProductVersion, DotnetUtil.VerifyVelopackApp(path, logger));

            var newPath = Path.Combine(dir, "AvaloniaCrossPlat-asd2.exe");
            File.Move(path, newPath);
            Assert.Equal(VelopackRuntimeInfo.VelopackProductVersion, DotnetUtil.VerifyVelopackApp(newPath, logger));
        }

        [SkippableFact]
        public void PublishDotnet6Passes()
        {
            Skip.IfNot(VelopackRuntimeInfo.IsWindows);
            using var logger = _output.BuildLoggerFor<DotnetUtilTests>();
            using var _1 = Utility.GetTempDirectory(out var dir);
            var sample = PathHelper.GetAvaloniaSample();
            Exe.InvokeAndThrowIfNonZero(
                "dotnet",
                new string[] { "publish", "--no-self-contained", "-r", "win-x64", "-o", dir,
                    "-p:UseLocalVelopack=true" },
                sample);

            var path = Path.Combine(dir, "AvaloniaCrossPlat.exe");
            Assert.Equal(VelopackRuntimeInfo.VelopackProductVersion, DotnetUtil.VerifyVelopackApp(path, logger));

            var newPath = Path.Combine(dir, "AvaloniaCrossPlat-asd2.exe");
            File.Move(path, newPath);
            Assert.Equal(VelopackRuntimeInfo.VelopackProductVersion, DotnetUtil.VerifyVelopackApp(newPath, logger));
        }

        [SkippableFact]
        public void PublishNet48Passes()
        {
            Skip.IfNot(VelopackRuntimeInfo.IsWindows);
            using var logger = _output.BuildLoggerFor<DotnetUtilTests>();
            using var _1 = Utility.GetTempDirectory(out var dir);
            var sample = PathHelper.GetWpfSample();
            Exe.InvokeAndThrowIfNonZero(
                "dotnet",
                new string[] { "publish", "-o", dir },
                sample);

            var path = Path.Combine(dir, "VeloWpfSample.exe");
            Assert.NotNull(DotnetUtil.VerifyVelopackApp(path, logger));

            var newPath = Path.Combine(dir, "VeloWpfSample-asd2.exe");
            File.Move(path, newPath);
            Assert.NotNull(DotnetUtil.VerifyVelopackApp(newPath, logger));
        }

        [SkippableFact]
        public void UnawareDotnetAppFails()
        {
            Skip.IfNot(VelopackRuntimeInfo.IsWindows);
            using var logger = _output.BuildLoggerFor<DotnetUtilTests>();
            using var _1 = Utility.GetTempDirectory(out var dir);
            var sample = PathHelper.GetTestRootPath("TestApp");
            Exe.InvokeAndThrowIfNonZero(
                "dotnet",
                new string[] { "publish", "--no-self-contained", "-r", "win-x64", "-o", dir,
                    "-p:NoVelopackApp=true" },
                sample);

            var path = Path.Combine(dir, "TestApp.exe");
            Assert.Throws<UserInfoException>(() => DotnetUtil.VerifyVelopackApp(path, logger));
        }
    }
}
