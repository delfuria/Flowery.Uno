using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Flowery.Controls;
using Flowery.Controls.Scheduler.Models;
using Flowery.Controls.Scheduler.Rendering;
using Flowery.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.Foundation;
using Windows.System;
using static Flowery.Uno.RuntimeTests.Controls.Scheduler.SchedulerScope;

namespace Flowery.Uno.RuntimeTests.Controls.Scheduler;

/// <summary>Counterparts of Flowery.NET's SchedulerLayoutTests and SchedulerCalendarTests.</summary>
[TestClass]
public class Given_SchedulerLayout
{
    [TestMethod]
    public async Task When_HostResizes_AppointmentAndToolbarControlsStayAttached()
    {
        foreach (var view in Enum.GetValues<SchedulerView>())
        {
            using var scope = await CreateAsync(view);
            scope.Scheduler.Size = DaisySize.ExtraLarge;
            var renders = 0;
            scope.Scheduler.AppointmentRenderer = _ => { renders++; return null; };
            await scope.LayoutAsync();
            var frame = scope.Frame(scope.Appointment);
            var scroll = Ancestors(frame).OfType<ScrollViewer>().First();
            var dateButton = scope.Descendants<DaisyButton>().Single(button => Equals(button.Tag, "Date"));
            scope.Scheduler.SelectedAppointment = scope.Appointment;
            var before = renders;
            var detachments = 0;
            frame.Unloaded += (_, _) => detachments++;
            foreach (var width in new[] { 900d, 760d, 1080d })
            {
                var offset = new Point(scroll.HorizontalOffset, scroll.VerticalOffset);
                scope.Host.Width = width;
                scope.Host.Height = 680;
                await scope.LayoutAsync();
                Assert.AreSame(frame, scope.Frame(scope.Appointment), view.ToString());
                Assert.AreSame(scroll, Ancestors(frame).OfType<ScrollViewer>().First(), view.ToString());
                Assert.IsTrue(scope.Descendants<DaisyButton>().Contains(dateButton), view.ToString());
                Assert.AreEqual(Math.Min(offset.X, Math.Max(0, scroll.ExtentWidth - scroll.ViewportWidth)), scroll.HorizontalOffset, 1, view.ToString());
                Assert.AreEqual(Math.Min(offset.Y, Math.Max(0, scroll.ExtentHeight - scroll.ViewportHeight)), scroll.VerticalOffset, 1, view.ToString());
            }
            Assert.AreEqual(before, renders, view.ToString());
            Assert.AreEqual(0, detachments, view.ToString());
            Assert.AreSame(scope.Appointment, scope.Scheduler.SelectedAppointment, view.ToString());
        }
    }

