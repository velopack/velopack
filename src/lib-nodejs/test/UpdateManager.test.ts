import { copyFileSync, existsSync, readFileSync } from "fs";
import http from "http";
import { AddressInfo } from "net";
import { FileSource, HttpSource, UpdateManager, UpdateOptions, VelopackApp, VelopackLocatorConfig } from "../src";
import path from "path";
import { tempd3, fixture, updateExe } from "./helper";

test("UpdateManager detects local update", async () => {
  await tempd3(async (tmpDir, packagesDir, rootDir) => {
    const locator: VelopackLocatorConfig = {
      ManifestPath: "../../test/fixtures/Test.Squirrel-App.nuspec",
      PackagesDir: packagesDir,
      RootAppDir: rootDir,
      UpdateExePath: updateExe(),
      CurrentBinaryDir: path.join(rootDir, "current"),
      IsPortable: true,
    };

    const options: UpdateOptions = {
      ExplicitChannel: "beta",
      AllowVersionDowngrade: false,
      MaximumDeltasBeforeFallback: 10,
    };

    const um = new UpdateManager(tmpDir, options, locator);
    copyFileSync(fixture("testfeed.json"), path.join(tmpDir, "releases.beta.json"));
    const update = await um.checkForUpdatesAsync();

    expect(update).not.toBeNull();
    expect(update?.TargetFullRelease).not.toBeNull();
    expect(update?.TargetFullRelease?.Version).toBe("1.0.11");
    expect(update?.TargetFullRelease?.FileName).toBe("AvaloniaCrossPlat-1.0.11-full.nupkg");
  });
});

test("UpdateManager detects update from explicit FileSource", async () => {
  await tempd3(async (tmpDir, packagesDir, rootDir) => {
    const locator: VelopackLocatorConfig = {
      ManifestPath: "../../test/fixtures/Test.Squirrel-App.nuspec",
      PackagesDir: packagesDir,
      RootAppDir: rootDir,
      UpdateExePath: updateExe(),
      CurrentBinaryDir: path.join(rootDir, "current"),
      IsPortable: true,
    };

    const options: UpdateOptions = {
      ExplicitChannel: "beta",
      AllowVersionDowngrade: false,
      MaximumDeltasBeforeFallback: 10,
    };

    const um = new UpdateManager(new FileSource(tmpDir), options, locator);
    copyFileSync(fixture("testfeed.json"), path.join(tmpDir, "releases.beta.json"));
    const update = await um.checkForUpdatesAsync();

    expect(update?.TargetFullRelease?.Version).toBe("1.0.11");
  });
});

test("UpdateManager detects update from HttpSource with custom headers", async () => {
  await tempd3(async (tmpDir, packagesDir, rootDir) => {
    const locator: VelopackLocatorConfig = {
      ManifestPath: "../../test/fixtures/Test.Squirrel-App.nuspec",
      PackagesDir: packagesDir,
      RootAppDir: rootDir,
      UpdateExePath: updateExe(),
      CurrentBinaryDir: path.join(rootDir, "current"),
      IsPortable: true,
    };

    const options: UpdateOptions = {
      ExplicitChannel: "beta",
      AllowVersionDowngrade: false,
      MaximumDeltasBeforeFallback: 10,
    };

    const feedJson = readFileSync(fixture("testfeed.json"), "utf-8");
    let receivedAuth: string | undefined;
    const server = http.createServer((req, res) => {
      receivedAuth = req.headers["authorization"];
      if (req.url?.includes("releases.beta.json")) {
        res.writeHead(200, { "Content-Type": "application/json" });
        res.end(feedJson);
      } else {
        res.writeHead(404);
        res.end();
      }
    });
    await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
    const port = (server.address() as AddressInfo).port;

    try {
      const source = new HttpSource(`http://127.0.0.1:${port}/`, {
        Headers: [{ Name: "Authorization", Value: "Bearer test123" }],
        TimeoutMilliseconds: 30000,
      });
      const um = new UpdateManager(source, options, locator);
      const update = await um.checkForUpdatesAsync();

      expect(update?.TargetFullRelease?.Version).toBe("1.0.11");
      expect(receivedAuth).toBe("Bearer test123");
    } finally {
      server.close();
    }
  });
});

test("UpdateManager downloads full update", async () => {
  await tempd3(async (feedDir, packagesDir, rootDir) => {
    const locator: VelopackLocatorConfig = {
      ManifestPath: "../../test/fixtures/Test.Squirrel-App.nuspec",
      PackagesDir: packagesDir,
      RootAppDir: rootDir,
      UpdateExePath: updateExe(),
      CurrentBinaryDir: path.join(rootDir, "current"),
      IsPortable: true,
    };

    const options: UpdateOptions = {
      ExplicitChannel: "beta",
      AllowVersionDowngrade: false,
      MaximumDeltasBeforeFallback: 10,
    };

    const um = new UpdateManager(feedDir, options, locator);
    copyFileSync(fixture("testfeed.json"), path.join(feedDir, "releases.beta.json"));

    copyFileSync(fixture("AvaloniaCrossPlat-1.0.11-win-full.nupkg"), path.join(feedDir, "AvaloniaCrossPlat-1.0.11-full.nupkg"));

    const update = await um.checkForUpdatesAsync();

    console.log(`about to download update from ${feedDir} to ${packagesDir} ...`);
    await um.downloadUpdateAsync(update!, () => {});

    expect(existsSync(path.join(packagesDir, "AvaloniaCrossPlat-1.0.11-full.nupkg"))).toBe(true);
  });
});
