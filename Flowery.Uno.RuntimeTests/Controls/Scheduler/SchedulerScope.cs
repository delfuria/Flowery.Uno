using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Flowery.Controls;
using Flowery.Controls.Scheduler.Models;
using Flowery.Controls.Scheduler.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;

namespace Flowery.Uno.RuntimeTests.Controls.Scheduler;

/// <summary>
/// Hosts a scheduler like the Flowery.NET headless window and drives it with synthesized pointer input:
/// each press is hit-tested in window coordinates and routed through the scheduler's own handlers.
/// </summary>
internal sealed class SchedulerScope : IDisposable
{
    public static readonly DateTime Monday = new(2026, 10, 12);

    // Presses one second apart never form a double-click.
    private ulong _timestamp = 1_000_000_000;

    public AppointmentModel Appointment { get; } = new() { Id = 1, Subject = "Install", Start = Monday.AddHours(9), End = Monday.AddHours(10), ResourceIds = { 1 } };
    public DaisyScheduler Scheduler { get; }
    public Border Host { get; }

    private SchedulerScope(SchedulerView view)
    {
        Scheduler = new DaisyScheduler
        {
            CurrentView = view, SelectedDate = new DateTimeOffset(Monday), Size = DaisySize.Medium,
            Appointments = new[] { Appointment },
            SchedulerResources = view is SchedulerView.Planner or SchedulerView.Timeline ? new[] { new ResourceModel { Id = 1, Name = "Alex" } } : Array.Empty<ResourceModel>()
        };
        Host = new Border
        {
            Width = 1100, Height = 720, Child = Scheduler,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
        };
    }

    public static async Task<SchedulerScope> CreateAsync(SchedulerView view)
    {
        var scope = new SchedulerScope(view);
        RuntimeTestHelpers.AttachToHost(scope.Host);
        await RuntimeTestHelpers.CompleteLayoutAsync(scope.Host, scope.Scheduler);
        await scope.LayoutAsync();
        return scope;
    }

    private UIElement Root => Scheduler.XamlRoot?.Content ?? throw new InvalidOperationException("The scheduler is not in a window.");

    /// <summary>Runs the deferred render and two layout passes, like the Avalonia test's RunJobs/UpdateLayout pairs.</summary>
    public async Task LayoutAsync()
    {
        for (var pass = 0; pass < 2; pass++)
            await RuntimeTestHelpers.CompleteLayoutAsync(Host);
    }

    public IEnumerable<T> Descendants<T>() where T : DependencyObject => Descendants<T>(Scheduler);

