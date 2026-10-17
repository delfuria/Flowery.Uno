using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace Flowery.Uno.Gallery
{
    /// <summary>
    /// Pads elements by the part of them that lies under system overlays on mobile: status bar, navigation or
    /// gesture bar and display cutout.
    /// </summary>
    /// <remarks>
    /// Replaces Uno Toolkit's SafeArea for the Gallery shell. SafeArea drops its event subscriptions once its element
    /// is unloaded, so after Android reloads the visual tree (e.g. when the navigation mode changes) its insets stay
    /// stale. This helper subscribes on every Loaded of its host and unsubscribes on Unloaded.
    /// The targets must not set a padding of their own; it is replaced by the insets.
    /// </remarks>
    internal sealed class GallerySafeAreaPadding
    {
        private readonly FrameworkElement _host;
        private readonly FrameworkElement[] _targets;
        private ApplicationView? _applicationView;

        public GallerySafeAreaPadding(FrameworkElement host, params FrameworkElement[] targets)
        {
            _host = host;
            _targets = targets;

            host.Loaded += OnHostLoaded;
            host.Unloaded += OnHostUnloaded;
        }

        private void OnHostLoaded(object sender, RoutedEventArgs e)
        {
            Unsubscribe();

            _applicationView = ApplicationView.GetForCurrentView();
            _applicationView.VisibleBoundsChanged += OnVisibleBoundsChanged;
            _host.LayoutUpdated += OnLayoutUpdated;

            Update();
        }

        private void OnHostUnloaded(object sender, RoutedEventArgs e) => Unsubscribe();

        private void Unsubscribe()
        {
            if (_applicationView is { } view)
            {
                view.VisibleBoundsChanged -= OnVisibleBoundsChanged;
                _applicationView = null;
            }

            _host.LayoutUpdated -= OnLayoutUpdated;
        }

        private void OnVisibleBoundsChanged(ApplicationView sender, object args) => Update();

        // Element positions change without a VisibleBoundsChanged (e.g. the pane opening), so re-check after layout
        private void OnLayoutUpdated(object? sender, object e) => Update();

        private void Update()
        {
            if (_host.XamlRoot is not { } xamlRoot || _applicationView is not { } view)
                return;

            var windowSize = xamlRoot.Size;
            var visibleBounds = view.VisibleBounds;

            if (windowSize.Width <= 0 || windowSize.Height <= 0 || visibleBounds.Width <= 0 || visibleBounds.Height <= 0)
                return;

            // During a rotation the visible bounds can briefly still belong to the old orientation and stick out of
            // the window; wait for the update. (Comparing orientations instead fails on near-square foldable screens,
            // where the visible bounds of a portrait window are landscape.)
            if (visibleBounds.Left < -0.5 || visibleBounds.Top < -0.5
                || visibleBounds.Right > windowSize.Width + 0.5 || visibleBounds.Bottom > windowSize.Height + 0.5)
                return;

            var windowInsets = new Thickness(
                visibleBounds.Left,
                visibleBounds.Top,
                windowSize.Width - visibleBounds.Right,
                windowSize.Height - visibleBounds.Bottom);

            foreach (var target in _targets)
            {
                if (GetOverlap(target, visibleBounds, windowInsets) is { } padding)
                    ApplyPadding(target, padding);
            }
        }

        private static Thickness? GetOverlap(FrameworkElement target, Rect visibleBounds, Thickness windowInsets)
        {
            // A hidden element (e.g. the closed overlay pane) keeps its padding until it is laid out again
            if (target.ActualWidth <= 0 || target.ActualHeight <= 0)
                return null;

            var bounds = target.TransformToVisual(null).TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight));

            var left = ClampInset(visibleBounds.Left - bounds.Left, windowInsets.Left);
            var top = ClampInset(visibleBounds.Top - bounds.Top, windowInsets.Top);
            var right = ClampInset(bounds.Right - visibleBounds.Right, windowInsets.Right);
            var bottom = ClampInset(bounds.Bottom - visibleBounds.Bottom, windowInsets.Bottom);

            // The overlap is physical, the padding follows the element's flow direction
            return target.FlowDirection == FlowDirection.RightToLeft
                ? new Thickness(right, top, left, bottom)
                : new Thickness(left, top, right, bottom);
        }

        private static double ClampInset(double overlap, double windowInset) => Math.Clamp(overlap, 0, Math.Max(0, windowInset));

        private static void ApplyPadding(FrameworkElement target, Thickness padding)
        {
            switch (target)
            {
                case Border border when !AreClose(border.Padding, padding):
                    border.Padding = padding;
                    break;
                case Grid grid when !AreClose(grid.Padding, padding):
                    grid.Padding = padding;
                    break;
            }
        }

        // Ignore sub-pixel differences so a layout pass caused by the padding does not trigger another update
        private static bool AreClose(Thickness a, Thickness b) =>
            Math.Abs(a.Left - b.Left) < 0.5
            && Math.Abs(a.Top - b.Top) < 0.5
            && Math.Abs(a.Right - b.Right) < 0.5
            && Math.Abs(a.Bottom - b.Bottom) < 0.5;
    }
}
