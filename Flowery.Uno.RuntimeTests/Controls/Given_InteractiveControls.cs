using System.Collections.Generic;
using System.Threading.Tasks;
using Flowery.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.Foundation;

namespace Flowery.Uno.RuntimeTests.Controls
{
    [TestClass]
    public class Given_InteractiveControls
    {
        [TestMethod]
        public async Task When_Carousel_Loads_DefaultsAndNavigationWork()
        {
            var carousel = new DaisyCarousel
            {
                Content = new StackPanel
                {
                    Children =
                    {
                        new Border { Width = 10, Height = 10 },
                        new Border { Width = 10, Height = 10 },
                        new Border { Width = 10, Height = 10 }
                    }
                }
            };

            RuntimeTestHelpers.AttachToHost(carousel);
            await RuntimeTestHelpers.EnsureLoadedAsync(carousel);

            Assert.AreEqual(3, carousel.ItemCount);
            Assert.AreEqual(0, carousel.SelectedIndex);
            Assert.IsTrue(carousel.ShowNavigation);
            Assert.IsFalse(carousel.WrapAround);
            Assert.AreEqual(Orientation.Horizontal, carousel.Orientation);
            Assert.AreEqual(FlowerySlideshowMode.Manual, carousel.Mode);
            Assert.AreEqual(3.0, carousel.SlideInterval);

            carousel.Next();
            Assert.AreEqual(1, carousel.SelectedIndex);
            carousel.Next();
            Assert.AreEqual(2, carousel.SelectedIndex);
            carousel.Next();
            Assert.AreEqual(2, carousel.SelectedIndex);
        }

        [TestMethod]
        public void When_Select_SelectionSyncs_IndexAndItem()
        {
            var select = new DaisySelect();
            select.Items.Add("A");
            select.Items.Add("B");

            select.SelectedIndex = 1;
            Assert.AreEqual("B", select.SelectedItem);

            select.SelectedItem = "A";
            Assert.AreEqual(0, select.SelectedIndex);
        }

        [TestMethod]
        public void When_Select_LocalizationSource_UpdatesItems()
        {
            var select = new DaisySelect();
            select.Items.Add(new DaisySelectItem { LocalizationKey = "KeyA", Text = "A" });
            select.Items.Add(new DaisySelectItem { LocalizationKey = "KeyB", Text = "B" });

            select.LocalizationSource = new Dictionary<string, string>
            {
                ["KeyA"] = "Alpha",
                ["KeyB"] = "Beta"
            };

            select.RefreshLocalization();

            var first = (DaisySelectItem)select.Items[0];
            var second = (DaisySelectItem)select.Items[1];

            Assert.AreEqual("Alpha", first.Text);
            Assert.AreEqual("Beta", second.Text);
        }

        [TestMethod]
        public async Task When_Modal_IsOpen_TogglesHitTesting()
        {
            var modal = new DaisyModal();
            RuntimeTestHelpers.AttachToHost(modal);
            await RuntimeTestHelpers.EnsureLoadedAsync(modal);

            Assert.IsFalse(modal.IsOpen);
            Assert.IsFalse(modal.IsHitTestVisible);
            Assert.AreEqual(16.0, modal.TopLeftRadius);

            modal.IsOpen = true;
            Assert.IsTrue(modal.IsHitTestVisible);

            modal.IsOpen = false;
            Assert.IsFalse(modal.IsHitTestVisible);
        }

        [TestMethod]
        public async Task When_Keyboard_Covers_Modal_Input_Moves_Above_It()
        {
            // The on-screen keyboard on Android covers the lower half of the window; the field at the bottom of the
            // dialog has to stay visible while the user types, and return once the keyboard is gone.
            var input = new TextBox();
            var modal = new DaisyModal
            {
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children = { new Border { Height = 160 }, input }
                }
            };
            RuntimeTestHelpers.AttachToHost(modal);
            modal.IsOpen = true;
            await RuntimeTestHelpers.CompleteLayoutAsync(modal, input);

            var modalBounds = GetWindowBounds(modal);
            var inputBefore = GetWindowBounds(input);
            var keyboardTop = modalBounds.Top + modalBounds.Height / 2;
            Assert.IsTrue(inputBefore.Bottom > keyboardTop, $"The field must start below the keyboard edge: {inputBefore}, {keyboardTop}");

            modal.ApplyInputPaneOcclusion(new Rect(modalBounds.Left, keyboardTop, modalBounds.Width, modalBounds.Height), Rect.Empty);
            await RuntimeTestHelpers.CompleteLayoutAsync(modal);

