// Run with: node --test scripts/tests/carousel-gl.test.cjs
// These tests execute the production overlay APIs with controlled DOM/network boundaries.
// They verify URL, error and lifecycle behavior; GPU pixels require the browser checks.
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const path = require('node:path');
const { setImmediate: nextTurn } = require('node:timers/promises');
const test = require('node:test');
const vm = require('node:vm');

const root = path.resolve(__dirname, '../..');
const scripts = [
    {
        name: 'Carousel GL',
        file: 'Flowery.Uno/WasmScripts/flowery-carousel-gl.js',
        api: 'FloweryCarouselGL',
        attach(api, assets = { effect: 'Assets/photo.jpg' }, rect = [10, 20, 240, 120]) {
            api.attachOverlay('test', 'effect', ...rect, assets, 'pan-zoom');
        }
    },
    {
        name: 'Carousel GL Transitions',
        file: 'Flowery.Uno.Gallery.Core/WasmScripts/flowery-carousel-gl-transitions.js',
        api: 'FloweryCarouselGLTransitions',
        attach(api, assets = null, rect = [10, 20, 240, 120]) {
            api.attachOverlay('test', ...rect, assets, 'fade', 0.6, 1);
        }
    }
];

function createGl(shaderFailure) {
    // Only the browser API boundary is substituted. Shader assembly, initialization,
    // async continuations and state ownership all remain production code.
    return {
        VERTEX_SHADER: 1, FRAGMENT_SHADER: 2, COMPILE_STATUS: 3, LINK_STATUS: 4,
        ARRAY_BUFFER: 5, STATIC_DRAW: 6, TEXTURE_2D: 7, TEXTURE0: 8, TEXTURE1: 9,
        TEXTURE_WRAP_S: 10, TEXTURE_WRAP_T: 11, CLAMP_TO_EDGE: 12,
        TEXTURE_MIN_FILTER: 13, TEXTURE_MAG_FILTER: 14, LINEAR: 15,
        UNPACK_FLIP_Y_WEBGL: 16, RGBA: 17, UNSIGNED_BYTE: 18,
        COLOR_BUFFER_BIT: 19, TRIANGLE_STRIP: 20, FLOAT: 21,
        createShader: () => ({}),
        shaderSource() {}, compileShader() {},
        getShaderParameter: () => !shaderFailure,
        getShaderInfoLog: () => 'Test shader compilation failure',
        deleteShader() {},
        createProgram: () => ({}),
        attachShader() {}, linkProgram() {},
        getProgramParameter: () => true,
        deleteProgram() {},
        createBuffer: () => ({}),
        bindBuffer() {}, bufferData() {},
        getAttribLocation: () => 0,
        enableVertexAttribArray() {}, vertexAttribPointer() {},
        createTexture: () => ({}),
        bindTexture() {}, texParameteri() {}, pixelStorei() {}, texImage2D() {},
        getUniformLocation: () => ({}),
        clearColor() {}, clear() {}, useProgram() {}, viewport() {},
        uniform1i() {}, uniform1f() {}, uniform2f() {}, uniform3f() {}, uniform4f() {},
        uniform2i() {}, uniform3i() {}, uniform4i() {}, activeTexture() {}, drawArrays() {}
    };
}

