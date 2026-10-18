using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Flowery.Controls;
using Flowery.Enums;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.UI;

namespace Flowery.Uno.RuntimeTests.Controls
{
    [TestClass]
    public class Given_DaisyDivider
    {
        [TestMethod]
        public async Task When_NotLoaded_VisualTreeIsNotRebuilt()
        {
            var divider = new DaisyDivider
            {
                DividerStyle = DaisyDividerStyle.Wave,
                Size = DaisySize.Large,
                DividerText = "Section"
            };

            Assert.IsNull(divider.Content, "Property changes before Loaded should not build native views that are discarded again.");

            RuntimeTestHelpers.AttachToHost(divider);
            await RuntimeTestHelpers.EnsureLoadedAsync(divider);
            var loadedGrid = divider.Content as Grid;
            Assert.IsNotNull(loadedGrid, "Loading builds the visual tree.");

            divider.DividerStyle = DaisyDividerStyle.Dashed;
            var restyledGrid = divider.Content as Grid;
            Assert.IsNotNull(restyledGrid);
            Assert.AreNotSame(loadedGrid, restyledGrid, "A style change while loaded rebuilds the visual tree.");

            var unloaded = new TaskCompletionSource();
            divider.Unloaded += (_, _) => unloaded.TrySetResult();
            RuntimeTestHelpers.AttachToHost(new Border());
            await unloaded.Task;

            divider.DividerStyle = DaisyDividerStyle.Dotted;
            Assert.AreSame(restyledGrid, divider.Content, "A style change while unloaded waits for the next Loaded.");
        }

        [TestMethod]
        public async Task When_Inset_Rendered_DrawsShadowAndHighlightAtEverySize()
        {
            foreach (var horizontal in new[] { false, true })
            {
                foreach (var size in AllSizes)
                {
                    var (darker, lighter) = await CountChangedPixelsAsync(DaisyDividerStyle.Inset, size, horizontal);
                    var label = $"Inset {size}, Horizontal={horizontal}";
                    Assert.IsGreaterThan(0, darker, $"{label}: the shadow line should be drawn.");
                    Assert.IsGreaterThan(0, lighter, $"{label}: the highlight line should be drawn.");
                }
            }
        }

        [TestMethod]
        public async Task When_Tapered_Rendered_DrawsLineAtEverySize()
        {
            foreach (var horizontal in new[] { false, true })
            {
                foreach (var size in AllSizes)
                {
                    var (darker, lighter) = await CountChangedPixelsAsync(DaisyDividerStyle.Tapered, size, horizontal, tolerance: 0);
                    Assert.IsGreaterThan(0, darker + lighter, $"Tapered {size}, Horizontal={horizontal}: the line should be drawn.");
                }
            }
        }

        private static readonly DaisySize[] AllSizes =
            [DaisySize.ExtraSmall, DaisySize.Small, DaisySize.Medium, DaisySize.Large, DaisySize.ExtraLarge];

        // The tapered line is a 1 px sliver anti-aliased over two pixel rows, so its contrast depends on the Skia backend: tolerance 0 counts any changed pixel.
        private static async Task<(int Darker, int Lighter)> CountChangedPixelsAsync(DaisyDividerStyle style, DaisySize size, bool horizontal, int tolerance = 16)
        {
            // Mid gray shows both the dark shadow and the light highlight of Inset.
            const byte background = 128;
            var divider = new DaisyDivider
            {
                DividerStyle = style,
                Size = size,
                Horizontal = horizontal,
                Width = horizontal ? double.NaN : 200,
                Height = horizontal ? 60 : double.NaN
            };
            FlowerySizeManager.SetIgnoreGlobalSize(divider, true);
            var host = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, background, background, background)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Padding = new Thickness(8),
                Child = divider
            };

            RuntimeTestHelpers.AttachToHost(host);
            await RuntimeTestHelpers.EnsureLoadedAsync(divider);
            host.UpdateLayout();

            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(host);
            var pixels = (await bitmap.GetPixelsAsync()).ToArray();

            var darker = 0;
            var lighter = 0;
            for (var offset = 0; offset + 3 < pixels.Length; offset += 4)
            {
                var luminance = (pixels[offset] + pixels[offset + 1] + pixels[offset + 2]) / 3;
                if (luminance < background - tolerance)
                {
                    darker++;
                }
                else if (luminance > background + tolerance)
                {
                    lighter++;
                }
            }

            return (darker, lighter);
        }
    }
}
