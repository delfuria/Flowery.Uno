using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Flowery.Controls;
using Flowery.Controls.Scheduler.Contracts;
using Flowery.Controls.Scheduler.Models;
using Flowery.Controls.Scheduler.Rendering;
using Flowery.Controls.Scheduler.Utilities;
using Flowery.Localization;
using Flowery.Services;
using Flowery.Theming;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.System;
using Windows.UI;
using static Flowery.Uno.RuntimeTests.Controls.Scheduler.SchedulerScope;

namespace Flowery.Uno.RuntimeTests.Controls.Scheduler;

/// <summary>Counterparts of Flowery.NET's SchedulerTests.</summary>
[TestClass]
public class Given_Scheduler
{
    [TestMethod]
    public async Task When_DragMoving_ReportsSnappedProposal_WithoutMutatingHostData()
    {
        using var scope = await CreateAsync(SchedulerView.Day);
        AppointmentMoveArgs? request = null;
        scope.Scheduler.AppointmentMoveRequested += (_, args) => request = args;
        var block = scope.Frame(scope.Appointment);
        var origin = scope.Center(block);
        await scope.DragAsync(origin, new(origin.X, origin.Y + 48));
        Assert.IsNotNull(request);
        Assert.AreSame(scope.Appointment, request.Appointment);
        Assert.AreEqual(Monday.AddHours(10), request.NewStart);
        Assert.AreEqual(Monday.AddHours(11), request.NewEnd);
        Assert.AreEqual(Monday.AddHours(9), scope.Appointment.Start);
        Assert.AreEqual(Monday.AddHours(10), scope.Appointment.End);
    }

    [TestMethod]
    public async Task When_ResizingSelectedEdge_ReportsDurationChange_WithoutMutatingHostData()
    {
        using var scope = await CreateAsync(SchedulerView.Day);
        scope.Scheduler.SelectedAppointment = scope.Appointment;
        await scope.LayoutAsync();
        AppointmentResizeArgs? request = null;
        scope.Scheduler.AppointmentResizeRequested += (_, args) => request = args;
        var block = scope.Frame(scope.Appointment);
        var origin = scope.Point(block, block.ActualWidth / 2, block.ActualHeight - 2);
        await scope.DragAsync(origin, new(origin.X, origin.Y + 24));
        Assert.IsNotNull(request);
        Assert.AreEqual(Monday.AddHours(9), request.NewStart);
        Assert.AreEqual(Monday.AddHours(10.5), request.NewEnd);
        Assert.AreEqual(Monday.AddHours(10), scope.Appointment.End);
    }

    [TestMethod]
    public async Task When_DraggingEmptyTimeRange_RequestsCreation()
    {
        using var scope = await CreateAsync(SchedulerView.Day);
        AppointmentCreateArgs? request = null;
        scope.Scheduler.AppointmentCreateRequested += (_, args) => request = args;
        var surface = scope.Descendants<Canvas>().Single(canvas => canvas.Height == 1152 && canvas.Width > 100);
        var origin = scope.Point(surface, surface.Width / 2, 11 * 60 * 0.8);
        await scope.DragAsync(origin, new(origin.X, origin.Y + 48));
        Assert.IsNotNull(request);
        Assert.AreEqual(Monday.AddHours(11), request.Start);
        Assert.AreEqual(Monday.AddHours(12.5), request.End);
        Assert.IsFalse(request.IsAllDay);
        Assert.HasCount(1, scope.Scheduler.Appointments!);
    }

    [TestMethod]
    public async Task When_ReadOnly_PreventsMoveResizeCreateAndDelete_ButKeepsSelection()
    {
        using var scope = await CreateAsync(SchedulerView.Day);
        scope.Scheduler.IsReadOnly = true;
        await scope.LayoutAsync();
        var writes = 0;
        scope.Scheduler.AppointmentMoveRequested += (_, _) => writes++;
        scope.Scheduler.AppointmentResizeRequested += (_, _) => writes++;
        scope.Scheduler.AppointmentCreateRequested += (_, _) => writes++;
        scope.Scheduler.AppointmentsDeleteRequested += (_, _) => writes++;
        var origin = scope.Center(scope.Frame(scope.Appointment));
        await scope.DragAsync(origin, new(origin.X, origin.Y + 48));
        scope.Scheduler.DeleteSelected();
        Assert.AreSame(scope.Appointment, scope.Scheduler.SelectedAppointment);
        Assert.AreEqual(0, writes);
    }