function createHarness(script, options = {}) {
    const imageRequests = [];
    const fetchRequests = [];
    const frames = new Map();
    let nextFrameId = 0;
    const gl = options.webgl === false ? null : createGl(options.shaderFailure === true);

    class Element {
        constructor(tagName) {
            this.tagName = tagName;
            this.style = {};
            this.children = [];
            this.parentNode = null;
            this.textContent = '';
        }
        get isConnected() {
            return this === body || this.parentNode?.isConnected === true;
        }
        appendChild(child) {
            child.remove();
            this.children.push(child);
            child.parentNode = this;
            return child;
        }
        remove() {
            if (this.parentNode) {
                const siblings = this.parentNode.children;
                siblings.splice(siblings.indexOf(this), 1);
                this.parentNode = null;
            }
        }
        getContext(kind) {
            assert.equal(this.tagName, 'canvas');
            if (kind === 'webgl') {
                this.contextKind = kind;
                return gl;
            }
            assert.equal(kind, '2d');
            assert.notEqual(this.contextKind, 'webgl', 'Error messages must not request 2D on a WebGL canvas');
            this.contextKind = kind;
            return { clearRect() {}, fillText() {} };
        }
        getBoundingClientRect() {
            return {
                left: Number.parseFloat(this.style.left) || 0,
                top: Number.parseFloat(this.style.top) || 0,
                width: Number.parseFloat(this.style.width) || 0,
                height: Number.parseFloat(this.style.height) || 0
            };
        }
    }

    const body = new Element('body');
    const document = {
        baseURI: options.baseURI || 'https://example.test/',
        body,
        documentElement: new Element('html'),
        createElement: tag => new Element(tag),
        getElementById: () => null
    };
    class ControlledImage {
        set src(url) {
            this.url = url;
            imageRequests.push(this);
        }
    }
    const context = vm.createContext({
        URL, document,
        config: { uno_app_base: options.appBase || 'package_test' },
        Image: ControlledImage,
        performance: { now: () => 100 },
        console: { error() {} },
        devicePixelRatio: 1,
        getComputedStyle: element => ({ ...element.style, getPropertyValue: () => '' }),
        requestAnimationFrame(callback) {
            const id = ++nextFrameId;
            frames.set(id, callback);
            return id;
        },
        cancelAnimationFrame: id => frames.delete(id),
        fetch(url) {
            return new Promise((resolve, reject) => fetchRequests.push({ url, resolve, reject }));
        }
    });
    context.window = context;
    vm.runInContext(readFileSync(path.join(root, script.file), 'utf8'), context, { filename: script.file });
    const api = context[script.api];

    return {
        api, body, frames, imageRequests, fetchRequests,
        messages: () => body.children.filter(element => element.tagName === 'div'),
        finishImages(error = false) {
            for (const image of imageRequests.splice(0)) {
                if (error) image.onerror();
                else image.onload();
            }
        },
        finishFetches(error = false) {
            for (const request of fetchRequests.splice(0)) {
                if (error) request.reject(new Error('Test shader request failure'));
                else request.resolve({ ok: true, text: async () => 'vec4 transition(vec2 uv) { return getToColor(uv); }' });
            }
        }
    };
}

const deployments = [
    { name: 'origin root', baseURI: 'https://example.test/', appBase: 'package_test', expected: 'https://example.test/package_test/' },
    { name: 'application subpath', baseURI: 'https://example.test/gallery/', appBase: './package_test', expected: 'https://example.test/gallery/package_test/' },
    { name: 'CDN package', baseURI: 'https://example.test/gallery/', appBase: 'https://cdn.example.test/releases/package_test', expected: 'https://cdn.example.test/releases/package_test/' }
];

