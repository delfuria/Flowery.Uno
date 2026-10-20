#if WINDOWS || (__SKIA__ && !__WASM__)
using System.Diagnostics;
using System.Threading.Tasks;
using Flowery.Controls;
using Flowery.Helpers;
using Flowery.Uno.Gallery.Examples;
using Flowery.Uno.Gallery.Localization;
using Flowery.Uno.RuntimeTests;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flowery.Uno.Gallery.Tests;

[TestClass]
public class Given_CarouselGalleryCycles
{
    [TestMethod]
    public async Task When_TransitionOutlastsTimer_EachCompletedSlideGetsItsNextEffect()
    {
        var host = RuntimeTestContext.HostPanel
            ?? throw new InvalidOperationException("Runtime test host is not initialized.");
        var page = new CarouselExamples();
        DaisyToggle? enabled = null;
        DaisyCarousel? carousel = null;
        var completed = new List<(int Index, FlowerySlideEffect Effect)>();

        void OnSlideChanged(object? sender, FlowerySlideChangedEventArgs args)
        {
            var changedCarousel = (DaisyCarousel)sender!;
            completed.Add((changedCarousel.SelectedIndex, changedCarousel.SlideEffect));
        }

        try
        {
            host.Children.Clear();
            host.Children.Add(page);
            await WaitForAsync(() => page.IsLoaded, "Gallery page did not load.");
            enabled = Named<DaisyToggle>(page, "InteractiveCarouselEnabledToggle");
            carousel = Named<DaisyCarousel>(page, "InteractiveCarousel");
            var infinite = Named<DaisyToggle>(page, "CarouselInfiniteToggle");
            enabled.IsOn = false;
            // Let the page's deferred localization/default selection finish before configuring it.
            await DrainDispatcherAsync(page.DispatcherQueue);
            await DrainDispatcherAsync(page.DispatcherQueue);
            SelectTag(Named<DaisySelect>(page, "CarouselModeSelect"), "Slideshow");
            SelectTag(Named<DaisySelect>(page, "CarouselTransitionSelect"), "MatrixRain");
            SelectTag(Named<DaisySelect>(page, "CarouselDurationSelect"), "1.0");
            SelectTag(Named<DaisySelect>(page, "CarouselEffectSelect"), "PanLeft");
            Named<DaisyNumericUpDown>(page, "CarouselIntervalBox").Value = 1m;
            Named<DaisyNumericUpDown>(page, "CarouselSliceCountBox").Value = 20m;
            Named<DaisyNumericUpDown>(page, "CarouselStaggerMsBox").Value = 200m;
            Named<DaisyToggle>(page, "CarouselStaggerToggle").IsOn = true;
            infinite.IsOn = true;

            Assert.AreEqual(GalleryLocalization.Instance["Gallery_Carousel_Infinite_Label"],
                AutomationProperties.GetName(infinite), "The visible toggle label must also be accessible.");

            var outgoingImage = Descendant<Image>(carousel)
                ?? throw new InvalidOperationException("The Gallery carousel has no image.");
            var outgoingSlide = VisualTreeHelper.GetParent(outgoingImage) as UIElement
                ?? throw new InvalidOperationException("The image has no slide parent.");

            carousel.SlideChanged += OnSlideChanged;
            enabled.IsOn = true;
            await WaitForAsync(() => carousel.SelectedIndex == 1, "The first automatic transition did not start.");

            Assert.AreEqual(FlowerySlideEffect.PanLeft, FlowerySlideEffects.GetEffect(outgoingSlide),
                "Selecting the incoming effect must not restart the outgoing image's effect.");

            await WaitForAsync(() => completed.Count >= 2, "Two automatic transitions did not complete.", 15);
            Assert.HasCount(2, completed, "A timer tick must not queue an extra transition while one is pending.");
            Assert.AreEqual((1, FlowerySlideEffect.PanRight), completed[0],
                "The first completed slide must receive the next sequential effect.");
            Assert.AreEqual((2, FlowerySlideEffect.PanUp), completed[1],
                "The second completed slide must receive its own next sequential effect.");
        }
        finally
        {
            try
            {
                if (enabled != null)
                    enabled.IsOn = false;
                if (carousel != null)
                    carousel.SlideChanged -= OnSlideChanged;
            }
            finally
            {
                host.Children.Remove(page);
            }
        }
    }

    private static T Named<T>(FrameworkElement root, string name) where T : FrameworkElement
    {
        return root.FindName(name) as T
            ?? throw new InvalidOperationException($"Missing Gallery control: {name}.");
    }

    private static void SelectTag(DaisySelect select, string tag)
    {
        for (var index = 0; index < select.Items.Count; index++)
        {
            if (select.Items[index] is DaisySelectItem item && item.Tag?.ToString() == tag)
            {
                select.SelectedIndex = index;
                return;
            }
        }

        Assert.Fail($"Missing Gallery selection: {tag}.");
    }

    private static T? Descendant<T>(DependencyObject root) where T : FrameworkElement
    {
        if (root is T match)
            return match;

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var matchInChild = Descendant<T>(VisualTreeHelper.GetChild(root, index));
            if (matchInChild != null)
                return matchInChild;
        }

        return null;
    }

    private static async Task WaitForAsync(Func<bool> condition, string failureMessage, int timeoutSeconds = 5)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.IsLessThan(timeoutSeconds, elapsed.Elapsed.TotalSeconds, failureMessage);
            await Task.Delay(16);
        }
    }

    private static Task DrainDispatcherAsync(DispatcherQueue dispatcher)
    {
        var completion = new TaskCompletionSource();
        Assert.IsTrue(dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () => completion.SetResult()),
            "The Gallery dispatcher rejected the initialization barrier.");
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
#endif