    public static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    public static IEnumerable<DependencyObject> Ancestors(DependencyObject element)
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            yield return parent;
    }

    public IEnumerable<Border> Frames(AppointmentModel appointment) => Descendants<Border>()
        .Where(border => border.IsTabStop && border.DataContext is AppointmentContext context && Equals(context.Appointment.Id, appointment.Id));

    public Border Frame(AppointmentModel appointment) => Frames(appointment).First();

    public Point Point(UIElement element, double x, double y) => element.TransformToVisual(Root).TransformPoint(new Point(x, y));

    public Point Center(FrameworkElement element) => Point(element, element.ActualWidth / 2, element.ActualHeight / 2);

    /// <param name="elapsedMicroseconds">Time since the previous press; the default never forms a double-click.</param>
    public bool MouseDown(Point point, bool right = false, VirtualKeyModifiers modifiers = VirtualKeyModifiers.None,
        ulong elapsedMicroseconds = 1_000_000)
    {
        _timestamp += elapsedMicroseconds;
        var hit = HitTest(Scheduler, point);
        return hit is not null && Scheduler.RoutePointerPressed(hit, Input(point, !right, right, modifiers));
    }

    // VisualTreeHelper.FindElementsInHostCoordinates returns only the scheduler itself on Skia, so walk the tree:
    // the topmost visible, hit-testable element under the point, with Canvas.ZIndex and scroll viewports applied.
    private FrameworkElement? HitTest(FrameworkElement element, Point point)
    {
        if (element.Visibility != Visibility.Visible || !element.IsHitTestVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return null;
        var local = Root.TransformToVisual(element).TransformPoint(point);
        var inside = local.X >= 0 && local.Y >= 0 && local.X < element.ActualWidth && local.Y < element.ActualHeight;
        if (!inside && element is ScrollViewer)
            return null;
        var children = Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(element))
            .Select(index => VisualTreeHelper.GetChild(element, index)).OfType<FrameworkElement>()
            .Select((child, order) => (Child: child, Order: order))
            .OrderByDescending(item => Canvas.GetZIndex(item.Child)).ThenByDescending(item => item.Order);
        foreach (var (child, _) in children)
            if (HitTest(child, point) is { } hit)
                return hit;
        var receivesInput = element is Control or TextBlock || element is Border { Background: not null } || element is Panel { Background: not null };
        return inside && receivesInput ? element : null;
    }

    public void MouseMove(Point point) => Scheduler.MovePointer(Input(point, true, false, VirtualKeyModifiers.None));

    public void MouseUp(Point point, VirtualKeyModifiers modifiers = VirtualKeyModifiers.None) =>
        Scheduler.ReleasePointer(Input(point, false, false, modifiers));

    private DaisyScheduler.PointerInput Input(Point point, bool left, bool right, VirtualKeyModifiers modifiers, bool touch = false) =>
        new(Root, point, left, right, modifiers, _timestamp, isTouch: touch);

    public async Task ClickAsync(FrameworkElement element, bool right = false, VirtualKeyModifiers modifiers = VirtualKeyModifiers.None)
    {
        var point = Center(element);
        MouseDown(point, right, modifiers);
        MouseUp(point, modifiers);
        await LayoutAsync();
    }

    /// <summary>Presses at <paramref name="first"/>, then at <paramref name="second"/> after <paramref name="elapsedMicroseconds"/>.</summary>
    public async Task DoubleClickAsync(Point first, Point second, ulong elapsedMicroseconds)
    {
        MouseDown(first);
        MouseUp(first);
        MouseDown(second, elapsedMicroseconds: elapsedMicroseconds);
        MouseUp(second);
        await LayoutAsync();
    }

    public async Task DragAsync(Point origin, Point destination, VirtualKeyModifiers modifiers = VirtualKeyModifiers.None)
    {
        MouseDown(origin, modifiers: modifiers);
        MouseMove(destination);
        MouseUp(destination, modifiers);
        await LayoutAsync();
    }

    /// <summary>A touch press at <paramref name="origin"/>, a move to <paramref name="destination"/> and a release.</summary>
    public async Task TouchDragAsync(Point origin, Point destination)
    {
        _timestamp += 1_000_000;
        var hit = HitTest(Scheduler, origin) ?? throw new InvalidOperationException("Nothing to touch at " + origin);
        Scheduler.RoutePointerPressed(hit, Input(origin, true, false, VirtualKeyModifiers.None, touch: true));
        Scheduler.MovePointer(Input(destination, true, false, VirtualKeyModifiers.None, touch: true));
        Scheduler.ReleasePointer(Input(destination, false, false, VirtualKeyModifiers.None, touch: true));
        await LayoutAsync();
    }

    /// <summary>A touch press and release at <paramref name="point"/> without moving.</summary>
    /// <param name="elapsedMicroseconds">Time since the previous press; the default never forms a double tap.</param>
    public async Task TouchTapAsync(Point point, VirtualKeyModifiers modifiers = VirtualKeyModifiers.None, ulong elapsedMicroseconds = 1_000_000)
    {
        _timestamp += elapsedMicroseconds;
        var hit = HitTest(Scheduler, point) ?? throw new InvalidOperationException("Nothing to touch at " + point);
        Scheduler.RoutePointerPressed(hit, Input(point, true, false, modifiers, touch: true));
        Scheduler.ReleasePointer(Input(point, false, false, modifiers, touch: true));
        await LayoutAsync();
    }

    public void Dispose() => RuntimeTestHelpers.GetHostPanel().Children.Clear();
}
