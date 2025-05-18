import {copyFileSync, existsSync} from "fs";
import {
  UpdateManager,
  UpdateOptions,
  VelopackApp,
  VelopackLocatorConfig,
} from "../src";
import path from "path";
import {tempd3, fixture, updateExe} from "./helper";

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
    copyFileSync(
      fixture("testfeed.json"),
      path.join(tmpDir, "releases.beta.json"),
    );
    const update = await um.checkForUpdatesAsync();

    expect(update).not.toBeNull();
    expect(update?.TargetFullRelease).not.toBeNull();
    expect(update?.TargetFullRelease?.Version).toBe("1.0.11");
    expect(update?.TargetFullRelease?.FileName).toBe(
      "AvaloniaCrossPlat-1.0.11-full.nupkg",
    );
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
    copyFileSync(
      fixture("testfeed.json"),
      path.join(feedDir, "releases.beta.json"),
    );

    copyFileSync(
      fixture("AvaloniaCrossPlat-1.0.11-win-full.nupkg"),
      path.join(feedDir, "AvaloniaCrossPlat-1.0.11-full.nupkg"),
    );

    const update = await um.checkForUpdatesAsync();

    console.log(
      `about to download update from ${feedDir} to ${packagesDir} ...`,
    );
    await um.downloadUpdateAsync(update!, () => {
    });

    expect(
      existsSync(
        path.join(packagesDir, "AvaloniaCrossPlat-1.0.11-full.nupkg"),
      ),
    ).toBe(true);
  });
});
