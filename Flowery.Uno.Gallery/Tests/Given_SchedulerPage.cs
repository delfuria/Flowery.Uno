#if WINDOWS || (__SKIA__ && !__WASM__)
using System.Threading.Tasks;
using Flowery.Controls;
using Flowery.Controls.Scheduler.Models;
using Flowery.Uno.Gallery.Examples;
using Flowery.Uno.Gallery.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using static Flowery.Uno.Gallery.Tests.GalleryTestHost;

namespace Flowery.Uno.Gallery.Tests;

/// <summary>Counterparts of Flowery.NET's Given_SchedulerPage: the Gallery's scheduler page on phones and the desktop.</summary>
[TestClass]
public class Given_SchedulerPage
{
    [TestMethod]
    public Task When_AnAppointmentIsCreated_TheEditorLiesOverTheScheduler() =>
        WithPageAsync(static () => new SchedulerExamples { Width = 1000, Height = 720 }, static async page =>
        {
            var scheduler = (DaisyScheduler)page.FindName("DemoScheduler");
            var editor = (Border)page.FindName("EditorPanel");
            var width = scheduler.ActualWidth;

            await OpenNewAsync(page);

            Assert.AreEqual(Visibility.Visible, editor.Visibility);
            // The scheduler keeps its width; the editor covers its right side.
            Assert.AreEqual(width, scheduler.ActualWidth, 0.5);
            var schedulerBounds = Bounds(scheduler, page);
            var editorBounds = Bounds(editor, page);
            Assert.AreEqual(schedulerBounds.Right, editorBounds.Right, 1);
            Assert.IsTrue(editorBounds.Left > schedulerBounds.Left);
        });

    [TestMethod]
    public Task When_TheSchedulerIsPressed_AnUnchangedEditorCloses() =>
        WithPageAsync(static () => new SchedulerExamples { Width = 1000, Height = 720 }, static async page =>
        {
            var editor = (Border)page.FindName("EditorPanel");
            var subject = (DaisyInput)page.FindName("SubjectInput");

            await OpenNewAsync(page);
            Assert.AreEqual(Visibility.Visible, editor.Visibility);
            page.DismissEditorIfUnchanged();
            Assert.AreEqual(Visibility.Collapsed, editor.Visibility);

            // With a change the editor stays open.
            await OpenNewAsync(page);
            subject.Text = "Lunch";
            page.DismissEditorIfUnchanged();
            Assert.AreEqual(Visibility.Visible, editor.Visibility);

            // Once the change is undone, it closes.
            subject.Text = string.Empty;
            page.DismissEditorIfUnchanged();
            Assert.AreEqual(Visibility.Collapsed, editor.Visibility);

            // Another date format writes the dates anew but changes no value, so the editor still closes.
            await OpenNewAsync(page);
            var scheduler = (DaisyScheduler)page.FindName("DemoScheduler");
            var start = ((DaisyInput)page.FindName("StartInput")).Text;
            scheduler.DateTimeFormat = scheduler.DateTimeFormat == SchedulerDateTimeFormat.European
                ? SchedulerDateTimeFormat.US : SchedulerDateTimeFormat.European;
            await SettleAsync(page);
            Assert.AreNotEqual(start, ((DaisyInput)page.FindName("StartInput")).Text, "The date is written anew");
            page.DismissEditorIfUnchanged();
            Assert.AreEqual(Visibility.Collapsed, editor.Visibility);
        });

    [TestMethod]
    public Task When_NoTitleBarFoldsTheIntro_TheEditorLeavesItOpen() =>
        WithPageAsync(static () => new SchedulerExamples { Width = 1000, Height = 720 }, static async page =>
        {
            await OpenNewAsync(page);
            Assert.AreEqual(Visibility.Visible, ((FrameworkElement)page.FindName("IntroPanel")).Visibility);
        });

