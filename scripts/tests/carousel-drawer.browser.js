// Build the Debug WASM Gallery, use a 600px-wide viewport and open
// ?drawer-test=normal or ?drawer-test=missing. Run this expression in the console.
// The missing case renames the real group before the production lookup.
// Actual Uno storyboards, Gallery services and WebGL renderers remain in use.
(async function checkCarouselDrawer() {
    const exports = await Module.getAssemblyExports("Flowery.Uno.Gallery.Core");
    const probe = exports.Flowery.Uno.Gallery.MainView;
    const state = () => JSON.parse(probe.GetDrawerProbeState());
    const frame = () => new Promise(resolve => requestAnimationFrame(resolve));
    const assert = (condition, message) => { if (!condition) throw new Error(message); };
    async function until(condition, message) {
        const deadline = performance.now() + 10000;
        while (!condition()) {
            assert(performance.now() < deadline, message);
            await frame();
        }
    }
    const initial = state();
    assert(initial.missingGroup !== initial.trackedGroup, "The group lookup does not match the requested fault.");
    if (initial.open) {
        probe.SetDrawerProbeOpen(false);
        await until(() => state().completedAt >= 0 && !state().paneVisible, "Initial pane closure did not complete.");
    }
    const results = [];

    for (const transitions of [false, true]) {
        probe.SelectDrawerProbeDemo(transitions);
        const canvas = () => [...document.querySelectorAll("canvas")].find(element =>
            transitions ? element._floweryGlTransitions?.program : element._floweryGl?._render);
        const visible = () => {
            const current = canvas();
            return !!current && current.style.display !== "none";
        };
        const renderer = () => transitions ? canvas()?._floweryGlTransitions : canvas()?._floweryGl;
        const opened = () => {
            const current = state();
            return current.state.startsWith("OpenOverlay") && !current.openingActive && current.paneVisible;
        };
        await until(() => canvas(), "The real GL renderer did not initialize.");
        const api = transitions ? window.FloweryCarouselGLTransitions : window.FloweryCarouselGL;
        const original = api.setOverlayActive;
        let prematureActivations = 0;
        api.setOverlayActive = function (...args) {
            const current = state();
            if (args[1] && current.startedAt >= 0 && !current.open && current.completedAt < 0)
                prematureActivations++;
            return original.apply(this, args);
        };
        try {
            for (const speed of [1, 0.1]) {
                await until(() => visible() && !renderer().paused, "The renderer was not running before opening.");
                probe.SetDrawerProbeSpeed(speed);
                probe.SetDrawerProbeOpen(true);
                await until(opened, "The pane did not finish opening.");
                assert(!visible() && (!renderer() || renderer().paused),
                    "An open pane must hide and pause any attached GL renderer.");
                probe.SetDrawerProbeOpen(false);
                let activeFrames = 0;
                let prematureFrames = 0;
                let firstVisibleAt = -1;
                await until(() => {
                    const current = state();
                    const isVisible = visible();
                    if (current.closingActive) activeFrames++;
                    if (current.closingActive && renderer())
                        assert(renderer().paused, "The renderer must stay paused during closure.");
                    if (isVisible && current.completedAt < 0) prematureFrames++;
                    if (isVisible && firstVisibleAt < 0) firstVisibleAt = performance.now();
                    return current.completedAt >= 0 && isVisible;
                }, "The renderer did not resume after the closing storyboard.");
                const closed = state();
                assert(activeFrames > 0, "No actual closing-animation frame was observed.");
                assert(prematureFrames === 0 && prematureActivations === 0, "GL became visible before Storyboard.Completed.");
                assert(!closed.paneVisible && !closed.watching, "The pane or closure watcher remained active.");
                assert(firstVisibleAt >= closed.completedAt, "GL resumed before animation completion.");
                assert(!renderer().paused, "The visible renderer did not resume.");
                const elapsed = closed.completedAt - closed.startedAt;
                assert(speed === 1 || elapsed >= 500, "The slowed animation did not actually run slowly.");
                results.push({ transitions, speed, activeFrames, prematureFrames, prematureActivations,
                    animationMs: Math.round(elapsed), resumeAfterCompletedMs: Math.round(firstVisibleAt - closed.completedAt) });
            }

            // Interrupt a slow close by reopening; its completion must not release the overlay.
            probe.SetDrawerProbeOpen(true);
            await until(opened, "The pane did not reopen.");
            probe.SetDrawerProbeOpen(false);
            await until(() => state().closingActive, "The interrupted closing animation did not start.");
            probe.SetDrawerProbeOpen(true);
            await until(opened, "Reopening did not finish.");
            assert(!visible() && !state().watching,
                "Reopening must keep GL hidden and stop the closure watcher.");
            probe.SetDrawerProbeOpen(false);
            await until(() => state().completedAt >= 0 && visible(),
                "The renderer did not resume after the final close.");
            assert(prematureActivations === 0, "An interrupted close released GL too early.");
        } finally {
            api.setOverlayActive = original;
            probe.SetDrawerProbeSpeed(1);
        }
    }
    return { passed: true, missingGroup: initial.missingGroup, results };
})();
