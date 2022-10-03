using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;
using Squirrel.CommandLine.Sync;

namespace Squirrel.CommandLine.Deployment
{
    public class S3BaseCommand : BaseCommand
    {
        public Option<string> KeyId { get; }
        public Option<string> Secret { get; }
        public Option<string> Region { get; }
        public Option<string> Endpoint { get; }
        public Option<string> Bucket { get; }
        public Option<string> PathPrefix { get; }

        protected S3BaseCommand(string name, string description)
            : base(name, description)
        {
            KeyId = new Option<string>("--keyId", "Authentication identifier or access key.") {
                ArgumentHelpName = "IDENTIFIER",
                IsRequired = true
            };
            Add(KeyId);

            Secret = new Option<string>("--secret", "Authentication secret key.") {
                ArgumentHelpName = "KEY",
                IsRequired = true
            };
            Add(Secret);

            Region = new Option<string>("--region", "AWS service region (eg. us-west-1).") {
                ArgumentHelpName = "REGION"
            };
            Region.AddValidator(result => {
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
            });
            Add(Region);

            Endpoint = new Option<string>("--endpoint", "Custom service url (backblaze, digital ocean, etc).") {
                ArgumentHelpName = "URL"
            };
            Add(Endpoint);

            Bucket = new Option<string>("--bucket", "Name of the S3 bucket.") {
                ArgumentHelpName = "NAME",
                IsRequired = true
            };
            Add(Bucket);

            PathPrefix = new Option<string>("--pathPrefix", "A sub-folder used for files in the bucket, for creating release channels (eg. 'stable' or 'dev').") {
                ArgumentHelpName = "PREFIX"
            };
            Add(PathPrefix);

            this.AreMutuallyExclusive(Region, Endpoint)
                .AtLeastOneRequired(Region, Endpoint);
        }

        private protected virtual void SetOptionsValues(InvocationContext context, SyncS3Options options)
        {
            base.SetOptionsValues(context, options);
            options.keyId = context.ParseResult.GetValueForOption(KeyId);
            options.secret = context.ParseResult.GetValueForOption(Secret);
            options.region = context.ParseResult.GetValueForOption(Region);
            options.endpoint = context.ParseResult.GetValueForOption(Endpoint);
            options.bucket = context.ParseResult.GetValueForOption(Bucket);
            options.pathPrefix = context.ParseResult.GetValueForOption(PathPrefix);
        }
    }
    
    public class S3DownloadCommand : S3BaseCommand
    {
        public S3DownloadCommand()
            : base("s3", "Download latest release from an S3 bucket.")
        {
            this.SetHandler(Execute);
        }

        private async Task Execute(InvocationContext context)
        {
            SyncS3Options options = new();
            SetOptionsValues(context, options);
            await new S3Repository(options).DownloadRecentPackages();
        }
    }

    public class S3UploadCommand : S3BaseCommand
    {
        public Option<bool> Overwrite { get; }
        public Option<int> KeepMaxReleases { get; }

        public S3UploadCommand()
            : base("s3", "Upload releases to an S3 bucket.")
        {
            Overwrite = new Option<bool>("--overwrite", "Replace remote files if local files have changed.");
            Add(Overwrite);

            KeepMaxReleases = new Option<int>("--keepMaxReleases", "Apply a retention policy which keeps only the specified number of old versions in remote source.") {
                ArgumentHelpName = "NUMBER"
            };
            Add(KeepMaxReleases);

            this.SetHandler(Execute);
        }
        
        private protected override void SetOptionsValues(InvocationContext context, SyncS3Options options)
        {
            base.SetOptionsValues(context, options);
            options.overwrite = context.ParseResult.GetValueForOption(Overwrite);
            options.keepMaxReleases = context.ParseResult.GetValueForOption(KeepMaxReleases);
        }

        private async Task Execute(InvocationContext context)
        {
            SyncS3Options options = new();
            SetOptionsValues(context, options);
            await new S3Repository(options).UploadMissingPackages();
        }
    }
}
