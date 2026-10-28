using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Flowery.Controls;
using Flowery.Controls.Scheduler.Models;
using Flowery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.Foundation;
using Windows.System;
using static Flowery.Uno.RuntimeTests.Controls.Scheduler.SchedulerScope;

namespace Flowery.Uno.RuntimeTests.Controls.Scheduler;

/// <summary>Counterparts of Flowery.NET's SchedulerZoomTests: the column zoom and its gestures.</summary>
[TestClass]
public class Given_SchedulerZoom
{
    [TestMethod]
    [DataRow(SchedulerView.Day)]
    [DataRow(SchedulerView.Week)]
    [DataRow(SchedulerView.Month)]
    [DataRow(SchedulerView.Planner)]
    [DataRow(SchedulerView.Timeline)]
    public async Task When_TheColumnZoomChanges_TheColumnsWidenWithoutBuildingTheViewAgain(SchedulerView view)
    {
        using var scope = await CreateAsync(view);
        var body = BodyScroll(scope);
        var fitted = body.ExtentWidth;
        var frame = scope.Frame(scope.Appointment);

        scope.Scheduler.ColumnZoom = 2;
        await scope.LayoutAsync();

        // Layout rounds widths to device pixels.
        Assert.IsTrue(await WaitUntilAsync(() => Math.Abs(body.ExtentWidth - fitted * 2) <= 2),
            $"{view}: the extent is {body.ExtentWidth}, not twice {fitted}.");
        Assert.AreSame(frame, scope.Frame(scope.Appointment), view.ToString());
    }

    [TestMethod]
    public async Task When_TheColumnZoomIsSetOutsideItsRange_ItStaysBetweenOneAndFour()
    {
        using var scope = await CreateAsync(SchedulerView.Week);
        scope.Scheduler.ColumnZoom = 10;
        Assert.AreEqual(4, scope.Scheduler.ColumnZoom);
        scope.Scheduler.ColumnZoom = 0.5;
        Assert.AreEqual(1, scope.Scheduler.ColumnZoom);
    }

    [TestMethod]
    public async Task When_CtrlAndTheWheelTurn_TheColumnsZoomAndTheDayUnderThePointerStays()
    {
        using var scope = await CreateAsync(SchedulerView.Week);
        var body = BodyScroll(scope);
        var x = body.ViewportWidth * 3 / 4;

        Assert.IsFalse(scope.Scheduler.ZoomByWheel(VirtualKeyModifiers.None, 120, x), "The wheel alone scrolls.");
        Assert.AreEqual(1, scope.Scheduler.ColumnZoom);

        Assert.IsTrue(scope.Scheduler.ZoomByWheel(VirtualKeyModifiers.Control, 120, x));
        await scope.LayoutAsync();
        Assert.AreEqual(1.25, scope.Scheduler.ColumnZoom, 0.001);
        // What was at x is still at x.
        Assert.IsTrue(await WaitUntilAsync(() => Math.Abs(body.HorizontalOffset - x * 0.25) <= 2),
            $"The offset is {body.HorizontalOffset}, not {x * 0.25}.");
    }

    [TestMethod]
    public async Task When_AHeadingIsTappedTwice_TheColumnsSwitchBetweenFittedAndWide_AndSwipesDoNot()
    {
        using var scope = await CreateAsync(SchedulerView.Week);
        var middle = scope.Center(WeekHeading(scope));

        // A week does not scroll sideways, so the swipes start where the taps come.
        await scope.TouchDragAsync(middle, new Point(middle.X + 40, middle.Y));
        await scope.TouchDragAsync(middle, new Point(middle.X + 40, middle.Y));
        Assert.AreEqual(1, scope.Scheduler.ColumnZoom);
        await scope.TouchTapAsync(middle);
        Assert.AreEqual(1, scope.Scheduler.ColumnZoom);
        await scope.TouchTapAsync(middle, elapsedMicroseconds: 100_000);
        Assert.AreEqual(2, scope.Scheduler.ColumnZoom);
    }

