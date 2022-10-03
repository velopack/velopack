using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Threading.Tasks;

namespace Squirrel.CommandLine.Commands
{
    public class S3BaseCommand : BaseCommand
    {
        public string KeyId { get; private set; }

        public string Secret { get; private set; }

        public string Region { get; private set; }

        public string Endpoint { get; private set; }

        public string Bucket { get; private set; }

        public string PathPrefix { get; private set; }

        protected S3BaseCommand(string name, string description)
            : base(name, description)
        {
            AddOption<string>("--keyId", (v) => KeyId = v)
                .SetDescription("Authentication identifier or access key.")
                .SetArgumentHelpName("IDENTIFIER")
                .SetRequired();

            AddOption<string>("--secret", (v) => Secret = v)
                .SetDescription("Authentication secret key.")
                .SetArgumentHelpName("KEY")
                .SetRequired();

            var region = AddOption<string>("--region", (v) => Region = v)
                .SetDescription("AWS service region (eg. us-west-1).")
                .SetArgumentHelpName("REGION");

            region.AddValidator(MustBeValidAwsRegion);

            var endpoint = AddOption<string>("--endpoint", (v) => Endpoint = v)
                .SetDescription("Custom service url (backblaze, digital ocean, etc).")
                .SetArgumentHelpName("URL");

            this.AreMutuallyExclusive(region, endpoint);
            this.AtLeastOneRequired(region, endpoint);

            AddOption<string>("--bucket", (v) => Bucket = v)
                .SetDescription("Name of the S3 bucket.")
                .SetArgumentHelpName("NAME")
                .SetRequired();

            AddOption<string>("--pathPrefix", (v) => PathPrefix = v)
                .SetDescription("A sub-folder used for files in the bucket, for creating release channels (eg. 'stable' or 'dev').")
                .SetArgumentHelpName("PREFIX");
        }

        protected static void MustBeValidAwsRegion(OptionResult result)
        {
            for (var i = 0; i < result.Tokens.Count; i++) {
                var region = result.Tokens[i].Value;
                if (!string.IsNullOrWhiteSpace(region)) {
                    var r = Amazon.RegionEndpoint.GetBySystemName(result.Tokens[0].Value);
                    if (r is null || r.DisplayName == "Unknown") {
                        result.ErrorMessage = $"Region '{region}' lookup failed, is this a valid AWS region?";
                    }
                } else {
                    result.ErrorMessage = "A region value is required";
                }
            }
        }
    }

    public class S3DownloadCommand : S3BaseCommand
    {
        public S3DownloadCommand()
            : base("s3", "Download latest release from an S3 bucket.")
        {
        }
    }

    public class S3UploadCommand : S3BaseCommand
    {
        public bool Overwrite { get; private set; }

        public int KeepMaxReleases { get; private set; }

        public S3UploadCommand()
            : base("s3", "Upload releases to a S3 bucket.")
        {
            AddOption<bool>("--overwrite", (v) => Overwrite = v)
                .SetDescription("Replace remote files if local files have changed.");

            AddOption<int>("--keepMaxReleases", (v) => KeepMaxReleases = v)
                .SetDescription("Apply a retention policy which keeps only the specified number of old versions in remote source.")
                .SetArgumentHelpName("NUMBER");
        }
    }
}