    [TestMethod]
    public async Task When_WeekOverlaps_SubjectAndTimeStayUnclipped_AtEverySize()
    {
        foreach (var (size, clock) in new[]
        {
            (DaisySize.ExtraSmall, SchedulerClockFormat.TwentyFourHour), (DaisySize.Small, SchedulerClockFormat.TwentyFourHour),
            (DaisySize.Medium, SchedulerClockFormat.TwentyFourHour), (DaisySize.Large, SchedulerClockFormat.TwentyFourHour),
            (DaisySize.ExtraLarge, SchedulerClockFormat.TwentyFourHour), (DaisySize.ExtraLarge, SchedulerClockFormat.TwelveHour)
        })
        {
            using var scope = await CreateAsync(SchedulerView.Week);
            scope.Scheduler.Size = size;
            scope.Scheduler.ClockFormat = clock;
            scope.Appointment.Description = "A note that must yield to the subject and time";
            scope.Appointment.Tiles.Add(new AppointmentDecoration { Text = "Alex", Position = DecorationPosition.Bottom });
            scope.Appointment.RecurrenceRule = "FREQ=WEEKLY";
            scope.Appointment.Indicators.Add(new AppointmentIndicator { Glyph = "↻", Tooltip = "Recurring" });
            var overlap = scope.Appointment.Clone();
            overlap.Id = 2;
            overlap.Subject = "Overlap";
            var longAppointment = scope.Appointment.Clone();
            longAppointment.Id = 3;
            longAppointment.Start = Monday.AddDays(1).AddHours(9);
            longAppointment.End = longAppointment.Start.AddHours(3);
            scope.Scheduler.Appointments = new[] { scope.Appointment, overlap, longAppointment };
            scope.Host.Width = 900;
            await scope.LayoutAsync();
            var label = $"{size}/{clock}";
            var frame = scope.Frame(scope.Appointment);
            var context = (AppointmentContext)frame.DataContext;
            var title = Descendants<TextBlock>(frame).Single(text => text.Text == context.Subject);
            var time = Descendants<TextBlock>(frame).Single(text => text.Text == context.TimeRange);
            AssertFullyInside(title, frame, label);
            AssertFullyInside(time, frame, label);
            var fullTime = new TextBlock { Text = time.Text, FontSize = time.FontSize, FontFamily = time.FontFamily };
            fullTime.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.IsTrue(time.ActualWidth + 1 >= fullTime.DesiredSize.Width,
                $"{label}: time width {time.ActualWidth}, required {fullTime.DesiredSize.Width}, font {time.FontSize}, frame {frame.ActualWidth}x{frame.ActualHeight}");
            Assert.HasCount(1, Descendants<TextBlock>(frame).Where(text => text.Text == "↻"));
            foreach (var text in Descendants<TextBlock>(frame).Where(text => IsShown(text) &&
                         Ancestors(text).TakeWhile(ancestor => !ReferenceEquals(ancestor, frame)).OfType<FrameworkElement>().All(IsShown)))
                AssertFullyInside(text, frame, label);
            var tallFrame = scope.Frame(longAppointment);
            var badge = Descendants<TextBlock>(tallFrame).Single(text => text.Text == "Alex");
            AssertFullyInside(badge, tallFrame, label);
            Assert.IsTrue(IsShown(badge), label);
        }
    }

    // Rows that do not fit keep their desired size in WinUI, so the text panel hides them with an empty clip.
    private static bool IsShown(FrameworkElement element) =>
        element.ActualHeight > 0 && element.Clip is not RectangleGeometry { Rect.Width: 0 };

    private static void AssertFullyInside(FrameworkElement child, FrameworkElement parent, string label)
    {
        var origin = child.TransformToVisual(parent).TransformPoint(default);
        Assert.IsTrue(IsShown(child), label + ": expected a visible row");
        Assert.IsTrue(origin.X >= -1 && origin.Y >= -1 &&
            origin.X + child.ActualWidth <= parent.ActualWidth + 1 &&
            origin.Y + child.ActualHeight <= parent.ActualHeight + 1,
            $"{label} {child} '{(child as TextBlock)?.Text}' font {(child as TextBlock)?.FontSize}: {origin}, {child.ActualWidth}x{child.ActualHeight} outside {parent.ActualWidth}x{parent.ActualHeight}; chain " + string.Join(" < ", Ancestors(child).TakeWhile(a => !ReferenceEquals(a, parent)).OfType<FrameworkElement>().Select(a => $"{a.GetType().Name} {a.ActualHeight:F1}@{a.TransformToVisual(parent).TransformPoint(default).Y:F1} desired {a.DesiredSize.Height:F1}")));
    }

