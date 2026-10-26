#if WINDOWS || (__SKIA__ && !__WASM__)
using System.Threading.Tasks;
using Flowery.Controls;
using Flowery.Theming;
using Flowery.Uno.RuntimeTests;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flowery.Uno.Gallery.Tests;

/// <summary>Hosts a Gallery view or page for a runtime test and gives the next tests the global size, theme and
/// neumorphic settings back, which MainView and the pages change.</summary>
internal static class GalleryTestHost
{
    // CI runners can hold back low-priority dispatcher work for more than five seconds.
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(30);

    /// <param name="create">Creates the element after the global state is saved, so it may change that state first.</param>
    public static async Task RunAsync<T>(Func<T> create, Func<T, Task> test) where T : FrameworkElement
    {
        var host = RuntimeTestContext.HostPanel ?? throw new InvalidOperationException("Runtime test host is not initialized.");
        var previousSize = FlowerySizeManager.CurrentSize;
        var previousGlobal = FlowerySizeManager.UseGlobalSizeByDefault;
        var previousAuto = FlowerySizeManager.EnableGlobalAutoSize;
        var previousTheme = DaisyThemeManager.CurrentThemeName;
        var previousIntensity = DaisyBaseContentControl.GlobalNeumorphicIntensity;
        T? element = null;
        try
        {
            host.Children.Clear();
            element = create();
            host.Children.Add(element);
            await SettleAsync(element);
            await test(element);
        }
        finally
        {
            try
            {
                if (element != null)
                    host.Children.Remove(element);
                // Unloaded comes later in WinUI: the removed tree must stop listening before the state changes back.
                await SettleAsync(host);
            }
            finally
            {
                FlowerySizeManager.UseGlobalSizeByDefault = previousGlobal;
                FlowerySizeManager.EnableGlobalAutoSize = previousAuto;
                FlowerySizeManager.ApplySize(previousSize);
                if (previousTheme != null)
                    DaisyThemeManager.ApplyTheme(previousTheme);
                DaisyBaseContentControl.GlobalNeumorphicIntensity = previousIntensity;
            }
        }
    }

    public static async Task SettleAsync(FrameworkElement element)
    {
        if (!element.IsLoaded)
        {
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            RoutedEventHandler onLoaded = (_, _) => loaded.TrySetResult();
            element.Loaded += onLoaded;
            try { await loaded.Task.WaitAsync(SettleTimeout); }
            finally { element.Loaded -= onLoaded; }
        }
        // MainView restores initial navigation through two low-priority dispatcher hops; a selection made before they ran
        // would be deferred.
        for (var pass = 0; pass < 2; pass++)
        {
            var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.IsTrue(element.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => queued.SetResult()));
            await queued.Task.WaitAsync(SettleTimeout);
        }
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<object> onRendering = (_, _) => rendered.TrySetResult();
        CompositionTarget.Rendering += onRendering;
        try
        {
            element.UpdateLayout();
            await rendered.Task.WaitAsync(SettleTimeout);
        }
        finally { CompositionTarget.Rendering -= onRendering; }
    }

    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
#endif
