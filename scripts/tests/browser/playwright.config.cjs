const { defineConfig } = require("@playwright/test");
const path = require("node:path");

module.exports = defineConfig({
    testDir: __dirname,
    testMatch: ["drawer.spec.cjs", "gl-transitions.spec.cjs"],
    workers: 1,
    retries: 0,
    timeout: 180000,
    forbidOnly: true,
    reporter: [["list"], ["junit", { outputFile: "test-results/drawer.xml" }]],
    use: {
        baseURL: "http://127.0.0.1:5280",
        viewport: { width: 600, height: 800 },
        launchOptions: {
            args: ["--use-angle=swiftshader", "--enable-unsafe-swiftshader"]
        },
        screenshot: "only-on-failure",
        trace: "retain-on-failure"
    },
    webServer: {
        command: "dotnet run --project Flowery.Uno.Gallery/Flowery.Uno.Gallery.csproj -c Debug -f net10.0-browserwasm --no-build --no-restore --no-launch-profile",
        env: { ASPNETCORE_URLS: "http://127.0.0.1:5280" },
        cwd: path.resolve(__dirname, "../../.."),
        url: "http://127.0.0.1:5280",
        reuseExistingServer: false,
        timeout: 120000,
        stdout: "pipe",
        stderr: "pipe",
        gracefulShutdown: { signal: "SIGTERM", timeout: 5000 }
    }
});
