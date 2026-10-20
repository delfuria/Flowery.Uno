const { test, expect } = require("@playwright/test");
const { readFileSync } = require("node:fs");
const { mkdir, writeFile } = require("node:fs/promises");
const path = require("node:path");

const expression = readFileSync(path.join(__dirname, "../carousel-drawer.browser.js"), "utf8");
// Debug builds can contain IDE endpoints, but CI does not run Uno's remote-control server.
const unavailableDevServer = /^WebSocket connection to 'wss?:\/\/[^/]+\/rc' failed: Error in connection establishment: net::ERR_(CONNECTION_REFUSED|ADDRESS_UNREACHABLE)$/;

for (const group of ["normal", "missing"]) {
    test(`Drawer with ${group} state group`, async ({ page }, testInfo) => {
        await mkdir(testInfo.outputDir, { recursive: true });
        const errors = [];
        const consoleLog = [];
        page.on("pageerror", error => errors.push(error.message));
        page.on("console", message => {
            consoleLog.push(`${message.type()}: ${message.text()}`);
            if (message.type() === "error" && !unavailableDevServer.test(message.text()))
                errors.push(message.text());
        });
        try {
            await page.goto(`/?drawer-test=${group}`);
            await expect.poll(() => page.evaluate(async () => {
                if (!globalThis.Module?.getAssemblyExports) return false;
                try {
                    const exports = await Module.getAssemblyExports("Flowery.Uno.Gallery.Core");
                    exports.Flowery.Uno.Gallery.MainView.GetDrawerProbeState();
                    return true;
                } catch (error) {
                    // The exports can exist before MainView.Loaded registers the probe.
                    return String(error);
                }
            }), { timeout: 90000, message: "The Debug Gallery drawer probe must be ready." }).toBe(true);

            const report = await page.evaluate(expression);
            const reportPath = testInfo.outputPath("drawer-results.json");
            await writeFile(reportPath, JSON.stringify(report, null, 2));
            await testInfo.attach("drawer-results", {
                path: reportPath,
                contentType: "application/json"
            });
            expect(report.passed).toBe(true);
            expect(report.missingGroup).toBe(group === "missing");
            expect(report.results).toHaveLength(4);
            expect(errors).toEqual([]);
        } finally {
            const consolePath = testInfo.outputPath("browser-console.txt");
            await writeFile(consolePath, consoleLog.concat(errors.map(error => `ERROR: ${error}`)).join("\n"));
            await testInfo.attach("browser-console", {
                path: consolePath,
                contentType: "text/plain"
            });
        }
    });
}
