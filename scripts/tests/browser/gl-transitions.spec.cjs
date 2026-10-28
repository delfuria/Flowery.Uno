const { test, expect } = require("@playwright/test");
const { readFile } = require("node:fs/promises");
const path = require("node:path");

// Runs the Gallery's GL transition engine with the catalog shaders from the source tree in a blank page.
// The page renders a frame only when the test asks for one, at a chosen time, so the frame has a known progress.
const galleryCore = path.resolve(__dirname, "../../../Flowery.Uno.Gallery.Core");
const origin = "http://flowery.test";
const appBase = "app";
const size = 200;

const pageHtml = `<!doctype html>
<script>globalThis.config = { uno_app_base: "${appBase}" };</script>
<script src="/${appBase}/Flowery.Uno.Gallery.Core/WasmScripts/flowery-carousel-gl-transitions.js"></script>`;

function installManualFrames() {
    let now = 0;
    let frames = [];
    let nextId = 0;
    performance.now = () => now;
    window.requestAnimationFrame = callback => {
        frames.push({ id: ++nextId, callback });
        return nextId;
    };
    window.cancelAnimationFrame = id => {
        frames = frames.filter(frame => frame.id !== id);
    };

    function checkerboard(width, height) {
        const canvas = document.createElement("canvas");
        canvas.width = width;
        canvas.height = height;
        const context = canvas.getContext("2d");
        context.fillStyle = "#000";
        context.fillRect(0, 0, width, height);
        context.fillStyle = "#fff";
        for (let y = 0; y < height; y += 2)
            for (let x = (y / 2) % 2 * 2; x < width; x += 4)
                context.fillRect(x, y, 2, 2);
        return canvas.toDataURL("image/png");
    }

    function solid(width, height, color) {
        const canvas = document.createElement("canvas");
        canvas.width = width;
        canvas.height = height;
        const context = canvas.getContext("2d");
        context.fillStyle = color;
        context.fillRect(0, 0, width, height);
        return canvas.toDataURL("image/png");
    }

    globalThis.glTransitionTest = {
        images: (width, height) => ({ from: checkerboard(width, height), to: solid(width, height, "#f00") }),
        pendingFrames: () => frames.length,
        // Renders the queued frame after advancing the clock and reads the canvas in the same task,
        // before the browser clears the drawing buffer.
        renderFrame(advanceMs) {
            now += advanceMs;
            const due = frames;
            frames = [];
            due.forEach(frame => frame.callback(now));
            const canvas = document.querySelector("canvas");
            const gl = canvas.getContext("webgl");
            const pixels = new Uint8Array(canvas.width * canvas.height * 4);
            gl.readPixels(0, 0, canvas.width, canvas.height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
            return { width: canvas.width, height: canvas.height, pixels: Array.from(pixels) };
        }
    };
}

async function renderMiddleFrame(page, transition) {
    // 0.8 s is 1 s after the engine's duration scale; without an interval, 500 ms is the middle of the transition.
    await page.evaluate(({ name, size }) => {
        const images = glTransitionTest.images(size, size);
        FloweryCarouselGLTransitions.attachOverlay("test", 0, 0, size, size, images, name, 0.8, 0);
    }, { name: transition, size });
    await expect.poll(() => page.evaluate(() => glTransitionTest.pendingFrames()),
        { message: `The ${transition} transition must load its shader and images.` }).toBe(1);
    const frame = await page.evaluate(() => {
        const result = glTransitionTest.renderFrame(500);
        FloweryCarouselGLTransitions.detachOverlay("test");
        return result;
    });
    expect([frame.width, frame.height]).toEqual([size, size]);
    return frame;
}

function channelMean(frame, channel) {
    let sum = 0;
    for (let i = channel; i < frame.pixels.length; i += 4)
        sum += frame.pixels[i];
    return sum / (frame.pixels.length / 4);
}

function meanDifference(a, b) {
    let sum = 0;
    for (let i = 0; i < a.pixels.length; i++)
        sum += Math.abs(a.pixels[i] - b.pixels[i]);
    return sum / a.pixels.length;
}

// Share of horizontally adjacent pixels with different colors: about 0.5 for the 2 px checkerboard,
// at most 0.1 for squares of 10 px.
function colorChangeRatio(frame) {
    let changes = 0;
    let pairs = 0;
    for (let y = 0; y < frame.height; y++) {
        for (let x = 1; x < frame.width; x++) {
            const i = (y * frame.width + x) * 4;
            const left = i - 4;
            if (frame.pixels[i] !== frame.pixels[left] || frame.pixels[i + 1] !== frame.pixels[left + 1]
                || frame.pixels[i + 2] !== frame.pixels[left + 2])
                changes++;
            pairs++;
        }
    }
    return changes / pairs;
}

test("Carousel GL transition pixelize pixelates its middle frame", async ({ page }, testInfo) => {
    const errors = [];
    page.on("pageerror", error => errors.push(error.message));
    page.on("console", message => {
        if (message.type() === "error")
            errors.push(message.text());
    });
    await page.route(`${origin}/**`, async route => {
        const url = new URL(route.request().url());
        if (url.pathname === "/")
            return route.fulfill({ contentType: "text/html", body: pageHtml });
        const prefix = `/${appBase}/Flowery.Uno.Gallery.Core/`;
        if (!url.pathname.startsWith(prefix))
            return route.fulfill({ status: 404 });
        const file = path.join(galleryCore, ...decodeURIComponent(url.pathname.slice(prefix.length)).split("/"));
        const contentType = file.endsWith(".js") ? "text/javascript" : "text/plain";
        try {
            return route.fulfill({ contentType, body: await readFile(file, "utf8") });
        } catch {
            return route.fulfill({ status: 404 });
        }
    });
    await page.addInitScript(installManualFrames);
    await page.goto(`${origin}/`);

    const fade = await renderMiddleFrame(page, "fade");
    const pixelize = await renderMiddleFrame(page, "pixelize");
    const report = {
        fade: { red: channelMean(fade, 0), green: channelMean(fade, 1), colorChanges: colorChangeRatio(fade) },
        pixelize: { colorChanges: colorChangeRatio(pixelize), differenceFromFade: meanDifference(pixelize, fade) }
    };
    await testInfo.attach("gl-transition-frames", {
        body: JSON.stringify(report, null, 2),
        contentType: "application/json"
    });

    // The crossfade reference is halfway between the checkerboard and red (within 5 of the exact means).
    expect(report.fade.red).toBeCloseTo(191.25, -1);
    expect(report.fade.green).toBeCloseTo(63.75, -1);
    expect(report.fade.colorChanges).toBeGreaterThan(0.4);
    // pixelize reads progress in a global initializer. A shader that sees 0 there draws the same crossfade.
    expect(report.pixelize.differenceFromFade).toBeGreaterThan(10);
    expect(report.pixelize.colorChanges).toBeLessThan(0.15);
    expect(errors).toEqual([]);
});
