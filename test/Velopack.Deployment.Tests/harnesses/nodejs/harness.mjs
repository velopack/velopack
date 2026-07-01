// Node.js source-test harness for Velopack.Deployment.Tests.
//
// Invocation contract (shared by every language harness):
//   node harness.mjs <config.json>
// The result JSON is written to stdout as the LAST line; all log noise goes to stderr.
// Exit 0 on success, exit 1 on failure (still emitting {ok:false,error:...} as the last stdout line).
//
// Config schema:
//   { "source":  { "kind":"file|http|gitea|gitlab|github", "url":"...", "token":null, "prerelease":false },
//     "locator": { PascalCase VelopackLocatorConfig fields },
//     "channel": "stable",
//     "action":  "check" | "download",
//     "downloadDir": "..." }
// Downloads always land in locator.PackagesDir (the binding ignores downloadDir); we report the real path.

import { readFileSync, existsSync } from "node:fs";
import { createHash } from "node:crypto";
import { join } from "node:path";
import { FileSource, HttpSource, GithubSource, GitlabSource, GiteaSource, UpdateManager } from "velopack";

function buildSource(s) {
    const token = s.token ?? undefined;
    const prerelease = s.prerelease ?? false;
    switch (s.kind) {
        case "file":
            return new FileSource(s.url);
        case "http":
            return new HttpSource(s.url);
        case "github":
            return new GithubSource(s.url, token, prerelease);
        case "gitlab":
            return new GitlabSource(s.url, token, prerelease);
        case "gitea":
            return new GiteaSource(s.url, token, prerelease);
        default:
            throw new Error(`unknown source kind: ${s.kind}`);
    }
}

function sha256Upper(file) {
    const hash = createHash("sha256");
    hash.update(readFileSync(file));
    return hash.digest("hex").toUpperCase();
}

async function main() {
    const configPath = process.argv[2];
    if (!configPath) {
        throw new Error("usage: node harness.mjs <config.json>");
    }

    const config = JSON.parse(readFileSync(configPath, "utf-8"));
    const source = buildSource(config.source);
    const options = {
        ExplicitChannel: config.channel,
        AllowVersionDowngrade: false,
        MaximumDeltasBeforeFallback: 10,
    };
    const locator = config.locator;

    const um = new UpdateManager(source, options, locator);
    const update = await um.checkForUpdatesAsync();

    const result = {
        ok: true,
        updateAvailable: !!update,
        targetVersion: update?.TargetFullRelease?.Version ?? null,
    };

    if (config.action === "download") {
        if (!update) {
            throw new Error("no update available to download");
        }
        await um.downloadUpdateAsync(update, () => {});
        const fileName = update.TargetFullRelease.FileName;
        const downloadedFile = join(locator.PackagesDir, fileName);
        if (!existsSync(downloadedFile)) {
            throw new Error(`expected downloaded file not found: ${downloadedFile}`);
        }
        result.downloadedFile = downloadedFile;
        result.sha256 = sha256Upper(downloadedFile);
    }

    process.stdout.write(JSON.stringify(result) + "\n");
}

main().catch((err) => {
    const message = err && err.stack ? err.stack : String(err);
    console.error(message);
    process.stdout.write(JSON.stringify({ ok: false, error: message }) + "\n");
    process.exit(1);
});
