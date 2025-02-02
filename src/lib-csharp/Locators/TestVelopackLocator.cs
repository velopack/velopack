using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Velopack.Locators
{
    /// <summary>
    /// Provides a mock / test implementation of <see cref="VelopackLocator" />. This can be used to verify that
    /// your application is able to find and prepare updates from your chosen update source without actually
    /// having an installed application. This could be used in a CI/CD pipeline, or unit tests etc.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class TestVelopackLocator : VelopackLocator
    {
        public override string? AppId {
            get {
                if (_id == null) {
                    throw new NotSupportedException("AppId is not supported in this test implementation.");
                }
                return _id;
            }
        }

        public override string? RootAppDir {
            get {
                if (_root == null) {
                    throw new NotSupportedException("RootAppDir is not supported in this test implementation.");
                }
                return _root;
            }
        }

        public override string? PackagesDir {
            get {
                if (_packages == null) {
                    throw new NotSupportedException("PackagesDir is not supported in this test implementation.");
                }
                return _packages;
            }
        }

        public override string? UpdateExePath {
            get {
                if (_updatePath == null) {
                    throw new NotSupportedException("UpdateExePath is not supported in this test implementation.");
                }
                return _updatePath;
            }
        }

        public override SemanticVersion? CurrentlyInstalledVersion {
            get {
                if (_version == null) {
                    throw new NotSupportedException("CurrentlyInstalledVersion is not supported in this test implementation.");
                }
                return _version;
            }
        }

        public override string? AppContentDir {
            get {
                if (_appContent == null) {
                    throw new NotSupportedException("AppContentDir is not supported in this test implementation.");
                }
                return _appContent;
            }
        }

        public override string? Channel {
            get {
                return _channel;
            }
        }

        public override VelopackAsset? GetLatestLocalFullPackage()
        {
            if (_asset != null) {
                return _asset;
            }
            return base.GetLatestLocalFullPackage();
        }

        public override uint ProcessId => 0;
        
        public override string ProcessExePath { get; }

        private readonly string? _updatePath;
        private readonly SemanticVersion? _version;
        private readonly string? _packages;
        private readonly string? _id;
        private readonly string? _root;
        private readonly string? _appContent;
        private readonly string? _channel;
        private readonly VelopackAsset? _asset;

        /// <inheritdoc cref="TestVelopackLocator" />
        public TestVelopackLocator(string appId, string version, string packagesDir, ILogger? logger = null)
            : this(appId, version, packagesDir, null, null, null, null, logger)
        {
        }

        /// <inheritdoc cref="TestVelopackLocator" />
        public TestVelopackLocator(string appId, string version, string packagesDir, string? appDir,
            string? rootDir, string? updateExe, string? channel = null, ILogger? logger = null, VelopackAsset? localPackage = null, string processPath = null!)
            : base(logger)
        {
            _id = appId;
            _packages = packagesDir;
            _version = SemanticVersion.Parse(version);
            _updatePath = updateExe;
            _root = rootDir;
            _appContent = appDir;
            _channel = channel;
            _asset = localPackage;
            ProcessExePath = processPath;
        }
    }
}
