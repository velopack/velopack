import {VelopackApp, VelopackLocatorConfig} from "../src";
import {isWindows, makeId, updateExe} from "./helper";
import {execFile} from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import {promisify} from "node:util";

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
  // NewInstance mode: the callback runs synchronously inside run(), after onRestarted (run() consumes the
  // VELOPACK_RESTART variable, so it is set again here).
  process.env.VELOPACK_RESTART = "true";
  let received: string[] = [];
  VelopackApp.build()
    .onRestarted(() => received.push("restarted"))
    .onFileAssoc((path) => received.push("file:" + path))
    .onUrlProto((url) => received.push("url:" + url))
    .setArgs(["--veloapp-urlproto", "myapp://open?a=1"])
    .setLocator(locator)
    .run();
  expect(received).toEqual(["restarted", "url:myapp://open?a=1"]);

  // repeated [flag, value] pairs, as a restart to apply an update forwards them (a shell launch on Windows, without
  // VELOPACK_RESTART, carries exactly one value)
  process.env.VELOPACK_RESTART = "true";
  received = [];
  VelopackApp.build()
    .onFileAssoc((path) => received.push("file:" + path))
    .setArgs(["--veloapp-fileassoc", "C:\\docs\\my file.myproj", "--veloapp-fileassoc", "C:\\docs\\other.myproj"])
    .setLocator(locator)
    .run();
  expect(received).toEqual(["file:C:\\docs\\my file.myproj", "file:C:\\docs\\other.myproj"]);
});

test("VelopackApp should log a throwing association callback and deliver the rest", async () => {
  let locator: VelopackLocatorConfig = {
    ManifestPath: "../../test/fixtures/Test.Squirrel-App.nuspec",
    PackagesDir: "",
    RootAppDir: "",
    UpdateExePath: updateExe(),
    CurrentBinaryDir: "",
    IsPortable: true,
  };
  let messages: string[] = [];
  let received: string[] = [];
  process.env.VELOPACK_RESTART = "true"; // repeated pairs are only read for restarted launches on Windows
  VelopackApp.build()
    .setLogger((_level, msg) => messages.push(msg))
    .onFileAssoc((path) => {
      received.push(path);
      if (path === "first") throw new Error("boom from callback");
    })
    .setArgs(["--veloapp-fileassoc", "first", "--veloapp-fileassoc", "second"])
    .setLocator(locator)
    .run();

  expect(received).toEqual(["first", "second"]);
  await vi.waitFor(() => {
    expect(messages.some((msg) => msg.includes("file-assoc callback threw") && msg.includes("boom from callback"))).toBe(true);
  });
});

test("VelopackApp should deliver SingleInstance activations from the IPC thread to the JS thread", async () => {
  // the Rust test app (built by `npm run dev`) is the forwarding client; missing locally = skip, missing in CI = fail
  const testapp = path.join("..", "..", "target", "debug", isWindows() ? "testapp.exe" : "testapp");
  if (!fs.existsSync(testapp)) {
    if (process.env.CI) throw new Error(`rust testapp missing: ${testapp}`);
    return;
  }

  // an installed-looking root whose manifest selects SingleInstance (the lock is held for the rest of the process,
  // so the root is unique to this test)
  const root = path.join(os.tmpdir(), "velopack-node-ipc-" + makeId(8));
  fs.mkdirSync(path.join(root, "packages"), {recursive: true});
  const manifest = path.join(root, "sq.version");
  fs.writeFileSync(
    manifest,
    fs
      .readFileSync("../../test/fixtures/Test.Squirrel-App.nuspec", "utf8")
      .replace("</metadata>", "<associationLaunchMode>SingleInstance</associationLaunchMode></metadata>"),
  );
  const locator: VelopackLocatorConfig = {
    ManifestPath: manifest,
    PackagesDir: path.join(root, "packages"),
    RootAppDir: root,
    UpdateExePath: updateExe(),
    CurrentBinaryDir: "",
    IsPortable: true,
  };

  // a plain launch with no other instance: this process is elected the server
  let received: string[] = [];
  VelopackApp.build()
    .onUrlProto((url) => received.push(url))
    .setArgs([])
    .setLocator(locator)
    .run();
  expect(received).toEqual([]);

  // run() has returned, so the server's IPC thread must hand the activation to the event loop (unref'd channel)
  const url = 'myapp://open?q="a b"';
  const {stdout} = await promisify(execFile)(testapp, ["ipc-send", "Test.Squirrel-App", root, "--veloapp-urlproto", url]);
  expect(stdout.trim()).toBe("ack");
  await vi.waitFor(() => expect(received).toEqual([url]));
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
