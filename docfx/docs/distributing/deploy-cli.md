*Applies to: Windows, MacOS, Linux*

# Deployment CLI
The general process for deploying a Velopack release (`download -> pack -> upload`) can be greatly simplified by using the `download` and `upload` commands which are built into the `vpk` command line tool.

## Packing your new release with delta's

In order for delta's to be generated during the `pack` command, you need to first download the current latest release. This should be done with the download command:

```cmd
vpk download http --url https://the.place/you-host/updates
vpk pack -u YourAppId -v 1.0.1 -p {buildOutput}
```

There are providers for various sources, such GitHub, S3, HTTP, etc.

## Deploying releases
In the previous example, we used the `http` source, while that is very generic it does not provide any information about how to upload the releases, so in the following deployment example we will use [AWS S3](https://aws.amazon.com/s3/). 

> [!TIP]
> Most cloud storage providers today have an S3-compatible API ([GCP](https://cloud.google.com/storage/docs/interoperability), [BackBlaze B2](https://www.backblaze.com/docs/cloud-storage-s3-compatible-api), [DigitalOcean](https://docs.digitalocean.com/products/spaces/how-to/use-aws-sdks/), [Linode](https://www.linode.com/docs/products/storage/object-storage/), [IBM Cloud](https://cloud.ibm.com/docs/cloud-object-storage?topic=cloud-object-storage-compatibility-api), and so forth) and can be used with this command - it is not limited to AWS.

Using AWS, you can [authenticate using the `aws` command line tool](https://docs.aws.amazon.com/sdk-for-net/v3/developer-guide/creds-idc.html) or you can provide access keys as below. 

If you are using AWS SSO, you should check the [AWS CLI SSO](https://aws.amazon.com/blogs/security/aws-single-sign-on-now-enables-command-line-interface-access-for-aws-accounts-using-corporate-credentials/) doc and [AWS session authentication](https://docs.aws.amazon.com/STS/latest/APIReference/API_GetSessionToken.html).

```cmd
vpk download s3 --bucket MyApp --region us-west-1 --keyId {accessKeyId} --secret {accessKeySecret}
vpk pack -u YourAppId -v 1.0.1 -p {buildOutput}
vpk upload s3 --bucket MyApp --region us-west-1 --keyId {accessKeyId} --secret {accessKeySecret} 
```

Note that you can specify most of these argumentsas environment variables too. You can review the [AWS SDK environment variables here](https://docs.aws.amazon.com/cli/latest/userguide/cli-configure-envvars.html) and every `vpk` option can be provided as an environment variable too, to list these in the help text use `vpk -H` instead of `vpk -h`. 

When using a non-AWS S3-compatible API (eg. BackBlaze B2), you need to specify an endpoint instead of a region:

```cmd
vpk download s3 --bucket MyApp --endpoint https://s3.eu-central-003.backblazeb2.com --keyId {accessKeyId} --secret {accessKeySecret}
vpk pack -u YourAppId -v 1.0.1 -p {buildOutput}
vpk upload s3 --bucket MyApp --endpoint https://s3.eu-central-003.backblazeb2.com --keyId {accessKeyId} --secret {accessKeySecret} 
```