for (const script of scripts) {
    for (const deployment of deployments) {
        test(`${script.name}: resolves assets for ${deployment.name}`, t => {
            const harness = createHarness(script, deployment);
            t.after(() => harness.api.detachAllOverlays());
            script.attach(harness.api);
            const assets = script.api === 'FloweryCarouselGL'
                ? ['Assets/photo.jpg']
                : ['Assets/wallpaper_blue.jpg', 'Assets/wallpaper_green.jpg', 'Assets/wallpaper_red.jpg', 'Assets/wallpaper_yellow.jpg'];
            assert.deepEqual(harness.imageRequests.map(request => request.url), assets.map(asset => deployment.expected + asset));
            assert.deepEqual(harness.fetchRequests.map(request => request.url), script.api === 'FloweryCarouselGLTransitions'
                ? [deployment.expected + 'Flowery.Uno.Gallery.Core/WasmScripts/gl-transitions/fade.glsl']
                : []);
        });
    }

    test(`${script.name}: preserves absolute image URLs`, t => {
        const urls = ['https://images.example.test/a.jpg', '//images.example.test/b.jpg', 'data:image/png;base64,AA=='];
        for (const url of urls) {
            const harness = createHarness(script);
            t.after(() => harness.api.detachAllOverlays());
            const assets = script.api === 'FloweryCarouselGL'
                ? { effect: url }
                : { images: [url, 'Assets/second.jpg'] };
            script.attach(harness.api, assets);
            assert.equal(harness.imageRequests[0].url, url);
        }
    });

    test(`${script.name}: WebGL failure follows rectangle, visibility and detach`, () => {
        const harness = createHarness(script, { webgl: false });
        script.attach(harness.api);
        assert.equal(harness.messages().length, 1);
        const message = harness.messages()[0];
        assert.match(message.textContent, /WebGL unavailable/);
        assert.equal(message.style.display, 'flex');
        assert.equal(message.style.left, '10px');
        assert.equal(message.style.width, '240px');
        script.attach(harness.api, undefined, [30, 40, 300, 150]);
        assert.equal(message.style.left, '30px');
        assert.equal(message.style.top, '40px');
        assert.equal(message.style.width, '300px');
        assert.equal(message.style.height, '150px');
        harness.api.setOverlayActive('test', false);
        assert.equal(message.style.display, 'none');
        harness.api.setOverlayActive('test', true);
        assert.equal(message.style.display, 'flex');
        harness.api.detachOverlay('test');
        assert.equal(harness.body.children.length, 0);
        assert.equal(harness.frames.size, 0);
    });

    test(`${script.name}: reports shader compilation failure and cleans up`, async () => {
        const harness = createHarness(script, { shaderFailure: true });
        script.attach(harness.api);
        harness.finishFetches();
        harness.finishImages();
        await nextTurn();
        assert.equal(harness.messages().length, 1);
        assert.match(harness.messages()[0].textContent, /shader load failed|WebGL initialization failed/);
        harness.api.detachOverlay('test');
        assert.equal(harness.body.children.length, 0);
        assert.equal(harness.frames.size, 0);
    });

    test(`${script.name}: reports image loading failure and cleans up`, async () => {
        const harness = createHarness(script);
        script.attach(harness.api);
        harness.finishFetches();
        harness.finishImages(true);
        await nextTurn();
        assert.equal(harness.messages().length, 1);
        assert.match(harness.messages()[0].textContent, /load failed/);
        harness.api.detachOverlay('test');
        assert.equal(harness.body.children.length, 0);
        assert.equal(harness.frames.size, 0);
    });

    for (const loadFails of [false, true]) {
        test(`${script.name}: detach before async ${loadFails ? 'failure' : 'success'} leaves no DOM or animation`, async () => {
            const harness = createHarness(script);
            script.attach(harness.api);
            assert.ok(harness.imageRequests.length > 0);
            harness.api.detachOverlay('test');
            harness.finishFetches(loadFails);
            harness.finishImages(loadFails);
            await nextTurn();
            assert.equal(harness.body.children.length, 0);
            assert.equal(harness.frames.size, 0, 'A detached overlay must not start another animation frame');
        });
    }

    for (const pauseBeforeLoad of [false, true]) {
        test(`${script.name}: pause ${pauseBeforeLoad ? 'before' : 'after'} loading hides and stops the overlay until resume`, async t => {
            const harness = createHarness(script);
            t.after(() => harness.api.detachAllOverlays());
            script.attach(harness.api);
            const canvas = harness.body.children.find(element => element.tagName === 'canvas');
            if (pauseBeforeLoad) harness.api.setOverlayActive('test', false);
            harness.finishFetches();
            harness.finishImages();
            await nextTurn();
            if (!pauseBeforeLoad) {
                assert.equal(harness.frames.size, 1, 'The loaded overlay must animate before pausing');
                harness.api.setOverlayActive('test', false);
            }
            assert.equal(canvas.style.display, 'none');
            assert.equal(harness.frames.size, 0, 'Loading must not restart a paused overlay');
            harness.api.setOverlayActive('test', true);
            assert.equal(canvas.style.display, 'block');
            assert.equal(harness.frames.size, 1, 'Resuming must restart exactly one animation loop');
        });
    }

    test(`${script.name}: a load error stays hidden until the paused overlay resumes`, async t => {
        const harness = createHarness(script);
        t.after(() => harness.api.detachAllOverlays());
        script.attach(harness.api);
        harness.api.setOverlayActive('test', false);
        harness.finishFetches();
        harness.finishImages(true);
        await nextTurn();
        assert.equal(harness.messages().length, 1);
        assert.equal(harness.messages()[0].style.display, 'none');
        assert.equal(harness.frames.size, 0);
        harness.api.setOverlayActive('test', true);
        assert.equal(harness.messages()[0].style.display, 'flex');
    });
}

const libraryModes = [
    { mode: 'mask', keys: ['maskA', 'maskB'], variant: 'checkerboard' },
    { mode: 'flip', keys: ['flipA', 'flipB'], variant: 'flip-plane' },
    { mode: 'transition', keys: ['transitionA', 'transitionB'], variant: 'fade' },
    { mode: 'effect', keys: ['effect'], variant: 'pan-zoom' },
    { mode: 'text', keys: ['text'], variant: 'text-fill' }
];