            var inputAbove = GetWindowBounds(input);
            Assert.IsTrue(inputAbove.Bottom <= keyboardTop, $"The field stays under the keyboard: {inputAbove}, {keyboardTop}");

            modal.ApplyInputPaneOcclusion(Rect.Empty, Rect.Empty);
            await RuntimeTestHelpers.CompleteLayoutAsync(modal);

            Assert.AreEqual(inputBefore.Top, GetWindowBounds(input).Top, 1.0);
        }

        [TestMethod]
        public async Task When_Keyboard_Shrinks_Tall_Modal_It_Stays_Below_Status_Bar()
        {
            // A dialog taller than the part above the keyboard is limited to it; on Android it must not move up under
            // the status bar, which lies outside the visible bounds of the window.
            var content = new Border { Height = 2000 };
            var modal = new DaisyModal { Content = content };
            RuntimeTestHelpers.AttachToHost(modal);
            modal.IsOpen = true;
            await RuntimeTestHelpers.CompleteLayoutAsync(modal, content);

            var modalBounds = GetWindowBounds(modal);
            // Taller than the dialog's margin and padding together, so a dialog that ignores it starts above it.
            const double statusBarHeight = 80;
            var statusBarBottom = modalBounds.Top + statusBarHeight;
            var keyboardTop = modalBounds.Top + modalBounds.Height / 2;
            var visibleBounds = new Rect(modalBounds.Left, statusBarBottom, modalBounds.Width, modalBounds.Height - statusBarHeight);

            modal.ApplyInputPaneOcclusion(new Rect(modalBounds.Left, keyboardTop, modalBounds.Width, modalBounds.Height), visibleBounds);
            await RuntimeTestHelpers.CompleteLayoutAsync(modal);

            var contentBounds = GetWindowBounds(content);
            Assert.IsTrue(contentBounds.Top >= statusBarBottom, $"The dialog reaches under the status bar: {contentBounds}, {statusBarBottom}");
            Assert.IsTrue(contentBounds.Top < keyboardTop, $"The dialog starts under the keyboard: {contentBounds}, {keyboardTop}");
        }

        private static Rect GetWindowBounds(FrameworkElement element)
        {
            return element.TransformToVisual(null).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }

        [TestMethod]
        public async Task When_Popover_OpensAndCloses()
        {
            var popover = new DaisyPopover
            {
                TriggerContent = new TextBlock { Text = "Trigger" },
                PopoverContent = new TextBlock { Text = "Content" }
            };

            RuntimeTestHelpers.AttachToHost(popover);
            await RuntimeTestHelpers.EnsureLoadedAsync(popover);

            Assert.IsFalse(popover.IsOpen);
            Assert.AreEqual(DaisyPopoverPlacement.Bottom, popover.Placement);
            Assert.AreEqual(8.0, popover.VerticalOffset);
            Assert.IsTrue(popover.IsLightDismissEnabled);
            Assert.IsTrue(popover.ToggleOnClick);

            popover.IsOpen = true;
            Assert.IsTrue(popover.IsOpen);
            popover.IsOpen = false;
            Assert.IsFalse(popover.IsOpen);
        }

        [TestMethod]
        public async Task When_Tooltip_Loads_DefaultsHold()
        {
            var tooltip = new DaisyTooltip
            {
                Content = new TextBlock { Text = "Target" }
            };

            RuntimeTestHelpers.AttachToHost(tooltip);
            await RuntimeTestHelpers.EnsureLoadedAsync(tooltip);

            Assert.AreEqual(string.Empty, tooltip.Tip);
            Assert.IsFalse(tooltip.IsOpen);
            Assert.AreEqual(PlacementMode.Top, tooltip.Placement);

            tooltip.Tip = "Hello";
            tooltip.IsOpen = true;
            Assert.AreEqual("Hello", tooltip.Tip);
            var native = (ToolTip)ToolTipService.GetToolTip(tooltip);
            Assert.IsTrue(native.IsOpen);

            // The native tooltip is a separate popup; removing its owner must close it.
            var host = RuntimeTestHelpers.GetHostPanel();
            var unloaded = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            tooltip.Unloaded += (_, _) => unloaded.TrySetResult(null);
            host.Children.Remove(tooltip);
            await unloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(native.IsOpen, "An unloaded tooltip must not stay on screen.");
            Assert.IsTrue(tooltip.IsOpen);

            RuntimeTestHelpers.AttachToHost(tooltip);
            await RuntimeTestHelpers.EnsureLoadedAsync(tooltip).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(native.IsOpen, "A tooltip that is still requested opens again when it loads.");
            tooltip.IsOpen = false;
            Assert.IsFalse(native.IsOpen);
        }
    }
}
