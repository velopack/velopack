import argparse
import json
import os
import sys
import traceback


def asset_to_dict(asset):
    return {
        "PackageId": asset.PackageId,
        "Version": asset.Version,
        "FileName": asset.FileName,
        "Type": asset.Type,
        "SHA1": asset.SHA1,
        "SHA256": asset.SHA256,
        "Size": asset.Size,
    }


def main():
    parser = argparse.ArgumentParser(description="Velopack update source test harness")
    parser.add_argument("source_type", choices=["gitea", "gitlab", "http", "file"])
    parser.add_argument("url_or_path")
    parser.add_argument("token", nargs="?", default="")
    parser.add_argument("--channel", required=True)
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--packages-dir", required=True)
    args = parser.parse_args()

    try:
        import velopack
    except ImportError:
        print("Error: velopack module is not installed.", file=sys.stderr)
        sys.exit(1)

    token_val = args.token if args.token else None

    if args.source_type == "gitea":
        source = velopack.GiteaSource(args.url_or_path, access_token=token_val, prerelease=False)
    elif args.source_type == "gitlab":
        source = velopack.GitlabSource(args.url_or_path, access_token=token_val, prerelease=False)
    elif args.source_type == "http":
        source = velopack.HttpSource(args.url_or_path)
    elif args.source_type == "file":
        # Python doesn't expose FileSource directly - pass path string to
        # UpdateManager which auto-detects it as FileSource via AutoSource
        source = args.url_or_path

    locator = velopack.VelopackLocatorConfig(
        RootAppDir=args.packages_dir,
        UpdateExePath=sys.executable,
        PackagesDir=args.packages_dir,
        ManifestPath=args.manifest,
        CurrentBinaryDir=os.path.dirname(args.manifest),
        IsPortable=True,
    )

    options = velopack.UpdateOptions(
        AllowVersionDowngrade=False,
        MaximumDeltasBeforeFallback=10,
        ExplicitChannel=args.channel,
    )

    um = velopack.UpdateManager(source, options=options, locator=locator)
    info = um.check_for_updates()

    if info is not None:
        target = asset_to_dict(info.TargetFullRelease)
    else:
        target = None

    output = {
        "target": target,
        "feed": None,
    }

    print(json.dumps(output))


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception:
        traceback.print_exc()
        sys.exit(1)