    [TestMethod]
    public async Task When_AppointmentIsLocked_CannotBeSelectedOrDeleted_ButAllowsContextRequest()
    {
        using var scope = await CreateAsync(SchedulerView.Month);
        scope.Appointment.IsLocked = true;
        scope.Scheduler.Appointments = new[] { scope.Appointment };
        await scope.LayoutAsync();
        var block = scope.Frame(scope.Appointment);
        await scope.ClickAsync(block);
        Assert.IsNull(scope.Scheduler.SelectedAppointment);
        AppointmentContextRequestedArgs? context = null;
        scope.Scheduler.AppointmentContextRequested += (_, args) => context = args;
        await scope.ClickAsync(block, right: true);
        Assert.IsNotNull(context);
        Assert.AreSame(scope.Appointment, context.Appointment);
        scope.Scheduler.SelectedAppointment = scope.Appointment;
        Assert.IsEmpty(scope.Scheduler.SelectedAppointments);
    }

    [TestMethod]
    public async Task When_DeleteKeyPressed_ReportsSelection_AndLeavesInputListIntact()
    {
        using var scope = await CreateAsync(SchedulerView.Month);
        AppointmentsDeleteArgs? request = null;
        scope.Scheduler.AppointmentsDeleteRequested += (_, args) => request = args;
        await scope.ClickAsync(scope.Frame(scope.Appointment));
        Assert.IsTrue(scope.Scheduler.HandleKey(VirtualKey.Delete, withShift: false));
        Assert.IsNotNull(request);
        Assert.AreSame(scope.Appointment, request.Appointments.Single());
        Assert.HasCount(1, scope.Scheduler.Appointments!);
        Assert.IsEmpty(scope.Scheduler.SelectedAppointments);
    }

    [TestMethod]
    public async Task When_Refreshing_PreservesScroll_AndMatchesSelectionToReplacedHostInstance()
    {
        using var scope = await CreateAsync(SchedulerView.Day);
        ScrollViewer Body() => scope.Descendants<ScrollViewer>().Single(scroll =>
            scroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto && scroll.VerticalScrollBarVisibility == ScrollBarVisibility.Auto);
        Body().ChangeView(null, 480, null, true);
        await scope.LayoutAsync();
        scope.Scheduler.SelectedAppointment = scope.Appointment;
        var updated = scope.Appointment.Clone();
        updated.Subject = "Updated";
        scope.Scheduler.Appointments = new[] { updated };
        await scope.LayoutAsync();
        Assert.AreEqual(480, Body().VerticalOffset, 1);
        Assert.AreSame(updated, scope.Scheduler.SelectedAppointment);
        Assert.IsTrue(scope.Descendants<TextBlock>().Any(text => text.Text == "Updated"));
    }

    [TestMethod]
    public async Task When_GlobalSizeChanges_FontsFollow_AndExplicitSizeIsRespected()
    {
        var previous = FlowerySizeManager.CurrentSize;
        using var scope = await CreateAsync(SchedulerView.Month);
        try
        {
            scope.Scheduler.ClearValue(DaisyScheduler.SizeProperty);
            FlowerySizeManager.ApplySize(DaisySize.ExtraLarge);
            await scope.LayoutAsync();
            Assert.AreEqual(DaisySize.ExtraLarge, scope.Scheduler.ResolvedSize);
            Assert.AreEqual(20, scope.Scheduler.FontSize);
            foreach (var button in scope.Descendants<DaisyButton>())
                Assert.AreEqual(DaisySize.ExtraLarge, button.Size);
            scope.Scheduler.Size = DaisySize.Small;
            FlowerySizeManager.ApplySize(DaisySize.ExtraSmall);
            await scope.LayoutAsync();
            Assert.AreEqual(DaisySize.Small, scope.Scheduler.ResolvedSize);
            Assert.AreEqual(12, scope.Scheduler.FontSize);
        }
        finally { FlowerySizeManager.ApplySize(previous); }
    }