    [TestMethod]
    public async Task When_AMonthHeadingIsDoubleClicked_TheColumnsSwitchBetweenFittedAndWide()
    {
        using var scope = await CreateAsync(SchedulerView.Month);
        var heading = scope.Center(Heading(scope, "ddd"));

        await scope.DoubleClickAsync(heading, heading, 100_000);
        Assert.AreEqual(2, scope.Scheduler.ColumnZoom);
        await scope.DoubleClickAsync(heading, heading, 100_000);
        Assert.AreEqual(1, scope.Scheduler.ColumnZoom);
    }

    [TestMethod]
    [DataRow(SchedulerView.Week)]
    [DataRow(SchedulerView.Month)]
    [DataRow(SchedulerView.Planner)]
    public async Task When_TheRightEdgeOfAHeadingIsDraggedWithTheMouse_EveryColumnResizes(SchedulerView view)
    {
        using var scope = await CreateAsync(view);
        var body = BodyScroll(scope);
        var cell = Heading(scope, view == SchedulerView.Month ? "ddd" : "ddd, d MMM");
        // A Planner day is wider than the view: scroll its right edge into the middle. The heading follows the body.
        var right = cell.TransformToVisual(body).TransformPoint(default).X + body.HorizontalOffset + cell.ActualWidth;
        body.ChangeView(Math.Max(0, right - body.ViewportWidth / 2), null, null, true);
        await scope.SettleScrollAsync();
        await scope.LayoutAsync();
        var width = cell.ActualWidth;
        var left = cell.TransformToVisual(body).TransformPoint(default).X;
        var drag = Math.Min(width / 2, 300);

        // A drag from inside the heading changes nothing.
        var inside = scope.Point(cell, width / 2, cell.ActualHeight / 2);
        await scope.DragAsync(inside, new Point(inside.X + drag, inside.Y));
        Assert.AreEqual(1, scope.Scheduler.ColumnZoom, view.ToString());

        var edge = scope.Point(cell, width - 2, cell.ActualHeight / 2);
        await scope.DragAsync(edge, new Point(edge.X + drag, edge.Y));

        Assert.AreEqual((width + drag) / width, scope.Scheduler.ColumnZoom, 0.01, view.ToString());
        Assert.IsTrue(await WaitUntilAsync(() => Math.Abs(cell.ActualWidth - (width + drag)) <= 2),
            $"{view}: the heading is {cell.ActualWidth} wide, not {width + drag}.");
        // The left edge of the column stays where it was.
        Assert.IsTrue(await WaitUntilAsync(() => Math.Abs(cell.TransformToVisual(body).TransformPoint(default).X - left) <= 2),
            $"{view}: the heading moved from {left} to {cell.TransformToVisual(body).TransformPoint(default).X}.");
    }

    [TestMethod]
    public async Task When_TwoFingersSpreadAndPinchOnTheBody_TheColumnsZoom()
    {
        using var scope = await CreateAsync(SchedulerView.Week);
        var body = BodyScroll(scope);
        var middle = scope.Point(body, body.ViewportWidth / 2, body.ViewportHeight / 2);

        Pinch(scope.Scheduler, middle, from: 30, to: 60);
        Assert.AreEqual(2, scope.Scheduler.ColumnZoom, 0.01);

        // Lifting both fingers ends the pinch, so the next one scales the zoom it starts at.
        Pinch(scope.Scheduler, middle, from: 60, to: 45);
        Assert.AreEqual(1.5, scope.Scheduler.ColumnZoom, 0.01);
        Assert.IsNull(scope.Scheduler.SelectedAppointment);
    }

    [TestMethod]
    public async Task When_APinchStartsOnAnAppointment_TheAppointmentIsNeitherSelectedNorMoved()
    {
        using var scope = await CreateAsync(SchedulerView.Week);
        var appointment = scope.Center(scope.Frame(scope.Appointment));
        var moved = false;
        scope.Scheduler.AppointmentMoveRequested += (_, _) => moved = true;

        // The first finger lands on the appointment, which a lift would select, then the second one lands.
        scope.TouchDown(appointment);
        scope.Scheduler.Pinch.Press(1, appointment);
        scope.Scheduler.Pinch.Press(2, new Point(appointment.X + 60, appointment.Y));
        scope.Scheduler.Pinch.Move(2, new Point(appointment.X + 120, appointment.Y));
        scope.Scheduler.MovePointer(new DaisyScheduler.PointerInput(scope.Scheduler.XamlRoot!.Content!, appointment, true, false,
            VirtualKeyModifiers.None, 0, isTouch: true));
        scope.TouchUp(appointment);
        scope.Scheduler.Pinch.Lift(1);
        scope.Scheduler.Pinch.Lift(2);
        await scope.LayoutAsync();

        Assert.AreEqual(2, scope.Scheduler.ColumnZoom, 0.01);
        Assert.IsNull(scope.Scheduler.SelectedAppointment);
        Assert.IsFalse(moved);
    }

