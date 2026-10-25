using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Flowery.Controls;
using Flowery.Controls.Scheduler.Models;
using Flowery.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.Foundation;
using static Flowery.Uno.RuntimeTests.Controls.Scheduler.SchedulerScope;

namespace Flowery.Uno.RuntimeTests.Controls.Scheduler;

/// <summary>Counterparts of the DaisySchedulerToolbar tests in Flowery.NET's SchedulerLayoutTests.</summary>
[TestClass]
public class Given_SchedulerToolbar
{
    private static DaisySchedulerToolbar Toolbar(SchedulerScope scope) => scope.Descendants<DaisySchedulerToolbar>().Single();

    private static Button Action(DaisySchedulerToolbar toolbar, string tag) =>
        Descendants<Button>(toolbar).Single(button => Equals(button.Tag, tag));

    private static string[] Title(DaisySchedulerToolbar toolbar) =>
        Descendants<TextBlock>(Action(toolbar, "Date")).Select(static text => text.Text).ToArray();

    private static void Click(Button button) => new ButtonAutomationPeer(button).Invoke();

    [TestMethod]
    public async Task When_TheSizeChanges_ToolbarIconsAndButtonsFollowIt()
    {
        using var scope = await CreateAsync(SchedulerView.Month);
        var sizes = new Dictionary<DaisySize, (double Icon, double Button)>();
        foreach (var size in new[] { DaisySize.Small, DaisySize.Large })
        {
            scope.Scheduler.Size = size;
            await scope.LayoutAsync();
            var segment = Descendants<Button>(Toolbar(scope)).First(static button => button.Tag is SchedulerView);
            sizes[size] = (((FrameworkElement)segment.Content).ActualWidth, segment.ActualHeight);
        }
        Assert.IsTrue(sizes[DaisySize.Large].Icon > sizes[DaisySize.Small].Icon, $"icons {sizes[DaisySize.Small].Icon} and {sizes[DaisySize.Large].Icon}");
        Assert.IsTrue(sizes[DaisySize.Large].Button > sizes[DaisySize.Small].Button, $"buttons {sizes[DaisySize.Small].Button} and {sizes[DaisySize.Large].Button}");
    }

    [TestMethod]
    public async Task When_TheSchedulerChanges_TheToolbarKeepsItsButtons_SoFocusAndAnOpenMenuStay()
    {
        using var scope = await CreateAsync(SchedulerView.Week);
        var toolbar = Toolbar(scope);
        var settings = Action(toolbar, "Settings");
        var next = Action(toolbar, "Next");
        var title = Title(toolbar);

        next.Focus(FocusState.Programmatic);
        Click(next);
        scope.Scheduler.ShowWeekends = false;
        scope.Scheduler.CurrentView = SchedulerView.Month;
        await scope.LayoutAsync();

        Assert.AreSame(next, FocusManager.GetFocusedElement(scope.Scheduler.XamlRoot!), "The keyboard focus stays on Next");
        CollectionAssert.AreNotEqual(title, Title(toolbar));
        var selected = Descendants<DaisySchedulerToolbar.ToolbarButton>(toolbar).Where(static button => button.IsSelected).ToArray();
        Assert.AreEqual(SchedulerView.Month, selected.Single().Tag);

        // A setting changed in the open menu updates the scheduler, and the menu stays open.
        Click(settings);
        await scope.LayoutAsync();
        var popups = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(scope.Scheduler.XamlRoot);
        try
        {
            Assert.IsTrue(popups.Any(static popup => popup.Child is FlyoutPresenter), "The settings menu opens");
            scope.Scheduler.ShowTiles = false;
            await scope.LayoutAsync();
            Assert.IsTrue(popups.Any(static popup => popup.IsOpen && popup.Child is FlyoutPresenter), "The settings menu stays open");
        }
        finally
        {
            foreach (var popup in popups)
                popup.IsOpen = false;
        }
    }