    [TestMethod]
    public async Task When_ThemeChanges_AppointmentColorsRefresh_AndCustomFillsKeepReadableText()
    {
        var previous = DaisyThemeManager.CurrentThemeName;
        using var scope = await CreateAsync(SchedulerView.Month);
        try
        {
            Assert.IsTrue(DaisyThemeManager.ApplyTheme("Light"));
            await scope.LayoutAsync();
            // Palette brushes are shared and mutated. Capture the color value before switching.
            var light = ((SolidColorBrush)((AppointmentContext)scope.Frame(scope.Appointment).DataContext).BackBrush).Color;
            Assert.IsTrue(DaisyThemeManager.ApplyTheme("Dark"));
            await scope.LayoutAsync();
            var dark = ((AppointmentContext)scope.Frame(scope.Appointment).DataContext).BackBrush;
            Assert.AreNotEqual(light, ((SolidColorBrush)dark).Color);
            scope.Appointment.BackColor = Colors.Black;
            scope.Scheduler.Appointments = new[] { scope.Appointment };
            await scope.LayoutAsync();
            var context = (AppointmentContext)scope.Frame(scope.Appointment).DataContext;
            Assert.AreEqual(Colors.White, ((SolidColorBrush)context.Foreground).Color);
        }
        finally { DaisyThemeManager.ApplyTheme(previous ?? "Light"); }
    }

    [TestMethod]
    public async Task When_NameTagsRender_TheyUseTheirOwnBackground_ForReadableContrast()
    {
        var previous = DaisyThemeManager.CurrentThemeName;
        try
        {
            foreach (var (theme, darkAppointment) in new[] { ("Coffee", false), ("Light", false), ("Dark", false), ("Corporate", false), ("Light", true) })
            {
                using var scope = await CreateAsync(SchedulerView.Month);
                Assert.IsTrue(DaisyThemeManager.ApplyTheme(theme));
                if (darkAppointment)
                    scope.Appointment.BackColor = Colors.Black;
                scope.Appointment.Tiles.Add(new AppointmentDecoration { Text = "Alex" });
                scope.Appointment.Tiles.Add(new AppointmentDecoration { Text = "Dark tag", BackColor = Colors.Black });
                scope.Appointment.Tiles.Add(new AppointmentDecoration { Text = "Light tag", BackColor = Colors.White });
                scope.Appointment.Tiles.Add(new AppointmentDecoration { Text = "Translucent tag", BackColor = Color.FromArgb(128, 255, 255, 255) });
                scope.Appointment.Tiles.Add(new AppointmentDecoration { Text = "Explicit tag", BackColor = Colors.Black, ForeColor = Colors.Orange });
                scope.Appointment.Adornments.Add(new AppointmentDecoration { Text = "Overlay" });
                scope.Scheduler.ShowTiles = true;
                scope.Scheduler.Appointments = new[] { scope.Appointment };
                await scope.LayoutAsync();

                var frame = scope.Frame(scope.Appointment);
                var labels = Descendants<TextBlock>(frame).ToArray();
                var context = (AppointmentContext)frame.DataContext;
                var backdrop = ((SolidColorBrush)context.BackBrush).Color;
                foreach (var name in new[] { "Alex", "Dark tag", "Light tag", "Translucent tag" })
                {
                    var label = labels.Single(text => text.Text == name);
                    var fill = ((SolidColorBrush)((Border)label.Parent).Background).Color;
                    var textColor = ((SolidColorBrush)label.Foreground).Color;
                    var opacity = fill.A / 255d;
                    var rendered = Color.FromArgb(255,
                        (byte)Math.Round(fill.R * opacity + backdrop.R * (1 - opacity)),
                        (byte)Math.Round(fill.G * opacity + backdrop.G * (1 - opacity)),
                        (byte)Math.Round(fill.B * opacity + backdrop.B * (1 - opacity)));
                    var foregroundLuminance = Luminance(textColor);
                    var backgroundLuminance = Luminance(rendered);
                    var ratio = (Math.Max(foregroundLuminance, backgroundLuminance) + 0.05) /
                        (Math.Min(foregroundLuminance, backgroundLuminance) + 0.05);
                    Assert.IsTrue(ratio >= 4.5, $"{theme} / {name}: contrast {ratio:F2}; text {textColor}, fill {fill}, appointment {backdrop}");
                }
                Assert.AreEqual(Colors.Orange, ((SolidColorBrush)labels.Single(text => text.Text == "Explicit tag").Foreground).Color);
                Assert.AreEqual(((SolidColorBrush)context.Foreground).Color,
                    ((SolidColorBrush)labels.Single(text => text.Text == "Overlay").Foreground).Color);
            }
        }
        finally { DaisyThemeManager.ApplyTheme(previous ?? "Light"); }

        static double Luminance(Color color)
        {
            static double Linear(byte channel) => channel <= 10 ? channel / 3294.6 : Math.Pow((channel / 255d + 0.055) / 1.055, 2.4);
            return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        }
    }