    [TestMethod]
    public async Task When_TheSettingsMenuChoosesAColumnWidth_TheColumnZoomFollows()
    {
        using var scope = await CreateAsync(SchedulerView.Week);
        var settings = new StackPanel();
        typeof(DaisyScheduler).GetMethod("AddPreferences", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(scope.Scheduler, new object[] { settings });
        var zoom = settings.Children.OfType<DaisySelect>().Single(select => Equals(select.Tag, DaisyScheduler.ColumnZoomProperty));

        Assert.AreEqual(1d.ToString("P0", scope.Scheduler.FormattingCulture), zoom.SelectedItem?.ToString());
        zoom.SelectedIndex = 2;
        Assert.AreEqual(2, scope.Scheduler.ColumnZoom);
    }

    [TestMethod]
    public async Task When_AStateKeyIsSet_TheZoomOfTheUserIsKeptForTheNextScheduler()
    {
        var previous = StateStorageProvider.Instance;
        var storage = new InMemoryStateStorage();
        StateStorageProvider.Configure(storage);
        try
        {
            using (var plain = await CreateAsync(SchedulerView.Week))
            {
                plain.Scheduler.ColumnZoom = 3;
                await plain.LayoutAsync();
            }
            Assert.AreEqual(0, storage.SaveCount);

            using (var first = await CreateAsync(SchedulerView.Week))
            {
                first.Scheduler.StateKey = "zoom";
                first.Scheduler.ColumnZoom = 2;
                first.Scheduler.ColumnZoom = 2.5;
                await first.LayoutAsync();
            }
            // The changes of one turn are written once.
            Assert.AreEqual(1, storage.SaveCount);

            using var second = await CreateAsync(SchedulerView.Week);
            Assert.AreEqual(1, second.Scheduler.ColumnZoom);
            second.Scheduler.StateKey = "zoom";
            Assert.AreEqual(2.5, second.Scheduler.ColumnZoom);
        }
        finally
        {
            StateStorageProvider.Configure(previous);
        }
    }

    // Two fingers on both sides of the middle, from and to that distance from it, then both lift.
    private static void Pinch(DaisyScheduler scheduler, Point middle, double from, double to)
    {
        scheduler.Pinch.Press(1, new Point(middle.X - from, middle.Y));
        scheduler.Pinch.Press(2, new Point(middle.X + from, middle.Y));
        for (var step = 1; step <= 4; step++)
        {
            var distance = from + (to - from) * step / 4;
            scheduler.Pinch.Move(1, new Point(middle.X - distance, middle.Y));
            scheduler.Pinch.Move(2, new Point(middle.X + distance, middle.Y));
        }
        scheduler.Pinch.Lift(1);
        scheduler.Pinch.Lift(2);
    }

    // The scroll viewer of the view's body; the heading and the gutter follow it in frozen panes.
    private static ScrollViewer BodyScroll(SchedulerScope scope) => scope.Descendants<ScrollViewer>()
        .First(static scroll => scroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto &&
            scroll.VerticalScrollBarVisibility == ScrollBarVisibility.Auto);

    // The date and weekday of Monday above its Week column.
    private static FrameworkElement WeekHeading(SchedulerScope scope) =>
        (FrameworkElement)scope.Descendants<TextBlock>().First(text => text.Text == Monday.ToString("ddd, d MMM", scope.Scheduler.FormattingCulture)).Parent;

    // The heading cell of Monday: the Border around the label in this format.
    private static Border Heading(SchedulerScope scope, string format) =>
        Ancestors(scope.Descendants<TextBlock>().First(text => text.Text == Monday.ToString(format, scope.Scheduler.FormattingCulture)))
            .OfType<Border>().First();
}
