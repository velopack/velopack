import { VelopackApp } from "../src/index";

test("VelopackApp should handle restarted event", () => {
    let afterInstall = false;
    let beforeUninstall = false;
    let beforeUpdate = false;
    let afterUpdate = false;
    let restarted = false;
    let firstRun = false;

    VelopackApp.build()
        .onAfterInstallFastCallback(() => afterInstall = true)
        .onBeforeUninstallFastCallback(() => beforeUninstall = true)
        .onBeforeUpdateFastCallback(() => beforeUpdate = true)
        .onAfterUpdateFastCallback(() => afterUpdate = true)
        .onRestarted(() => restarted = true)
        .onFirstRun(() => firstRun = true)
        .run();

    expect(afterInstall).toBe(false);
    expect(beforeUninstall).toBe(false);
    expect(beforeUpdate).toBe(false);
    expect(afterUpdate).toBe(false);
    expect(restarted).toBe(true);
    expect(firstRun).toBe(false);
});

test("VelopackApp should handle after-install hook", () => {
    let afterInstall = false;
    let beforeUninstall = false;
    let beforeUpdate = false;
    let afterUpdate = false;
    let restarted = false;
    let firstRun = false;

    VelopackApp.build()
        .onAfterInstallFastCallback(() => afterInstall = true)
        .onBeforeUninstallFastCallback(() => beforeUninstall = true)
        .onBeforeUpdateFastCallback(() => beforeUpdate = true)
        .onAfterUpdateFastCallback(() => afterUpdate = true)
        .onRestarted(() => restarted = true)
        .onFirstRun(() => firstRun = true)
        .setArgs(["--veloapp-install", "1.2.3-test.4"])
        .run();

    expect(afterInstall).toBe(true);
    expect(beforeUninstall).toBe(false);
    expect(beforeUpdate).toBe(false);
    expect(afterUpdate).toBe(false);
    expect(restarted).toBe(false);
    expect(firstRun).toBe(false);
});

test("VelopackApp should handle before-uninstall hook", () => {
    let afterInstall = false;
    let beforeUninstall = false;
    let beforeUpdate = false;
    let afterUpdate = false;
    let restarted = false;
    let firstRun = false;

    VelopackApp.build()
        .onAfterInstallFastCallback(() => afterInstall = true)
        .onBeforeUninstallFastCallback(() => beforeUninstall = true)
        .onBeforeUpdateFastCallback(() => beforeUpdate = true)
        .onAfterUpdateFastCallback(() => afterUpdate = true)
        .onRestarted(() => restarted = true)
        .onFirstRun(() => firstRun = true)
        .setArgs(["--veloapp-uninstall", "1.2.3-test"])
        .run();

    expect(afterInstall).toBe(false);
    expect(beforeUninstall).toBe(true);
    expect(beforeUpdate).toBe(false);
    expect(afterUpdate).toBe(false);
    expect(restarted).toBe(false);
    expect(firstRun).toBe(false);
});

test("VelopackApp should handle after-update hook", () => {
    let afterInstall = false;
    let beforeUninstall = false;
    let beforeUpdate = false;
    let afterUpdate = false;
    let restarted = false;
    let firstRun = false;

    VelopackApp.build()
        .onAfterInstallFastCallback(() => afterInstall = true)
        .onBeforeUninstallFastCallback(() => beforeUninstall = true)
        .onBeforeUpdateFastCallback(() => beforeUpdate = true)
        .onAfterUpdateFastCallback(() => afterUpdate = true)
        .onRestarted(() => restarted = true)
        .onFirstRun(() => firstRun = true)
        .setArgs(["--veloapp-updated", "1.2.3"])
        .run();

    expect(afterInstall).toBe(false);
    expect(beforeUninstall).toBe(false);
    expect(beforeUpdate).toBe(false);
    expect(afterUpdate).toBe(true);
    expect(restarted).toBe(false);
    expect(firstRun).toBe(false);
});

test("VelopackApp should handle before-update hook", () => {
    let afterInstall = false;
    let beforeUninstall = false;
    let beforeUpdate = false;
    let afterUpdate = false;
    let restarted = false;
    let firstRun = false;

    VelopackApp.build()
        .onAfterInstallFastCallback(() => afterInstall = true)
        .onBeforeUninstallFastCallback(() => beforeUninstall = true)
        .onBeforeUpdateFastCallback(() => beforeUpdate = true)
        .onAfterUpdateFastCallback(() => afterUpdate = true)
        .onRestarted(() => restarted = true)
        .onFirstRun(() => firstRun = true)
        .setArgs(["--veloapp-obsolete", "1.2.3-test.4"])
        .run();

    expect(afterInstall).toBe(false);
    expect(beforeUninstall).toBe(false);
    expect(beforeUpdate).toBe(true);
    expect(afterUpdate).toBe(false);
    expect(restarted).toBe(false);
    expect(firstRun).toBe(false);
});