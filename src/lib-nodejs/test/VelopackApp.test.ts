import {VelopackApp, VelopackLocatorConfig} from "../src";
import {isWindows, updateExe} from "./helper";

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
    return [builder, tester];
  }
}

test("VelopackApp should handle restarted event", async () => {
  let [builder, tester] = HookTester.build();
  let locator: VelopackLocatorConfig = {
    ManifestPath: "../../test/fixtures/Test.Squirrel-App.nuspec",
    PackagesDir: "",
    RootAppDir: "",
    UpdateExePath: updateExe(),
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
});

test("VelopackApp should invoke association hooks with the opened value", async () => {
  let locator: VelopackLocatorConfig = {
    ManifestPath: "../../test/fixtures/Test.Squirrel-App.nuspec",
    PackagesDir: "",
    RootAppDir: "",
    UpdateExePath: updateExe(),
    CurrentBinaryDir: "",
    IsPortable: true,
  };
  let received: string[] = [];
  VelopackApp.build()
    .onFileAssoc((path) => received.push("file:" + path))
    .onUrlProto((url) => received.push("url:" + url))
    .setArgs(["--veloapp-urlproto", "myapp://open?a=1"])
    .setLocator(locator)
    .run();

  await vi.waitFor(() => {
    expect(received).toEqual(["url:myapp://open?a=1"]);
  });

  // repeated [flag, value] pairs, as a restart to apply an update forwards them
  received = [];
  VelopackApp.build()
    .onFileAssoc((path) => received.push("file:" + path))
    .setArgs(["--veloapp-fileassoc", "C:\\docs\\my file.myproj", "--veloapp-fileassoc", "C:\\docs\\other.myproj"])
    .setLocator(locator)
    .run();

  await vi.waitFor(() => {
    expect(received).toEqual(["file:C:\\docs\\my file.myproj", "file:C:\\docs\\other.myproj"]);
  });
});

test("VelopackApp should forward library logs to the logger callback", async () => {
  let messages: string[] = [];
  let locator: VelopackLocatorConfig = {
    ManifestPath: "../../test/fixtures/Test.Squirrel-App.nuspec",
    PackagesDir: "",
    RootAppDir: "",
    UpdateExePath: updateExe(),
    CurrentBinaryDir: "",
    IsPortable: true,
  };
  VelopackApp.build()
    .setLogger((_level, msg) => messages.push(msg))
    .setLocator(locator)
    .run();

  await vi.waitFor(() => {
    expect(messages.some((msg) => msg.includes("VelopackApp: Running with args"))).toBe(true);
  });
});
