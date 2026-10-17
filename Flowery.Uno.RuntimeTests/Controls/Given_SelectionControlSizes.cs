using System.Linq;
using System.Threading.Tasks;
using Flowery.Controls;
using Flowery.Enums;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flowery.Uno.RuntimeTests.Controls
{
    [TestClass]
    public class Given_SelectionControlSizes
    {
        private static readonly DaisySize[] Tiers =
            [DaisySize.ExtraSmall, DaisySize.Small, DaisySize.Medium, DaisySize.Large, DaisySize.ExtraLarge];

        [TestMethod]
        public async Task When_CheckBoxSized_IndicatorUsesCompactSizes()
        {
            double[] boxes = [8, 10, 12, 14, 16];
            for (var i = 0; i < Tiers.Length; i++)
            {
                var checkBox = new DaisyCheckBox { Content = "Label", Size = Tiers[i], IsChecked = true };
                RuntimeTestHelpers.AttachToHost(checkBox);
                await RuntimeTestHelpers.EnsureLoadedAsync(checkBox);
                await Task.Delay(100); // WinUI builds the visual content after Loaded
                checkBox.UpdateLayout();

                var box = Descendants<Border>(checkBox).First(b => b.Width > 0 && b.Width == b.Height);
                Assert.AreEqual(boxes[i], box.Width, $"CheckBox {Tiers[i]}");
            }
        }

        [TestMethod]
        public async Task When_RadioSized_IndicatorUsesItsOwnCompactSizes()
        {
            double[] outer = [8, 10, 12, 14, 16];
            double[] dots = [4, 5, 6, 7, 8];
            for (var i = 0; i < Tiers.Length; i++)
            {
                var radio = new DaisyRadio { Content = "Label", Size = Tiers[i], IsChecked = true };
                RuntimeTestHelpers.AttachToHost(radio);
                await RuntimeTestHelpers.EnsureLoadedAsync(radio);
                await Task.Delay(100);
                radio.UpdateLayout();

                var squares = Descendants<FrameworkElement>(radio)
                    .Where(e => e.Width > 0 && e.Width == e.Height)
                    .Select(e => e.Width)
                    .Distinct()
                    .OrderByDescending(w => w)
                    .ToList();
                CollectionAssert.Contains(squares, outer[i], $"Radio {Tiers[i]} outer");
                CollectionAssert.Contains(squares, dots[i], $"Radio {Tiers[i]} dot");
            }
        }

        [TestMethod]
        public async Task When_ToggleSized_TrackAndKnobUseCompactSizesAndKnobStaysInside()
        {
            double[] widths = [16, 20, 24, 28, 32];
            double[] heights = [8, 10, 12, 14, 16];
            double[] knobs = [4, 6, 8, 10, 12];
            for (var i = 0; i < Tiers.Length; i++)
            {
                var toggle = new DaisyToggle { Size = Tiers[i], IsOn = true };
                RuntimeTestHelpers.AttachToHost(toggle);
                await RuntimeTestHelpers.EnsureLoadedAsync(toggle);
                await Task.Delay(300); // let the knob animation finish

                var knob = Descendants<Ellipse>(toggle).Single();
                var track = Descendants<Border>(toggle).First(b => b.Width == widths[i]);
                Assert.AreEqual(heights[i], track.Height, $"Toggle {Tiers[i]} height");
                Assert.AreEqual(knobs[i], knob.Width, $"Toggle {Tiers[i]} knob");

                var offset = ((TranslateTransform)knob.RenderTransform).X;
                Assert.AreEqual(widths[i] - knobs[i] - toggle.TogglePadding, offset, 0.01, $"Toggle {Tiers[i]} checked knob offset");
                Assert.IsTrue(offset + knob.Width <= track.Width, $"Toggle {Tiers[i]} knob must stay inside the track");
            }
        }

        [TestMethod]
        public async Task When_ToggleResizedAfterLoad_MeasuresLikeAToggleCreatedAtThatSize()
        {
            // A global size change sets Size on toggles that are already loaded; nothing from the first size may stick (#70).
            (DaisySize Initial, DaisySize Target)[] changes =
                [(DaisySize.Medium, DaisySize.ExtraSmall), (DaisySize.ExtraSmall, DaisySize.ExtraLarge)];
            foreach (var (initial, target) in changes)
            {
                var resized = await LoadToggleAsync(initial);
                resized.Size = target;
                await Task.Delay(100);
                resized.UpdateLayout();
                var resizedWidth = resized.ActualWidth;
                var resizedHeight = resized.ActualHeight;

                // Attaching the fresh toggle removes the resized one from the host, so its size is read above.
                var fresh = await LoadToggleAsync(target);
                Assert.AreEqual(fresh.ActualWidth, resizedWidth, 0.01, $"Toggle {initial} -> {target} width");
                Assert.AreEqual(fresh.ActualHeight, resizedHeight, 0.01, $"Toggle {initial} -> {target} height");
            }
        }

        private static async Task<DaisyToggle> LoadToggleAsync(DaisySize size)
        {
            // Top-left alignment, so ActualWidth/ActualHeight are the toggle's own size and not the host's.
            var toggle = new DaisyToggle { Size = size, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            RuntimeTestHelpers.AttachToHost(toggle);
            await RuntimeTestHelpers.EnsureLoadedAsync(toggle);
            await Task.Delay(100);
            toggle.UpdateLayout();
            return toggle;
        }

        private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) yield return match;
                foreach (var nested in Descendants<T>(child)) yield return nested;
            }
        }
    }
}
