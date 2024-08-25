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
    expect(firstRun).toBe(true);
});