    [TestMethod]
    public async Task When_CalendarRenders_AppointmentsAcrossTheMonthAreHighlighted_AndResourceKindsFiltered()
    {
        using var scope = await CreateAsync(SchedulerView.Calendar);
        scope.Scheduler.SelectedDate = new DateTimeOffset(Monday.AddDays(1));
        scope.Scheduler.SchedulerResources = new[]
        {
            new ResourceModel { Id = 1, Kind = ResourceKind.Person },
            new ResourceModel { Id = 2, Kind = ResourceKind.Room }
        };
        var spanning = new AppointmentModel { Id = 2, Start = Monday.AddDays(3).AddHours(9), End = Monday.AddDays(5), ResourceIds = { 2 } };
        var daily = new AppointmentModel { Id = 3, Start = Monday.AddDays(7).AddHours(9), End = Monday.AddDays(9).AddHours(10),
            SpanMode = AppointmentSpanMode.Daily, ResourceIds = { 2 } };
        scope.Scheduler.Appointments = new[] { scope.Appointment, spanning, daily };
        await scope.LayoutAsync();
        Assert.AreEqual(DaisyButtonVariant.Primary, DayButton(scope, Monday).Variant);
        Assert.AreEqual(DaisyButtonStyle.Soft, DayButton(scope, Monday).ButtonStyle);
        Assert.AreEqual(DaisyButtonStyle.Default, DayButton(scope, Monday.AddDays(1)).ButtonStyle);
        scope.Scheduler.ResourceKindFilter = ResourceKind.Room;
        await scope.LayoutAsync();
        Assert.AreEqual(DaisyButtonVariant.Ghost, DayButton(scope, Monday).Variant);
        foreach (var day in new[] { 3, 4, 7, 8, 9 })
        {
            Assert.AreEqual(DaisyButtonVariant.Primary, DayButton(scope, Monday.AddDays(day)).Variant, day.ToString(CultureInfo.InvariantCulture));
            Assert.AreEqual(DaisyButtonStyle.Soft, DayButton(scope, Monday.AddDays(day)).ButtonStyle, day.ToString(CultureInfo.InvariantCulture));
        }
        Assert.AreEqual(DaisyButtonVariant.Ghost, DayButton(scope, Monday.AddDays(5)).Variant);
        Assert.AreEqual(DaisyButtonVariant.Ghost, DayButton(scope, Monday.AddDays(10)).Variant);
        var occupiedColor = ((SolidColorBrush)DayButton(scope, Monday.AddDays(3)).Background).Color;
        var emptyColor = ((SolidColorBrush)DayButton(scope, Monday.AddDays(5)).Background).Color;
        Assert.AreNotEqual(occupiedColor, emptyColor);
        Assert.IsEmpty(scope.Frames(spanning));
    }

    private static DaisyButton DayButton(SchedulerScope scope, DateTime date)
    {
        var days = scope.Descendants<Grid>().Single(grid => grid.Name == "PART_CalendarDays");
        var label = date.ToString("D", scope.Scheduler.FormattingCulture);
        return days.Children.OfType<DaisyButton>().Single(button => AutomationProperties.GetName(button) == label);
    }

