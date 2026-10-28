# Flowery.Uno Build Scripts

PowerShell scripts for building and running Flowery.Uno projects. All scripts follow a consistent output format with clear terminal, head identification, and build summaries.

## Projects

| Project                        | Description                    |
| ------------------------------ | ------------------------------ |
| `Flowery.Uno`                  | Core UI component library      |
| `Flowery.Uno.Gallery`          | Shared Gallery UI library      |
| `Flowery.Uno.Gallery.Windows`  | WinUI Gallery app (Windows)    |
| `Flowery.Uno.Gallery.Desktop`  | Skia desktop Gallery head      |
| `Flowery.Uno.Gallery.Browser`  | WebAssembly Gallery head       |
| `Flowery.Uno.Gallery.Android`  | Android Gallery head           |

---

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- PowerShell 7+ (`pwsh`)
- Visual Studio 2022 with MSBuild (for `build_desktop.ps1` and `build_windows.ps1`)

---

## Local CI with gh act (optional)

Run Linux GitHub Actions jobs locally using the `nektos/gh-act` extension (requires Docker Desktop).

**Install:**

```powershell
gh extension install nektos/gh-act
```

**Usage (CI build-library):**

```powershell
gh act -W .github/workflows/ci.yml -j build-library
```

**Usage (Release build-library without publish/tag):**

```powershell
@'
{"inputs":{"version":"0.0.0","skip_nuget":"true","skip_tag":"true"}}
'@ | Set-Content -Encoding utf8 .\.github\act-release.json
gh act workflow_dispatch -W .github/workflows/release.yml -j build-library -e .\.github\act-release.json
```

**Notes:**

- The repo includes `.actrc`, which provides a default `ubuntu-latest` runner image and forces `linux/amd64`, so you do not need to pass `-P` manually.
- `act` cannot run `windows-latest` jobs; use GitHub Actions or a Windows runner for the Gallery Windows build.

---

## Architecture

### Shared Core Module

All MSBuild-based build scripts use `build_core.ps1`, a shared module providing:

- **`Initialize-BuildEnvironment`** - Sets up logging, clears terminal, locates MSBuild
- **`Invoke-ProjectBuild`** - Builds a project, captures output, counts warnings
- **`Write-BuildSummary`** - Outputs formatted summary with per-project timing and warnings
- **`Start-GalleryApp`** - Launches the Gallery app after build

### Output Format

All scripts follow a consistent output pattern:

```bash
Building Flowery.Uno {HeadName} projects ({Configuration})...

Building Flowery.Uno...
Building Flowery.Uno.Gallery.Windows...

═══════════════════════════════════════════════════════════
 BUILD SUMMARY
═══════════════════════════════════════════════════════════

  [OK] Flowery.Uno                               00:13.05 (12 warnings)
  [OK] Flowery.Uno.Gallery.Windows               00:04.65

  Total time: 00:17.73
  Projects built: 2
  Total warnings: 12
  Log: D:\github\Flowery.Uno\logs\desktop_build.log

All builds completed successfully.

Starting Gallery app...
Started: Flowery.Uno.Gallery.Windows.exe
```

---

## Build Scripts

### build_desktop.ps1 / build_desktop.sh

Builds the library and Desktop (Skia) Gallery head using MSBuild. Runs the Gallery app by default.

```powershell
pwsh ./scripts/build_desktop.ps1              # Build and run
pwsh ./scripts/build_desktop.ps1 -NoRun       # Build only
pwsh ./scripts/build_desktop.ps1 -Rebuild     # Clean rebuild
pwsh ./scripts/build_desktop.ps1 -VerboseOutput     # Show full build output
pwsh ./scripts/build_desktop.ps1 -Configuration Release
```

**Git Bash:** `./scripts/build_desktop.sh`

| Parameter        | Type   | Default | Description                                |
| ---------------- | ------ | ------- | ------------------------------------------ |
| `-Configuration` | string | `Debug` | Build configuration (`Debug` or `Release`) |
| `-NoRun`         | switch | false   | Skip running the app after build           |
| `-Rebuild`       | switch | false   | Perform a clean rebuild                    |
| `-VerboseOutput` | switch | false   | Print build output to console              |

**Log file:** `logs/desktop_build.log`

---

### build_windows.ps1 / build_windows.sh

Builds only the Windows (WinUI) head using MSBuild.

```powershell
pwsh ./scripts/build_windows.ps1
pwsh ./scripts/build_windows.ps1 -NoRun -Rebuild
pwsh ./scripts/build_windows.ps1 -Configuration Release
```

**Git Bash:** `./scripts/build_windows.sh`

