using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flowery.Uno.RuntimeTests.Controls
{
    [TestClass]
    public class Given_WrapPanel
    {
        [TestMethod]
        public async Task When_ChildIsCollapsed_ItTakesNoSpacing()
        {
            var first = new Border { Width = 20, Height = 10 };
            var hidden = new Border { Width = 20, Height = 10, Visibility = Visibility.Collapsed };
            var last = new Border { Width = 20, Height = 10 };
            var panel = new Flowery.Controls.WrapPanel
            {
                HorizontalSpacing = 10,
                VerticalSpacing = 5,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Children = { first, hidden, last }
            };

            RuntimeTestHelpers.AttachToHost(panel);
            await RuntimeTestHelpers.EnsureLoadedAsync(panel);
            panel.UpdateLayout();

            Assert.AreEqual(50d, panel.DesiredSize.Width, 0.5, "Two 20 px items with one 10 px gap.");
            Assert.AreEqual(10d, panel.DesiredSize.Height, 0.5, "A collapsed child must not start a new line.");
            Assert.AreEqual(30d, LayoutInformation.GetLayoutSlot(last).X, 1.0, "The last item stays on the first line after its predecessor and one gap.");
        }

        [TestMethod]
        public async Task When_ItemsDoNotFit_TheyWrapWithLineSpacing()
        {
            var first = new Border { Width = 40, Height = 10 };
            var second = new Border { Width = 40, Height = 10 };
            var panel = new Flowery.Controls.WrapPanel
            {
                Width = 60,
                HorizontalSpacing = 10,
                VerticalSpacing = 5,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Children = { first, second }
            };

            RuntimeTestHelpers.AttachToHost(panel);
            await RuntimeTestHelpers.EnsureLoadedAsync(panel);
            panel.UpdateLayout();

            var slot = LayoutInformation.GetLayoutSlot(second);
            // Skia rounds layout slots to physical pixels, so allow one pixel.
            Assert.AreEqual(0d, slot.X, 1.0, "The second item starts the next line.");
            Assert.AreEqual(15d, slot.Y, 1.0, "The next line starts below the first line plus the line spacing.");
        }
    }
}