    [TestMethod]
    public async Task When_PlannerConvertsDisplayedTimes_SelectionReturnsOriginalAppointment()
    {
        using var scope = await CreateAsync(SchedulerView.Planner);
        scope.Scheduler.SchedulerResources = new[] { new ResourceModel { Id = 1, Name = "New York", TimeZoneId = "America/New_York" } };
        scope.Appointment.Start = Monday.AddHours(13);
        scope.Appointment.End = Monday.AddHours(14);
        scope.Scheduler.Appointments = new[] { scope.Appointment };
        await scope.LayoutAsync();
        var frame = scope.Frame(scope.Appointment);
        Assert.AreEqual(Monday.AddHours(9), ((AppointmentContext)frame.DataContext).Start);
        await scope.ClickAsync(frame);
        Assert.AreSame(scope.Appointment, scope.Scheduler.SelectedAppointment);
        Assert.AreEqual(Monday.AddHours(13), scope.Appointment.Start);
    }

    [TestMethod]
    public async Task When_HoverTooltipRenders_TextInheritsTheTooltipPalette()
    {
        var previous = DaisyThemeManager.CurrentThemeName;
        try
        {
            foreach (var theme in new[] { "Corporate", "Light", "Dark", "Coffee" })
            {
                using var scope = await CreateAsync(SchedulerView.Planner);
                Assert.IsTrue(DaisyThemeManager.ApplyTheme(theme));
                scope.Appointment.Description = "Appointment note";
                scope.Appointment.Memo = "Internal memo";
                scope.Appointment.Details.Add(new AppointmentDetail("Resource", "Alex"));
                scope.Scheduler.Appointments = new[] { scope.Appointment };
                await scope.LayoutAsync();
                var tooltip = (ToolTip)ToolTipService.GetToolTip(scope.Frame(scope.Appointment));
                var content = (StackPanel)tooltip.Content;
                var background = ((SolidColorBrush)tooltip.Background).Color;
                var foreground = ((SolidColorBrush)tooltip.Foreground).Color;
                Assert.AreEqual(((SolidColorBrush)DaisyResourceLookup.GetBrush("DaisyBase200Brush")).Color, background, theme);
                Assert.AreNotEqual(background, ((SolidColorBrush)tooltip.BorderBrush).Color, theme);
                Assert.IsTrue(tooltip.BorderThickness.Left > 0, theme);
                Assert.AreNotEqual(background, foreground, theme);
                // Unset foregrounds inherit the tooltip's, also over custom appointment fills.
                foreach (var label in content.Children.OfType<TextBlock>())
                    Assert.AreEqual(DependencyProperty.UnsetValue, label.ReadLocalValue(TextBlock.ForegroundProperty), theme);
            }
        }
        finally { DaisyThemeManager.ApplyTheme(previous ?? "Light"); }
    }

    [TestMethod]
    public async Task When_MonthRendered_RespectsFirstDayOfWeek_AndMidnightEndIsExclusive()
    {
        using var scope = await CreateAsync(SchedulerView.Month);
        scope.Appointment.IsAllDay = true;
        scope.Appointment.Start = Monday;
        scope.Appointment.End = Monday.AddDays(1);
        scope.Scheduler.Appointments = new[] { scope.Appointment };
        scope.Scheduler.FirstDayOfWeek = DayOfWeek.Sunday;
        await scope.LayoutAsync();
        Assert.HasCount(1, scope.Frames(scope.Appointment));
        var grid = scope.Descendants<Grid>().Single(control => control.RowDefinitions.Count == 7 && control.ColumnDefinitions.Count == 7);
        var header = (Border)grid.Children[0];
        Assert.AreEqual(new DateTime(2026, 9, 27).ToString("ddd", scope.Scheduler.FormattingCulture), ((TextBlock)header.Child).Text);
    }