for (const { mode, keys, variant } of libraryModes) {
    if (mode !== 'effect') {
        test(`Carousel GL ${mode}: loading respects an existing pause and resumes once`, async t => {
            const harness = createHarness(scripts[0]);
            t.after(() => harness.api.detachAllOverlays());
            const assets = Object.fromEntries(keys.map(key => [key, `Assets/${key}.jpg`]));
            harness.api.attachOverlay('test', mode, 10, 20, 240, 120, assets, variant);
            harness.api.setOverlayActive('test', false);
            harness.finishImages();
            await nextTurn();
            assert.equal(harness.frames.size, 0);
            harness.api.setOverlayActive('test', true);
            assert.equal(harness.frames.size, 1);
        });
    }
    for (const oldLoadFails of [false, true]) {
        test(`Carousel GL ${mode}: old async ${oldLoadFails ? 'failure' : 'success'} cannot replace a newer effect`, async () => {
            const harness = createHarness(scripts[0]);
            const previousAssets = Object.fromEntries(keys.map(key => [key, `Assets/old-${key}.jpg`]));
            const currentAssets = Object.fromEntries(keys.map(key => [key, `Assets/new-${key}.jpg`]));
            harness.api.attachOverlay('test', mode, 10, 20, 240, 120, previousAssets, variant);
            const previousImages = harness.imageRequests.splice(0);
            harness.api.attachOverlay('test', mode, 10, 20, 240, 120, currentAssets, variant);
            harness.finishImages();
            await nextTurn();
            assert.equal(harness.frames.size, 1);
            for (const previousImage of previousImages) {
                if (oldLoadFails) previousImage.onerror();
                else previousImage.onload();
            }
            await nextTurn();
            assert.equal(harness.messages().length, 0, 'An obsolete response must not display an error over the current effect');
            assert.equal(harness.frames.size, 1, 'Only the current effect may own an animation loop');
            harness.api.detachAllOverlays();
            assert.equal(harness.body.children.length, 0);
            assert.equal(harness.frames.size, 0);
        });
    }
}

for (const outcome of ['success', 'image failure', 'shader failure']) {
    test(`Carousel GL Transitions: old async ${outcome} cannot affect a replacement on the same canvas`, async t => {
        const harness = createHarness(scripts[1]);
        t.after(() => harness.api.detachAllOverlays());
        harness.api.attachOverlay('test', 10, 20, 240, 120,
            { images: ['Assets/old-a.jpg', 'Assets/old-b.jpg'] }, 'fade', 0.6, 1);
        const canvas = harness.body.children.find(element => element.tagName === 'canvas');
        const previousState = canvas._floweryGlTransitions;
        const previousImages = harness.imageRequests.splice(0);
        const previousFetches = harness.fetchRequests.splice(0);
        assert.equal(previousImages.length, 2);
        assert.equal(previousFetches.length, 1);

        harness.api.attachOverlay('test', 10, 20, 240, 120,
            { images: ['Assets/new-a.jpg', 'Assets/new-b.jpg'] }, 'angular', 0.6, 1);
        const currentState = canvas._floweryGlTransitions;
        assert.notEqual(currentState, previousState);
        assert.equal(previousState.alive, false);
        assert.equal(harness.body.children.find(element => element.tagName === 'canvas'), canvas);
        assert.equal(harness.fetchRequests.length, 1, 'The replacement must load a separate shader');
        harness.finishFetches();
        harness.finishImages();
        await nextTurn();
        assert.equal(harness.frames.size, 1, 'The replacement must be running before old requests finish');

        for (const image of previousImages) {
            if (outcome === 'image failure') image.onerror();
            else image.onload();
        }
        for (const request of previousFetches) {
            if (outcome === 'shader failure') request.reject(new Error('Obsolete shader request failure'));
            else request.resolve({ ok: true, text: async () => 'vec4 transition(vec2 uv) { return getToColor(uv); }' });
        }
        await nextTurn();
        assert.equal(harness.messages().length, 0, 'Obsolete requests must not cover the replacement with an error');
        assert.equal(canvas._floweryGlTransitions, currentState);
        assert.equal(currentState.alive, true);
        assert.equal(harness.frames.size, 1, 'Only the replacement may own an animation loop');
    });
}

test('Carousel GL Transitions: a failed shader request leaves a removable error message', async () => {
    const harness = createHarness(scripts[1]);
    scripts[1].attach(harness.api);
    harness.finishFetches(true);
    harness.finishImages();
    await nextTurn();
    assert.equal(harness.messages().length, 1);
    assert.match(harness.messages()[0].textContent, /shader load failed/);
    harness.api.detachAllOverlays();
    assert.equal(harness.body.children.length, 0);
    assert.equal(harness.frames.size, 0);
});