| Parameter        | Type   | Default | Description                                |
| ---------------- | ------ | ------- | ------------------------------------------ |
| `-Configuration` | string | `Debug` | Build configuration (`Debug` or `Release`) |
| `-NoRun`         | switch | false   | Skip running the app after build           |
| `-Rebuild`       | switch | false   | Perform a clean rebuild                    |
| `-VerboseOutput` | switch | false   | Print build output to console              |

**Log file:** `logs/windows_build.log`

---

### publish_windows.ps1

Publishes the Windows (WinUI) Gallery head using dotnet publish.

```powershell
pwsh ./scripts/publish_windows.ps1
pwsh ./scripts/publish_windows.ps1 -Configuration Release
pwsh ./scripts/publish_windows.ps1 -SelfContained:$false -OutputDir ./publish-win
```

| Parameter        | Type   | Default    | Description                                        |
| --------------- | ------ | ---------- | -------------------------------------------------- |
| `-Configuration` | string | `Release`  | Publish configuration (`Debug` or `Release`)       |
| `-RuntimeIdentifier` | string | `win-x64` | Runtime identifier                                 |
| `-SelfContained` | bool   | `true`     | Publish self-contained output                       |
| `-OutputDir`     | string | `publish`  | Output directory                                    |
| `-VerboseOutput` | switch | false      | Print output to console (still writes log)          |

**Log file:** `logs/windows_publish.log`

---

### build_android.ps1 / build_android.sh

Builds the Android head using dotnet build.

```powershell
pwsh ./scripts/build_android.ps1
pwsh ./scripts/build_android.ps1 -Rebuild
pwsh ./scripts/build_android.ps1 -AndroidSdkDirectory "C:\Users\YOU\AppData\Local\Android\Sdk"
```

**Git Bash:** `./scripts/build_android.sh`

| Parameter              | Type   | Default | Description                                |
| ---------------------- | ------ | ------- | ------------------------------------------ |
| `-Configuration`       | string | `Debug` | Build configuration (`Debug` or `Release`) |
| `-AndroidSdkDirectory` | string | auto    | Android SDK path (or set ANDROID_SDK_ROOT) |
| `-NoRun`               | switch | false   | Skip showing run instructions              |
| `-Rebuild`             | switch | false   | Perform a clean rebuild                    |
| `-VerboseOutput`       | switch | false   | Print build output to console              |

**Log file:** `logs/android_build.log`

---

### build_browser.ps1 / build_browser.sh

Builds the library and Browser (WebAssembly) Gallery head. Runs the Gallery app via local server by default.

```powershell
pwsh ./scripts/build_browser.ps1              # Build and run
pwsh ./scripts/build_browser.ps1 -NoRun       # Build only
pwsh ./scripts/build_browser.ps1 -Rebuild     # Clean rebuild
pwsh ./scripts/build_browser.ps1 -VerboseOutput     # Show full build output
pwsh ./scripts/build_browser.ps1 -Configuration Release
```

**Git Bash:** `./scripts/build_browser.sh`

| Parameter        | Type   | Default | Description                                |
| ---------------- | ------ | ------- | ------------------------------------------ |
| `-Configuration` | string | `Debug` | Build configuration (`Debug` or `Release`) |
| `-NoRun`         | switch | false   | Skip running the app after build           |
| `-Rebuild`       | switch | false   | Perform a clean rebuild                    |
| `-VerboseOutput` | switch | false   | Print build output to console              |

**Log file:** `logs/browser_build.log`

---

### build_all.ps1 / build_all.sh

Builds all heads (Windows, Desktop, Browser, Android) plus the shared libraries.

```powershell
pwsh ./scripts/build_all.ps1
pwsh ./scripts/build_all.ps1 -Rebuild
pwsh ./scripts/build_all.ps1 -RestoreWorkloads
pwsh ./scripts/build_all.ps1 -Configuration Release
```

**Git Bash:** `./scripts/build_all.sh`

| Parameter              | Type   | Default | Description                                |
| ---------------------- | ------ | ------- | ------------------------------------------ |
| `-Configuration`       | string | `Debug` | Build configuration (`Debug` or `Release`) |
| `-AndroidSdkDirectory` | string | auto    | Android SDK path (or set ANDROID_SDK_ROOT) |
| `-RestoreWorkloads`    | switch | false   | Restore .NET workloads before building     |
| `-NoRun`               | switch | false   | (reserved for future use)                  |
| `-Rebuild`             | switch | false   | Perform a clean rebuild                    |
| `-VerboseOutput`       | switch | false   | Print build output to console              |

**Log file:** `logs/all_build.log`

---