    [TestMethod]
    public async Task When_HostResizes_MonthFitsTheViewport()
    {
        using var scope = await CreateAsync(SchedulerView.Month);
        scope.Host.Width = 760;
        await scope.LayoutAsync();
        var scroll = scope.Descendants<ScrollViewer>().Single();
        Assert.IsTrue(scroll.ExtentWidth <= scroll.ViewportWidth + 1, $"{scroll.ExtentWidth} > {scroll.ViewportWidth}");
        Assert.IsTrue(scope.Frame(scope.Appointment).ActualWidth > 0);
    }

    [TestMethod]
    public async Task When_LanguageChanges_EveryLanguageHasSchedulerResources_AndUpdatesAttachedControls()
    {
        var previousCulture = FloweryLocalization.CurrentCultureName;
        var previousResolver = FloweryLocalization.CustomResolver;
        var assembly = typeof(DaisyScheduler).Assembly;
        using var englishStream = assembly.GetManifestResourceStream("Flowery.Uno.Localization.en.json")!;
        using var english = JsonDocument.Parse(englishStream);
        var requiredKeys = english.RootElement.EnumerateObject()
            .Where(static property => property.Name.StartsWith("Scheduler_", StringComparison.Ordinal))
            .Select(static property => property.Name).OrderBy(static key => key, StringComparer.Ordinal).ToArray();
        Assert.IsNotEmpty(requiredKeys);
        using var scope = await CreateAsync(SchedulerView.Planner);
        try
        {
            // App keys must not replace the library's view names or resource headings.
            FloweryLocalization.CustomResolver = static _ => "Host override";
            foreach (var language in FloweryLocalization.SupportedLanguages)
            {
                using var stream = assembly.GetManifestResourceStream($"Flowery.Uno.Localization.{language}.json")!;
                using var document = JsonDocument.Parse(stream);
                var keys = document.RootElement.EnumerateObject()
                    .Where(static property => property.Name.StartsWith("Scheduler_", StringComparison.Ordinal))
                    .Select(static property => property.Name).OrderBy(static key => key, StringComparer.Ordinal).ToArray();
                CollectionAssert.AreEqual(requiredKeys, keys, language);
                foreach (var key in keys)
                    Assert.IsFalse(string.IsNullOrWhiteSpace(document.RootElement.GetProperty(key).GetString()), language + " " + key);

                FloweryLocalization.SetCulture(language);
                await scope.LayoutAsync();
                var toolbar = scope.Descendants<DaisySchedulerToolbar>().Single();
                foreach (var button in Descendants<Button>(toolbar).Where(static button => button.Tag is SchedulerView or string))
                {
                    var key = button.Tag switch { SchedulerView view => view.ToString(), "Date" => "ChooseDate", var action => (string)action };
                    Assert.AreEqual(document.RootElement.GetProperty("Scheduler_" + key).GetString(), AutomationProperties.GetName(button), language);
                }
                var today = Descendants<Button>(toolbar).Single(static button => Equals(button.Tag, "Today"));
                Assert.AreEqual(document.RootElement.GetProperty("Scheduler_Today").GetString(), ((TextBlock)today.Content).Text, language);
                var resourceHeading = document.RootElement.GetProperty("Scheduler_Resources").GetString();
                Assert.IsTrue(scope.Descendants<TextBlock>().Any(text => text.Text == resourceHeading), language);
                Assert.AreEqual("Install", scope.Appointment.Subject);
                Assert.AreEqual(Monday.AddHours(9), scope.Appointment.Start);
            }
        }
        finally
        {
            FloweryLocalization.CustomResolver = previousResolver;
            FloweryLocalization.SetCulture(previousCulture);
        }
    }

    [TestMethod]
    public void When_ExternalDragDataIsCreated_ItCarriesTheTypedSourceItem()
    {
        var item = new Droppable();
        var data = ExternalDropHandler.CreateDataPackage(item).GetView();
        Assert.IsTrue(ExternalDropHandler.TryGetItem(data, out var dropped));
        Assert.AreSame(item, dropped);
        // A newer drag replaces the item; the stale package no longer resolves.
        ExternalDropHandler.CreateDataPackage(new Droppable());
        Assert.IsFalse(ExternalDropHandler.TryGetItem(data, out _));
    }

    internal sealed class Droppable : ISchedulerDroppable
    {
        public string Subject => "External";
        public TimeSpan DefaultDuration => TimeSpan.FromHours(1);
        public object? Payload => null;
    }
}