    [TestMethod]
    public async Task When_AToolbarIsPlacedElsewhere_ItControlsItsScheduler_WhoseOwnToolbarIsHidden()
    {
        var scheduler = new DaisyScheduler { ShowToolbar = false, SelectedDate = new DateTimeOffset(Monday), CurrentView = SchedulerView.Week };
        var toolbar = new DaisySchedulerToolbar { Scheduler = scheduler };
        var panel = new StackPanel { Width = 1100 };
        RuntimeTestHelpers.AttachToHost(panel);
        try
        {
            // The toolbar may be shown before its scheduler is; it stays empty until then.
            panel.Children.Add(toolbar);
            await RuntimeTestHelpers.CompleteLayoutAsync(toolbar);
            Assert.IsNull(toolbar.Content);
            scheduler.Height = 600;
            panel.Children.Add(scheduler);
            await RuntimeTestHelpers.CompleteLayoutAsync(scheduler);
            await RuntimeTestHelpers.CompleteLayoutAsync(panel);

            Click(Action(toolbar, "Next"));
            Assert.AreEqual(Monday.AddDays(7), scheduler.SelectedDate.Date);
            var builtIn = Descendants<DaisySchedulerToolbar>(scheduler).Single();
            Assert.AreEqual(Visibility.Collapsed, builtIn.Visibility);

            // Shown again, the built-in toolbar has caught up with the navigation it missed while hidden.
            scheduler.ShowToolbar = true;
            await RuntimeTestHelpers.CompleteLayoutAsync(panel);
            Assert.AreEqual(Visibility.Visible, builtIn.Visibility);
            CollectionAssert.AreEqual(Title(toolbar), Title(builtIn));
        }
        finally
        {
            RuntimeTestHelpers.GetHostPanel().Children.Clear();
        }
    }

    [TestMethod]
    public async Task When_HostButtonsAreClicked_TheyRaiseTheirEvents_AndAnImageShowsItsLabel()
    {
        using var scope = await CreateAsync(SchedulerView.Week);
        var clicked = new List<string?>();
        var toggled = new List<string?>();
        scope.Scheduler.ToolbarButtonClicked += (_, button) => clicked.Add(button.Id);
        scope.Scheduler.AdornmentToggleRequested += (_, button) => toggled.Add(button.Id);
        scope.Scheduler.ToolbarButtons.Add(new SchedulerToolbarButton { Id = "new", Label = "New", IconData = SchedulerToolbarIcons.Add });
        scope.Scheduler.ToolbarButtons.Add(new SchedulerToolbarButton { Id = "map", Label = "Map", Image = OnePixelPng });
        scope.Scheduler.ToolbarAdornments.Add(new SchedulerToolbarButton { Id = "flag", Label = "Flags", IsToggle = true, IsChecked = true });
        await scope.LayoutAsync();
        var toolbar = Toolbar(scope);
        DaisySchedulerToolbar.ToolbarButton Host(string id) => Descendants<DaisySchedulerToolbar.ToolbarButton>(toolbar)
            .Single(button => button.Tag is DaisyScheduler.ToolbarAction action && action.Button.Id == id);

        Click(Host("new"));
        Click(Host("flag"));
        CollectionAssert.AreEqual(new[] { "new" }, clicked);
        CollectionAssert.AreEqual(new[] { "flag" }, toggled);
        Assert.IsTrue(Host("flag").IsChecked);
        Assert.IsInstanceOfType<Viewbox>(Host("new").Content);
        Assert.IsTrue(Descendants<TextBlock>(Host("map")).Any(static text => text.Text == "Map"));
    }