    [TestMethod]
    public async Task When_ThePageExplainsTouch_ItShowsTheTouchHelp()
    {
        await WithPageAsync(static () => new SchedulerExamples(touchHelp: true) { Width = 1000, Height = 720 }, static page =>
        {
            Assert.AreEqual(GalleryLocalization.GetString("Scheduler_TouchHelp"), ((TextBlock)page.FindName("StatusText")).Text);
            return Task.CompletedTask;
        });
        await WithPageAsync(static () => new SchedulerExamples(touchHelp: false) { Width = 1000, Height = 720 }, static page =>
        {
            Assert.AreEqual(GalleryLocalization.GetString("Scheduler_Help"), ((TextBlock)page.FindName("StatusText")).Text);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public Task When_ThePageIsPhoneWide_TheSchedulerTakesMostOfItsWidth() =>
        WithPageAsync(static () => new SchedulerExamples { Width = 400, Height = 720 }, static async page =>
        {
            var scheduler = (DaisyScheduler)page.FindName("DemoScheduler");
            Assert.IsTrue(await WaitUntilAsync(page, () => Math.Abs(scheduler.ActualWidth - (400 - 16)) < 1), $"{scheduler.ActualWidth} at 400");

            page.Width = 1000;
            Assert.IsTrue(await WaitUntilAsync(page, () => Math.Abs(scheduler.ActualWidth - (1000 - 48)) < 1), $"{scheduler.ActualWidth} at 1000");
        });

    [TestMethod]
    public Task When_APageHasACollapsibleIntro_TheTitleBarFoldsIt() =>
        WithPageAsync(static () => new MainView(collapsesIntros: true) { Width = 680, Height = 500 }, static async view =>
        {
            var header = (UIElement)((Border)view.FindName("HeaderBorder")).Child;
            var bar = (Border)view.FindName("CategoryTitleBar");
            var menu = (DaisyButton)view.FindName("CategoryMenuButton");

            await NavigateAsync(view, "Sidebar_Scheduler");
            var intro = (FrameworkElement)VisiblePage(view).FindName("IntroPanel");
            Assert.AreEqual(Visibility.Collapsed, header.Visibility);
            Assert.AreEqual(Visibility.Collapsed, intro.Visibility);
            Assert.AreEqual(Visibility.Visible, menu.Visibility);
            Assert.AreEqual(bar.Padding.Top, bar.Padding.Bottom);
            // A new global size keeps the bar compact.
            FlowerySizeManager.ApplySize(DaisySize.Large);
            await SettleAsync(view);
            Assert.AreEqual(new Thickness(28, 6, 28, 6), bar.Padding);

            view.ToggleCategoryIntro(bar);
            Assert.AreEqual(Visibility.Visible, intro.Visibility);
            // A tap on the menu button opens the sidebar only.
            view.ToggleCategoryIntro(menu);
            Assert.AreEqual(Visibility.Visible, intro.Visibility);
            view.ToggleCategoryIntro(bar);
            Assert.AreEqual(Visibility.Collapsed, intro.Visibility);

            // The editor folds a shown intro to get the height, and the arrow of the bar follows.
            view.ToggleCategoryIntro(bar);
            Assert.AreEqual(180d, ((RotateTransform)((UIElement)view.FindName("CategoryIntroToggle")).RenderTransform).Angle);
            await OpenNewAsync((SchedulerExamples)VisiblePage(view));
            Assert.AreEqual(Visibility.Collapsed, intro.Visibility);
            Assert.AreEqual(0d, ((RotateTransform)((UIElement)view.FindName("CategoryIntroToggle")).RenderTransform).Angle);

            // A page without an intro shows the header again; a new scheduler page starts with its intro folded.
            await NavigateAsync(view, "Sidebar_Actions");
            Assert.AreEqual(Visibility.Visible, header.Visibility);
            Assert.AreEqual(Visibility.Collapsed, menu.Visibility);
            await NavigateAsync(view, "Sidebar_Scheduler");
            Assert.AreEqual(Visibility.Collapsed, header.Visibility);
            Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)VisiblePage(view).FindName("IntroPanel")).Visibility);
        });

    private static async Task OpenNewAsync(SchedulerExamples page)
    {
        var name = GalleryLocalization.GetString("Scheduler_New");
        var create = Descendants(page).OfType<Button>().Single(button => AutomationProperties.GetName(button) == name);
        new ButtonAutomationPeer(create).Invoke();
        await SettleAsync(page);
    }

    private static async Task NavigateAsync(MainView view, string tabHeader)
    {
        var sidebar = (GallerySidebar)view.FindName("ComponentSidebar");
        view.ComponentSidebar_ItemSelected(sidebar, new SidebarItemSelectedEventArgs(new SidebarItem { TabHeader = tabHeader },
            new SidebarCategory { Name = tabHeader }));
        await SettleAsync(view);
    }

    private static FrameworkElement VisiblePage(MainView view) =>
        ((Grid)view.FindName("MainContentHost")).Children.OfType<FrameworkElement>().Single(static child => child.Visibility == Visibility.Visible);

    private static Windows.Foundation.Rect Bounds(FrameworkElement element, FrameworkElement root) =>
        element.TransformToVisual(root).TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static Task WithPageAsync<T>(Func<T> create, Func<T, Task> test) where T : FrameworkElement =>
        RunAsync(() =>
        {
            var page = create();
            page.HorizontalAlignment = HorizontalAlignment.Left;
            page.VerticalAlignment = VerticalAlignment.Top;
            return page;
        }, test);

    // A test checks a result, not its speed: waits for at most two seconds.
    private static async Task<bool> WaitUntilAsync(FrameworkElement element, Func<bool> condition)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (condition())
                return true;
            await SettleAsync(element);
            await Task.Delay(50);
        }
        return condition();
    }

}
#endif
