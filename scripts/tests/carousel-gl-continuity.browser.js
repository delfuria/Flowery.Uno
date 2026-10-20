// Run this expression in the Gallery page's browser console or through Runtime.evaluate
// with awaitPromise=true. Open Carousel GL Transitions in Slideshow mode, enable it,
// set Interval to 1 second, and leave the navigation closed or in Inline mode.
// This checks the real Gallery -> C# overlay service -> JavaScript renderer path.
(async function checkCarouselGlContinuity() {
    const canvas = Array.from(document.querySelectorAll("canvas"))
        .find(element => element._floweryGlTransitions?.program);
    if (!canvas) {
        throw new Error("Open and enable Carousel GL Transitions before running this check.");
    }

    const initialState = canvas._floweryGlTransitions;
    const started = performance.now();
    const report = {
        passed: false, transitions: [], frames: 0, hiddenFrames: 0,
        hiddenChanges: 0, pausedFrames: 0, replacedFrames: 0, programChanges: 0
    };
    let previousName = initialState.transitionName;
    let previousProgram = initialState.program;
    let frame;
    let timeout;
    let wasHidden = canvas.style.display === "none";

    return new Promise(resolve => {
        const observer = new MutationObserver(() => {
            const hidden = canvas.style.display === "none";
            if (hidden && !wasHidden) report.hiddenChanges++;
            wasHidden = hidden;
        });
        observer.observe(canvas, { attributes: true, attributeFilter: ["style"] });

        function finish() {
            observer.disconnect();
            cancelAnimationFrame(frame);
            clearTimeout(timeout);
            report.elapsedMs = Math.round(performance.now() - started);
            report.passed = report.transitions.length >= 4 && report.programChanges >= 4
                && report.hiddenFrames === 0 && report.hiddenChanges === 0
                && report.pausedFrames === 0 && report.replacedFrames === 0;
            resolve(report);
        }

        function sample() {
            const state = canvas._floweryGlTransitions;
            report.frames++;
            if (canvas.style.display === "none") report.hiddenFrames++;
            if (!state?.running || state.paused) report.pausedFrames++;
            if (!canvas.isConnected || state !== initialState) report.replacedFrames++;
            if (state?.transitionName !== previousName) {
                previousName = state?.transitionName;
                report.transitions.push(previousName);
            }
            if (state?.program !== previousProgram) {
                previousProgram = state?.program;
                report.programChanges++;
            }
            if (report.transitions.length >= 4 && report.programChanges >= 4) {
                finish();
                return;
            }
            frame = requestAnimationFrame(sample);
        }

        timeout = setTimeout(finish, 30000);
        frame = requestAnimationFrame(sample);
    });
})();