## Run Scripts

All run scripts clear the terminal and display which head they're running. Desktop/Windows run in a console window and wait by default so exceptions stay visible.

### run-desktop.ps1

Builds and launches the Skia desktop head. Runs via `cmd /k` and waits by default; use `-NoWait` to detach.

```powershell
pwsh ./scripts/run-desktop.ps1
pwsh ./scripts/run-desktop.ps1 -Configuration Release -Rebuild
pwsh ./scripts/run-desktop.ps1 -NoWait
```

---

### run-windows.ps1

Builds and launches the WinUI Windows head. Runs via `cmd /k` and waits by default; use `-NoWait` to detach.

```powershell
pwsh ./scripts/run-windows.ps1
pwsh ./scripts/run-windows.ps1 -Configuration Release -Rebuild
pwsh ./scripts/run-windows.ps1 -NoWait
```

---

### run-browser.ps1

Runs the WASM head and optionally opens the browser.

```powershell
pwsh ./scripts/run-browser.ps1
pwsh ./scripts/run-browser.ps1 -NoBrowser -Rebuild
pwsh ./scripts/run-browser.ps1 -Port 8080
```

---

### run-android.ps1

Builds and deploys the Android head to an emulator/device.

```powershell
pwsh ./scripts/run-android.ps1
pwsh ./scripts/run-android.ps1 -DeviceName "emulator-5554" -Rebuild
```

---

### run-linux-desktop.ps1

Runs the Desktop (Skia) Gallery on a Linux desktop (XFCE) in Docker, shown in the browser through noVNC. Requires Docker Desktop with Linux containers.

- Publish: the Gallery is published self-contained to `bin/linux-desktop` for the architecture of the Docker host (`linux-x64` or `linux-arm64`), in the layout of the Linux packages. Only the `net10.0-desktop` target is published, so the runtime identifier does not reach the Android and WebAssembly targets.
- Image: `flowery-linux-desktop` from `docker/linux-desktop`, about 1 GB, with no .NET SDK. The script builds it when it is missing or older than 7 days, without the Docker build cache and from the current base image and packages, so Debian's security updates are at most a week old. It then removes the image from before. When a rebuild only for the age fails, for example offline, it keeps the old image. `-RebuildImage` builds at once, for example for a CVE.
- Shared with Flowery.NET: `docker/linux-desktop` is the same there and builds the image under the same tag, so either repository reuses or replaces it for both. The workflow below fails when the two differ.
- Container: `flowery-uno-desktop`, with the published app mounted read-only.

```powershell
# Publish, start the desktop and open http://localhost:6081 in the browser
pwsh ./scripts/run-linux-desktop.ps1

# Reuse the published app, use a smaller desktop and run it in German
pwsh ./scripts/run-linux-desktop.ps1 -SkipPublish -Geometry 1280x800 -Culture de-DE

# Build the image again now, after a change in docker/linux-desktop or for a CVE
pwsh ./scripts/run-linux-desktop.ps1 -RebuildImage
```

| Parameter | Type | Default | Description |
| --------- | ---- | ------- | ----------- |
| `-Configuration` | string | `Release` | Build configuration to publish |
| `-SkipPublish` | switch | false | Reuse the app in `bin/linux-desktop` |
| `-RebuildImage` | switch | false | Build the image again now, without the build cache and from the current packages |
| `-Geometry` | string | `1600x1000` | Size of the desktop |
| `-Culture` | string | culture of the system | Culture of the app, passed as `LANG`; English without one. The desktop stays English, as the image has no locales |
| `-Port` | int | `6081` | Local port of noVNC |
| `-VncPort` | int | `5902` | Local port of VNC (no password) |
| `-NoBrowser` | switch | false | Do not open the browser |

The ports listen on `127.0.0.1` only and differ from Flowery.NET's (6080, 5901), so both desktops can run at once. The Gallery's output is in `/tmp/app.log`; it writes handled exceptions to `~/.local/share/Flowery.Uno.Gallery/crash.log` and the chosen page to `sidebar.state` in the same folder. Checks from outside the container:

```powershell
# Screenshot as PNG
docker exec flowery-uno-desktop sh -c 'xwd -root -silent | xwdtopnm | pnmtopng' > shot.png

# Mouse and keyboard input with xdotool: type into the sidebar search (at the default geometry, with the window at 0,30)
docker exec flowery-uno-desktop xdotool mousemove 110 82 click 1 type Scheduler

# The page the Gallery shows
docker exec flowery-uno-desktop cat /home/flowery/.local/share/Flowery.Uno.Gallery/sidebar.state

# Stop the desktop
docker rm -f flowery-uno-desktop
```