    [TestMethod]
    public async Task When_SettingsOpenAtExtraLarge_ControlsFitBesideTheVerticalScrollbar()
    {
        var previous = FloweryLocalization.CurrentCultureName;
        using var scope = await CreateAsync(SchedulerView.Week);
        try
        {
            FloweryLocalization.SetCulture("de");
            scope.Scheduler.Size = DaisySize.ExtraLarge;
            await scope.LayoutAsync();
            var builder = typeof(DaisyScheduler).GetMethod("BuildSettings", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var scroll = (ScrollViewer)builder.Invoke(scope.Scheduler, null)!;
            var popupHost = new Border { Width = 500, Height = 320, Child = scroll };
            scope.Host.Child = null;
            scope.Host.Child = popupHost;
            await scope.LayoutAsync();
            Assert.IsTrue(scroll.ExtentHeight > scroll.ViewportHeight, "The settings must scroll at this height");
            // The settings panel never extends horizontally beyond the viewport, so nothing hides under the bar.
            Assert.IsTrue(scroll.ExtentWidth <= scroll.ViewportWidth + 1, $"{scroll.ExtentWidth} > {scroll.ViewportWidth}");
            foreach (var select in Descendants<DaisySelect>(scroll))
            {
                var left = select.TransformToVisual(scroll).TransformPoint(default).X;
                Assert.IsTrue(left + select.ActualWidth <= scroll.ViewportWidth + 1, "Settings control extends past the viewport");
            }
        }
        finally
        {
            FloweryLocalization.SetCulture(previous);
        }
    }

    [TestMethod]
    public async Task When_CalendarFiltersKinds_TextWraps_AndTheListScrolls_WithoutChangingOwnership()
    {
        using var scope = await CreateAsync(SchedulerView.Calendar);
        scope.Host.Height = 500;
        scope.Scheduler.ResourceKindFilter = ResourceKind.Room;
        scope.Scheduler.SchedulerResources = new[]
        {
            new ResourceModel { Id = 1, Name = "Person", Kind = ResourceKind.Person },
            new ResourceModel { Id = 2, Name = "Room", Kind = ResourceKind.Room }
        };
        var room = new AppointmentModel { Id = 2, Subject = "Room booking", Start = Monday.AddHours(9), End = Monday.AddHours(10), ResourceIds = { 1, 2 } };
        var longer = room.Clone();
        longer.Id = 3;
        longer.Description = string.Join(" ", Enumerable.Repeat("A long description that must wrap across multiple lines.", 70));
        scope.Scheduler.Appointments = new[] { scope.Appointment, room, longer };
        await scope.LayoutAsync();
        Assert.IsEmpty(scope.Frames(scope.Appointment));
        Assert.HasCount(1, scope.Frames(room));
        Assert.IsTrue(scope.Frame(longer).ActualHeight > scope.Frame(room).ActualHeight);
        var description = Descendants<TextBlock>(scope.Frame(longer)).Single(text => text.Text == longer.Description);
        Assert.AreEqual(TextWrapping.Wrap, description.TextWrapping);
        var scroll = scope.Descendants<ScrollViewer>().Single(control => control.Name == "PART_CalendarAppointmentScroll");
        Assert.IsTrue(scroll.ExtentHeight > scroll.ViewportHeight);
        Assert.IsTrue(scroll.ExtentWidth <= scroll.ViewportWidth + 1);
        Assert.IsTrue(scope.Scheduler.HandleAppointmentKey(scope.Frame(room), VirtualKey.Enter));
        Assert.AreSame(room, scope.Scheduler.SelectedAppointment);
        Assert.HasCount(3, scope.Scheduler.Appointments);
    }

    [TestMethod]
    public async Task When_CalendarNavigates_ConfiguredWeekRulesApply_AndMonthAndDateButtonsWork()
    {
        using var scope = await CreateAsync(SchedulerView.Calendar);
        scope.Scheduler.SelectedDate = new DateTimeOffset(new DateTime(2021, 1, 12));
        await scope.LayoutAsync();
        Grid Days() => scope.Descendants<Grid>().Single(grid => grid.Name == "PART_CalendarDays");
        string? FirstWeek() => Days().Children.OfType<TextBlock>().Single(text => Grid.GetRow(text) == 1 && Grid.GetColumn(text) == 0).Text;
        Assert.AreEqual("53", FirstWeek());
        scope.Scheduler.FirstDayOfWeek = DayOfWeek.Sunday;
        scope.Scheduler.WeekRule = CalendarWeekRule.FirstDay;
        await scope.LayoutAsync();
        Assert.AreEqual("1", FirstWeek());
        var down = scope.Descendants<DaisyButton>().Single(button => Equals(button.Content, "↓"));
        new ButtonAutomationPeer(down).Invoke();
        await scope.LayoutAsync();
        Assert.AreEqual(2, scope.Scheduler.SelectedDate.Month);
        var date = Days().Children.OfType<DaisyButton>().First(button => Equals(button.Content, "15"));
        new ButtonAutomationPeer(date).Invoke();
        await scope.LayoutAsync();
        Assert.AreEqual(new DateTime(2021, 2, 15), scope.Scheduler.SelectedDate.Date);
        Assert.IsFalse(string.IsNullOrEmpty(AutomationProperties.GetName(date)));
    }
}
