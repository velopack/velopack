import { VelopackApp, VelopackLocatorConfig } from "../src/index";
import { shortDelay, isWindows } from "./helper";

class HookTester {
  public afterInstall = false;
  public beforeUninstall = false;
  public beforeUpdate = false;
  public afterUpdate = false;
  public restarted = false;
  public firstRun = false;
  public version = "";

  static build(): [VelopackApp, HookTester] {
    let tester = new HookTester();
    let builder = VelopackApp.build();
    builder.onAfterInstallFastCallback((ver) => {
      tester.afterInstall = true;
      tester.version = ver;
    });
    builder.onBeforeUninstallFastCallback((ver) => {
      tester.beforeUninstall = true;
      tester.version = ver;
    });
    builder.onBeforeUpdateFastCallback((ver) => {
      tester.beforeUpdate = true;
      tester.version = ver;
    });
    builder.onAfterUpdateFastCallback((ver) => {
      tester.afterUpdate = true;
      tester.version = ver;
    });
    builder.onRestarted((ver) => {
      tester.restarted = true;
      tester.version = ver;
    });
    builder.onFirstRun((ver) => {
      tester.firstRun = true;
      tester.version = ver;
    });
    builder.setLogger((level, msg) => {
      console.log(level, msg);
    });
    return [builder, tester];
  }
}

test("VelopackApp should handle restarted event", async () => {
  let [builder, tester] = HookTester.build();
  let locator: VelopackLocatorConfig = {
    ManifestPath: "../../test/fixtures/Test.Squirrel-App.nuspec",
    PackagesDir: "",
    RootAppDir: "",
    UpdateExePath: "",
    CurrentBinaryDir: "",
    IsPortable: true,
  };
  builder.setLocator(locator).run();

  expect(tester.afterInstall).toBe(false);
  expect(tester.beforeUninstall).toBe(false);
  expect(tester.beforeUpdate).toBe(false);
  expect(tester.afterUpdate).toBe(false);
  expect(tester.restarted).toBe(true);
  expect(tester.firstRun).toBe(false);
  expect(tester.version).toBe("1.0.0");
  await shortDelay();
});

test("VelopackApp should handle after-install hook", async () => {
  if (!isWindows()) return;

  let [builder, tester] = HookTester.build();
  builder.setArgs(["--veloapp-install", "1.2.3-test.4"]).run();

  expect(tester.afterInstall).toBe(true);
  expect(tester.beforeUninstall).toBe(false);
  expect(tester.beforeUpdate).toBe(false);
  expect(tester.afterUpdate).toBe(false);
  expect(tester.restarted).toBe(false);
  expect(tester.firstRun).toBe(false);
  expect(tester.version).toBe("1.2.3-test.4");
  await shortDelay();
});

test("VelopackApp should handle before-uninstall hook", async () => {
  if (!isWindows()) return;

  let [builder, tester] = HookTester.build();
  builder.setArgs(["--veloapp-uninstall", "1.2.3-test"]).run();

  expect(tester.afterInstall).toBe(false);
  expect(tester.beforeUninstall).toBe(true);
  expect(tester.beforeUpdate).toBe(false);
  expect(tester.afterUpdate).toBe(false);
  expect(tester.restarted).toBe(false);
  expect(tester.firstRun).toBe(false);
  expect(tester.version).toBe("1.2.3-test");
  await shortDelay();
});

test("VelopackApp should handle after-update hook", async () => {
  if (!isWindows()) return;

  let [builder, tester] = HookTester.build();
  builder.setArgs(["--veloapp-updated", "1.2.3"]).run();

  expect(tester.afterInstall).toBe(false);
  expect(tester.beforeUninstall).toBe(false);
  expect(tester.beforeUpdate).toBe(false);
  expect(tester.afterUpdate).toBe(true);
  expect(tester.restarted).toBe(false);
  expect(tester.firstRun).toBe(false);
  expect(tester.version).toBe("1.2.3");
  await shortDelay();
});

test("VelopackApp should handle before-update hook", async () => {
  if (!isWindows()) return;

  let [builder, tester] = HookTester.build();
  builder.setArgs(["--veloapp-obsolete", "1.2.3-test.4"]).run();

  expect(tester.afterInstall).toBe(false);
  expect(tester.beforeUninstall).toBe(false);
  expect(tester.beforeUpdate).toBe(true);
  expect(tester.afterUpdate).toBe(false);
  expect(tester.restarted).toBe(false);
  expect(tester.firstRun).toBe(false);
  expect(tester.version).toBe("1.2.3-test.4");
  await shortDelay();
});