On pull requests that touch the library or the Gallery, the `Linux Desktop` workflow (`.github/workflows/linux-desktop.yml`) compares `docker/linux-desktop` with Flowery.NET's and runs this script on the runner, which builds the image there. It then starts the Gallery again with `last:scheduler` and a probe line in `sidebar.state`. The sidebar restores the Scheduler, asks for its page and writes the file again without the probe line. The workflow fails when that does not happen, the Gallery stops, it writes a crash log, or its output shows an exception. Its artifact holds the screenshots and the logs.

---

## build_nuget.ps1

Builds the NuGet package for `Flowery.Uno`.

```powershell
pwsh ./scripts/build_nuget.ps1
pwsh ./scripts/build_nuget.ps1 -OutputDir ./packages
```

| Parameter        | Type   | Default   | Description                        |
| ---------------- | ------ | --------- | ---------------------------------- |
| `-Configuration` | string | `Release` | Build configuration                |
| `-OutputDir`     | string | `./nupkg` | Output directory for `.nupkg` file |

---

## Troubleshooting

### Carousel regression tests

Run the JavaScript resource and lifecycle tests with Node.js. No npm packages are required:

```powershell
node --test scripts/tests/carousel-gl.test.cjs
```

The Gallery runtime suite includes pixel tests for composition masks, frozen outgoing
image transforms, native glass processing, and the automatic Gallery effect sequence.
Build the Windows or Desktop Gallery, then run its executable with
`--runtime-tests <absolute-result.xml>`. The process returns a nonzero exit code on failure.
The Windows and Desktop runtime suites and JavaScript tests also run in CI.
CI also builds the WASM Gallery, including its native link step.

For the live WASM path, open **Carousel GL Transitions**, enable **Slideshow**,
and set **Interval** to 1 second. Run `tests/carousel-gl-continuity.browser.js`
in that page's browser console. Keep the navigation closed or in Inline mode.
The expression returns a report after four shader changes, with a 30-second timeout.
`passed` must be `true`: no hidden, paused or replaced renderer frames are allowed.

### Drawer browser regression tests

The `test-gallery-drawer` CI job runs Chromium against the Debug WASM Gallery.
It checks both the normal state group and a renamed group that the production lookup cannot find.
Both cases use real Uno animations and GL renderers at a 600 × 800 viewport.
The test checks pause state, animation completion, renderer visibility, and reopening during closure.
The slow cases extend the real closing animation durations. Uno 6.7 does not implement `Timeline.SpeedRatio` on WASM/Skia.

For a local run, first build the Debug Gallery:

```powershell
pwsh ./scripts/build_browser.ps1 -Configuration Debug -NoRun -Cli
```

Then install the test dependencies and run the tests:

```powershell
cd scripts/tests/browser
pnpm install --frozen-lockfile
pnpm exec playwright install chromium
pnpm test
```

The runner starts and stops the Gallery server on port 5280. The port must be free.
Each case uses a fresh browser context. Retries are disabled.
CI uploads JSON results, console logs, and a JUnit report in the `gallery-browser-tests` artifact.
Failed tests also include a screenshot and a Playwright trace.
Unavailable Uno Debug remote-control WebSockets (`/rc`) remain in the logs but do not fail the test.
Other console errors and all unhandled page errors fail the test.
Chromium uses SwiftShader for WebGL. This job does not test a physical GPU.

The same job runs `gl-transitions.spec.cjs`. It loads the Gallery's GL transition engine
(`Flowery.Uno.Gallery.Core/WasmScripts/flowery-carousel-gl-transitions.js`) and the transition shaders
from the source tree into a blank page, renders single frames at a set time and reads their pixels.
The middle frame of `pixelize` must be made of squares and differ from the `fade` crossfade of the same images.
The middle frame of `BookFlip` must show the outgoing image left of the center and the incoming image right of it.
The tests do not use the Gallery, but the runner starts the Gallery server for every run.
`pnpm exec playwright test gl-transitions.spec.cjs` runs only these tests.

### Build fails with missing SDK

Ensure you have .NET 10.0 SDK installed:

```powershell
dotnet --version
# Should show 10.0.x
```

### MSBuild not found

The `build_desktop.ps1` and `build_windows.ps1` scripts require Visual Studio 2022 with MSBuild. Ensure VS is installed and includes the ".NET desktop development" workload.

### Executable not found after build

Check that the build succeeded and look in the `bin/` directory at the repo root.

### View build errors

When builds fail, errors and warnings are automatically extracted from the log and displayed in the console. Full logs are saved to the `logs/` folder with head-specific filenames.
