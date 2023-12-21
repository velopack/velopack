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
        public override string RootAppDir => AppContext.BaseDirectory;

        /// <inheritdoc />
        public override string PackagesDir { get; }

        /// <inheritdoc />
        public override string AppTempDir => CreateSubDirIfDoesNotExist(PackagesDir, "SquirrelClowdTemp");

        /// <inheritdoc />
        public override string UpdateExePath => throw new NotSupportedException("TestSquirrelLocator does not support this operation.");

        /// <inheritdoc />
        public override SemanticVersion CurrentlyInstalledVersion => new SemanticVersion(0, 0, 0);

        /// <inheritdoc />
        public override string AppContentDir => AppContext.BaseDirectory;

        /// <inheritdoc cref="TestSquirrelLocator" />
        public TestSquirrelLocator(string appId, string packagesDir, ILogger logger)
            : base(logger)
        {
            AppId = appId;
            PackagesDir = packagesDir;
        }
    }
}
