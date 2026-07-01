# Python source-test harness for Velopack.Deployment.Tests.
#
# Invocation contract (shared by every language harness):
#   python harness.py <config.json>
# The result JSON is written to stdout as the LAST line; all log noise goes to stderr.
# Exit 0 on success, exit 1 on failure (still emitting {"ok": false, "error": ...} as the last stdout line).
#
# Requirements: stdlib only (json, hashlib, os, sys) plus the `velopack` extension module, which the
# HarnessRunner installs into a venv via `maturin develop --manifest-path src/lib-python/Cargo.toml`.
#
# Config schema:
#   { "source":  { "kind": "file|http|gitea|gitlab|github", "url": "...", "token": null, "prerelease": false },
#     "locator": { PascalCase VelopackLocatorConfig fields },
#     "channel": "stable",
#     "action":  "check" | "download",
#     "downloadDir": "..." }
# Downloads always land in locator.PackagesDir (the binding ignores downloadDir); we report the real path.
#
# Note: the Python binding does not expose an explicit FileSource; a local directory path string is
# passed directly to UpdateManager, which auto-detects it as a file source.

import hashlib
import json
import os
import sys

import velopack


def build_source(s):
    kind = s["kind"]
    url = s["url"]
    token = s.get("token")
    prerelease = bool(s.get("prerelease", False))
    if kind == "file":
        return url  # auto-detected as a FileSource by UpdateManager
    if kind == "http":
        return velopack.HttpSource(url)
    if kind == "github":
        return velopack.GithubSource(url, token, prerelease)
    if kind == "gitlab":
        return velopack.GitlabSource(url, token, prerelease)
    if kind == "gitea":
        return velopack.GiteaSource(url, token, prerelease)
    raise ValueError("unknown source kind: " + str(kind))


def sha256_upper(path):
    with open(path, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest().upper()


def run(config):
    loc = config["locator"]
    locator = velopack.VelopackLocatorConfig(
        loc["RootAppDir"],
        loc["UpdateExePath"],
        loc["PackagesDir"],
        loc["ManifestPath"],
        loc["CurrentBinaryDir"],
        bool(loc["IsPortable"]),
    )
    options = velopack.UpdateOptions(False, 10, config["channel"])
    source = build_source(config["source"])

    um = velopack.UpdateManager(source, options, locator)
    info = um.check_for_updates()

    result = {
        "ok": True,
        "updateAvailable": info is not None,
        "targetVersion": info.TargetFullRelease.Version if info is not None else None,
    }

    if config.get("action") == "download":
        if info is None:
            raise RuntimeError("no update available to download")
        um.download_updates(info)
        file_name = info.TargetFullRelease.FileName
        downloaded = os.path.join(loc["PackagesDir"], file_name)
        if not os.path.exists(downloaded):
            raise RuntimeError("expected downloaded file not found: " + downloaded)
        result["downloadedFile"] = downloaded
        result["sha256"] = sha256_upper(downloaded)

    return result


def main():
    if len(sys.argv) < 2:
        raise SystemExit("usage: python harness.py <config.json>")
    with open(sys.argv[1], "r", encoding="utf-8") as f:
        config = json.load(f)
    result = run(config)
    sys.stdout.write(json.dumps(result) + "\n")


if __name__ == "__main__":
    try:
        main()
    except Exception as err:  # noqa: BLE001 - harness must report any failure as JSON
        import traceback

        message = "".join(traceback.format_exception(type(err), err, err.__traceback__))
        sys.stderr.write(message)
        sys.stdout.write(json.dumps({"ok": False, "error": message}) + "\n")
        sys.exit(1)
