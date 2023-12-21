using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace Squirrel.Locators
{
    /// <summary>
    /// Provides a mock / test implementation of <see cref="SquirrelLocator" />. This can be used to verify that
    /// your application is able to find and prepare updates from your chosen update source without actually
    /// having an installed Squirrel application. This could be used in a CI/CD pipeline, or unit tests etc.
    /// </summary>
    public class TestSquirrelLocator : SquirrelLocator
    {
        /// <inheritdoc />
        public override string AppId { get; }

        /// <inheritdoc />
        public override string RootAppDir { get; }

        /// <inheritdoc />
        public override string PackagesDir { get; }

        /// <inheritdoc />
        public override string AppTempDir => CreateSubDirIfDoesNotExist(PackagesDir, "SquirrelClowdTemp");

        /// <inheritdoc />
        public override string UpdateExePath => throw new NotSupportedException("TestSquirrelLocator does not support this operation.");

        /// <inheritdoc />
        public override SemanticVersion CurrentlyInstalledVersion { get; }

        /// <inheritdoc />
        public override string AppContentDir { get; }

        /// <inheritdoc cref="TestSquirrelLocator" />
        public TestSquirrelLocator(string appId, string version, string packagesDir, ILogger logger = null)
            : this(appId, version, packagesDir, AppContext.BaseDirectory, AppContext.BaseDirectory, logger)
        {
        }

        /// <inheritdoc cref="TestSquirrelLocator" />
        public TestSquirrelLocator(string appId, string version, string packagesDir, string appDir, string rootDir, ILogger logger = null)
            : base(logger)
        {
            AppId = appId;
            PackagesDir = packagesDir;
            CurrentlyInstalledVersion = SemanticVersion.Parse(version);
            RootAppDir = rootDir;
            AppContentDir = appDir;
        }
    }
}
