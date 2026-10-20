#if DEBUG && __WASM__
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Flowery.Uno.Gallery;

// Explicit opt-in browser regression probe. Neither this implementation nor its
// exports are present in Release. Renaming the group preserves Uno's real states.
public sealed partial class MainView
{
    private static WeakReference<MainView>? _drawerProbe;
    private readonly List<Storyboard> _probeClosingStoryboards = new();
    private readonly Dictionary<DoubleAnimation, TimeSpan> _probeClosingDurations = new();
    private VisualStateGroup? _probeDisplayStates;
    private bool _probeMissingGroup;
    private double _probeCompletedAt = -1;
    private double _probeCloseStartedAt = -1;

    partial void PreparePaneStateTest(FrameworkElement templateRoot)
    {
        var query = DrawerProbeEval("globalThis.location.search");
        if (!query.Contains("drawer-test=", StringComparison.Ordinal))
            return;

        var group = VisualStateManager.GetVisualStateGroups(templateRoot)
            .Single(stateGroup => stateGroup.Name == "DisplayModeStates");
        _probeDisplayStates = group;
        _probeMissingGroup = query.Contains("drawer-test=missing", StringComparison.Ordinal);
        if (_probeMissingGroup)
            group.Name = "DrawerProbeRenamedStates";

        foreach (var transition in group.Transitions)
        {
            if (transition.To == "Closed" && transition.Storyboard is { } storyboard)
            {
                _probeClosingStoryboards.Add(storyboard);
                storyboard.Completed += OnProbeClosingCompleted;
            }
        }
        if (_probeClosingStoryboards.Count == 0)
            throw new InvalidOperationException("The probe requires real closing storyboards.");
        foreach (var animation in _probeClosingStoryboards.SelectMany(storyboard => storyboard.Children)
                     .OfType<DoubleAnimation>())
        {
            if (!animation.Duration.HasTimeSpan || animation.Duration.TimeSpan <= TimeSpan.Zero)
                throw new InvalidOperationException("The probe requires explicit positive closing durations.");
            _probeClosingDurations.TryAdd(animation, animation.Duration.TimeSpan);
        }
        if (_probeClosingDurations.Count == 0)
            throw new InvalidOperationException("The probe requires real closing DoubleAnimations.");
        _drawerProbe = new WeakReference<MainView>(this);
    }

    partial void ReleasePaneStateTest()
    {
        foreach (var storyboard in _probeClosingStoryboards)
            storyboard.Completed -= OnProbeClosingCompleted;
        _probeClosingStoryboards.Clear();
        foreach (var (animation, originalDuration) in _probeClosingDurations)
            animation.Duration = new Duration(originalDuration);
        _probeClosingDurations.Clear();
        if (_probeDisplayStates != null)
            _probeDisplayStates.Name = "DisplayModeStates";
        _probeDisplayStates = null;
        _drawerProbe = null;
    }

    private void OnProbeClosingCompleted(object? sender, object e) =>
        _probeCompletedAt = double.Parse(DrawerProbeEval("String(performance.now())"), System.Globalization.CultureInfo.InvariantCulture);

    private static MainView ProbeView() =>
        _drawerProbe?.TryGetTarget(out var view) == true
            ? view : throw new InvalidOperationException("Open a Debug Gallery with ?drawer-test=normal or ?drawer-test=missing.");

    [JSExport]
    [SupportedOSPlatform("browser")]
    public static void SelectDrawerProbeDemo(bool transitions) =>
        ProbeView().NavigateToCategory(transitions ? "Sidebar_CarouselGLTransitions" : "Sidebar_CarouselGL");

    [JSExport]
    [SupportedOSPlatform("browser")]
    public static void SetDrawerProbeOpen(bool open)
    {
        var view = ProbeView();
        if (view.MainSplitView.DisplayMode != SplitViewDisplayMode.Overlay)
            throw new InvalidOperationException("The probe requires an Overlay pane. Use a narrow browser viewport.");
        if (!open)
        {
            view._probeCompletedAt = -1;
            view._probeCloseStartedAt = double.Parse(DrawerProbeEval("String(performance.now())"), System.Globalization.CultureInfo.InvariantCulture);
        }
        view.MainSplitView.IsPaneOpen = open;
    }

    [JSExport]
    [SupportedOSPlatform("browser")]
    public static void SetDrawerProbeSpeed(double speed)
    {
        if (speed <= 0 || speed > 1)
            throw new ArgumentOutOfRangeException(nameof(speed));
        // Uno 6.7 does not implement Timeline.SpeedRatio on WASM/Skia.
        // Scale the real closing animations, preserving their targets and easing.
        foreach (var (animation, originalDuration) in ProbeView()._probeClosingDurations)
            animation.Duration = new Duration(TimeSpan.FromTicks((long)(originalDuration.Ticks / speed)));
    }

    [JSExport]
    [SupportedOSPlatform("browser")]
    public static string GetDrawerProbeState()
    {
        var view = ProbeView();
        static string Bool(bool value) => value ? "true" : "false";
        var active = view._probeClosingStoryboards.Any(storyboard => storyboard.GetCurrentState() == ClockState.Active);
        var opening = view._probeDisplayStates?.Transitions.Any(transition =>
            transition.To?.StartsWith("OpenOverlay", StringComparison.Ordinal) == true &&
            transition.Storyboard?.GetCurrentState() == ClockState.Active) == true;
        return FormattableString.Invariant(
            $"{{\"missingGroup\":{Bool(view._probeMissingGroup)},\"trackedGroup\":{Bool(view._paneDisplayStates != null)},\"state\":\"{view._probeDisplayStates?.CurrentState?.Name}\",\"open\":{Bool(view.MainSplitView.IsPaneOpen)},\"paneVisible\":{Bool(view.IsPaneVisible())},\"closingActive\":{Bool(active)},\"openingActive\":{Bool(opening)},\"watching\":{Bool(view._isWaitingForPaneClosure)},\"startedAt\":{view._probeCloseStartedAt},\"completedAt\":{view._probeCompletedAt}}}");
    }

    [JSImport("globalThis.eval")]
    private static partial string DrawerProbeEval(string expression);
}
#endif