    [TestMethod]
    public async Task When_TheTitleIsShown_ItFollowsTheCultureOrderOfYearMonthAndDay()
    {
        var previous = FloweryLocalization.CurrentCultureName;
        try
        {
            foreach (var (language, view, days, primary, secondary) in new[]
            {
                ("en", SchedulerView.Month, 0, "October", "2026"),
                ("en", SchedulerView.Week, 0, "Oct 12 – Oct 18", "2026"),
                ("en", SchedulerView.Week, 79, "Dec 28 – Jan 3", "2026 – 2027"),
                ("ja", SchedulerView.Month, 0, "2026年10月", ""),
                ("ja", SchedulerView.Week, 0, "2026年10月12日 – 10月18日", ""),
                ("ja", SchedulerView.Week, 79, "2026年12月28日 – 2027年1月3日", "")
            })
            {
                FloweryLocalization.SetCulture(language);
                using var scope = await CreateAsync(view);
                scope.Scheduler.FirstDayOfWeek = DayOfWeek.Monday;
                scope.Scheduler.SelectedDate = new DateTimeOffset(Monday.AddDays(days));
                await scope.LayoutAsync();
                var texts = Title(Toolbar(scope));

                Assert.AreEqual(primary, texts[0], $"{language} {view} {days}");
                Assert.AreEqual(secondary, texts[1], $"{language} {view} {days}");
            }
        }
        finally
        {
            FloweryLocalization.SetCulture(previous);
        }
    }

    [TestMethod]
    public async Task When_TheToolbarIsNarrow_ItsButtonsMoveBelowTheTitle_AndNothingOverlaps()
    {
        await AssertToolbarAsync(1100, below: false);
        await AssertToolbarAsync(420, below: true);
        // A phone: the view segments narrow evenly, so all of them fit.
        await AssertToolbarAsync(320, below: true);
    }

    private static async Task AssertToolbarAsync(double width, bool below)
    {
        using var scope = await CreateAsync(SchedulerView.Week);
        scope.Scheduler.ToolbarButtons.Add(new SchedulerToolbarButton { Id = "new", Label = "New", IconData = SchedulerToolbarIcons.Add });
        scope.Scheduler.ToolbarButtons.Add(new SchedulerToolbarButton { Id = "print", Label = "Print", IconData = SchedulerToolbarIcons.Edit });
        scope.Host.Width = width;
        await scope.LayoutAsync();
        var toolbar = Toolbar(scope);
        var buttons = Descendants<Button>(toolbar).Where(static button => button.ActualWidth > 0)
            .Select(button => (Name: button.Tag?.ToString(), Bounds: button.TransformToVisual(toolbar).TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight))))
            .ToArray();

        foreach (var (name, bounds) in buttons)
            Assert.IsTrue(bounds.X >= -0.5 && bounds.Right <= toolbar.ActualWidth + 0.5, $"{name} at {bounds} outside {toolbar.ActualWidth}");
        for (var a = 0; a < buttons.Length; a++)
            for (var b = a + 1; b < buttons.Length; b++)
            {
                var overlap = RectHelper.Intersect(buttons[a].Bounds, buttons[b].Bounds);
                Assert.IsTrue(overlap.IsEmpty || overlap.Width < 0.5 || overlap.Height < 0.5,
                    $"{buttons[a].Name} {buttons[a].Bounds} overlaps {buttons[b].Name} {buttons[b].Bounds}");
            }
        var settings = buttons.Single(static button => button.Name == "Settings").Bounds;
        var date = buttons.Single(static button => button.Name == "Date").Bounds;
        Assert.AreEqual(below, settings.Y >= date.Bottom, $"Settings at {settings}, date at {date}");
        var segments = Descendants<Button>(toolbar).Where(static button => button.Tag is SchedulerView).ToArray();
        Assert.HasCount(Enum.GetValues<SchedulerView>().Length, segments.Where(static segment => segment.ActualWidth > 0).ToArray(), $"width {width}");
        Assert.AreEqual(1, segments.Select(static segment => Math.Round(segment.ActualWidth)).Distinct().Count(), $"width {width}");
    }

    // A 1x1 transparent PNG.
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
}
