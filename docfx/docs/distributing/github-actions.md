_Applies to: Windows_

# Continuous Deployment

> [!NOTE]
> This page is a work in progress. While there is only information regarding GitHub Actions here currently, please note
that Velopack can be used with many different methods of Continuous Deployment.

## GitHub Actions

This section assumes you have a basic knowledge of GitHub Actions. You can learn more about
creating workflows [here](https://docs.github.com/en/actions/deployment/about-deployments/deploying-with-github-actions).

### Creating the Workflow

The following example assumes you are building for Windows, but you can adapt the workflow for other platforms as well.

First create a workflow in your repository at `.github/workflows` with the `.yml` extension, for example
`.github/workflows/main.yml`. This example workflow will run when code is pushed to the `main` branch. Refer to
documentation linked above if you would prefer a different trigger.

```yml
name: Deploy to GitHub Releases

on:
  push:
    branches:
      - main
```

Create the job that will run when the trigger is activated. This example will run on a `windows-latest` machine as we're
packaging for Windows.

```yml
jobs:
  deploy-to-github-releases:
    runs-on: windows-latest
    steps:
```

### Compiling the Application

First, add a step to checkout your repository to get all the files needed to compile your application.

```yml
      - name: Checkout Repository
        uses: actions/checkout@v4
```

You will need the version number of your release for packing with Velopack. There are many ways to handle this.
If you are using GitHub Action variables to handle this, you can skip this step. This example will extract the
version number from the `<Version>` tag in the `.csproj` of the application. The `bash` shell is defined here
as this command will fail when running on Windows otherwise. The command works by using a regular expression
with `grep` to extract the value between `<Version>` and `</Version>` in the csproj file, and store it in a
variable called `version` in the current run of the workflow.

```yml
      - name: Get Version from Project File
        id: get-version
        shell: bash
        run: echo "version=$(grep -oE '<Version>[^<]+' MyApplication/MyApplication.csproj | sed 's/<Version>//')" >> $GITHUB_OUTPUT
```

Next, add a step to install .NET so the application can be compiled. Set the `dotnet-version` to the version needed by
your application.

```yml
      - name: Install .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
```

Compile your application. This example will do so by publishing the application to a folder in a self-contained manner.
You can publish without the self-contained flag if using Velopack to install such dependencies
(see [Bootstrapping](../packaging/bootstrapping.md) for details). This command uses the `-c` flag to set the build
configuration to `Release` mode, `-o` to set the output directory to `publish`, `-r` to set the runtime
to `win-x64` for distributing on 64-bit Windows, and `--self-contained` to publish the .NET runtime with the
application. Adapt this command to your needs. You can learn more about
`dotnet publish` in the [Microsoft Documentation](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-publish).

```yml
      - name: Publish Application
        run: dotnet publish MyProject/MyProject.csproj -c Release -o publish -r win-x64 --self-contained true
```

### Deploying the Release

Finally, use Velopack to package your application and deploy your release. Create a step that runs several commands
on the commandline.

Let's break down what each line does.

1. Installs the Velopack CLI.
2. Downloads the latest release of your repository. This is needed so that Velopack can create the delta package
between the current release and the new one, as well as populating the releases files.
3. Invokes the Velopack CLI to package your application. The `-v` argument calls upon
the `version` variable assigned earlier, which is accessed using the `id` of the step that assigned it (`get-version`).
`-p` is pointed at the `publish` directory that was used in the previous step. For more information on the Velopack CLI
and which flags are available for the `pack` command, [see here](../packaging/overview.md).
4. Creates a new release in your repository and uploads the necessary files to it automatically.

> [!NOTE]
> If your repository is private, you will need to provide Velopack with an OAuth token when using the `vpk download`
and `vpk upload` commands. Simply append the following to both commands: `--token ${{ secrets.GITHUB_TOKEN }}`.

```yml
      - name: Create Velopack Release
        run: |
          dotnet tool install -g vpk
          vpk download github --repoUrl https://github.com/Myname/Myrepo
          vpk pack -u MyUniqueIdentifier -v ${{ steps.get-version.outputs.version }} -p publish
          vpk upload github --repoUrl https://github.com/Myname/Myrepo --publish --releaseName "MyProject ${{ steps.get-version.outputs.version }}" --tag v${{ steps.get-version.outputs.version }}
```