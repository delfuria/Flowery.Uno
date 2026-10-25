using System.Threading.Tasks;
using Flowery.Controls;
using Flowery.Enums;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flowery.Uno.RuntimeTests.Controls
{
    [TestClass]
    public class Given_ButtonDesignRegression
    {
        [TestMethod]
        public async Task When_ButtonResizedAfterLoad_CornerRadiusFollowsTheNewSize()
        {
            // A global size change sets Size on loaded buttons; the token radius must follow instead of sticking (#71).
            // Comparing with fresh buttons keeps the test independent of the host's global size.
            var medium = await LoadButtonAsync(DaisySize.Medium);
            var small = await LoadButtonAsync(DaisySize.ExtraSmall);
            var large = await LoadButtonAsync(DaisySize.ExtraLarge);
            Assert.AreNotEqual(medium.CornerRadius, small.CornerRadius, "The tiers need different radii for this test to mean anything");

            var resized = await LoadButtonAsync(DaisySize.Medium);
            resized.Size = DaisySize.ExtraSmall;
            Assert.AreEqual(small.CornerRadius, resized.CornerRadius, "Medium -> ExtraSmall");

            resized.Size = DaisySize.ExtraLarge;
            Assert.AreEqual(large.CornerRadius, resized.CornerRadius, "ExtraSmall -> ExtraLarge");
        }

        [TestMethod]
        public void When_ButtonsAreLaidOutTheFirstTime_TheyHaveTheirSizeHeight()
        {
            // Auto-sized panels keep the first measured height until something else invalidates them.
            var large = new DaisyButton { Content = "Save", Size = DaisySize.Large, Variant = DaisyButtonVariant.Ghost };
            var automatic = new DaisyButton { Content = "Save" };
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            panel.Children.Add(large);
            panel.Children.Add(automatic);
            RuntimeTestHelpers.AttachToHost(panel);

            Assert.AreEqual(Flowery.Theming.DaisyResourceLookup.GetDouble("DaisySizeLargeHeight"), large.ActualHeight, 0.5);
            Assert.AreEqual(Flowery.Theming.DaisyResourceLookup.GetDouble($"DaisySize{automatic.ResolvedSize}Height"), automatic.ActualHeight, 0.5);
        }

        [TestMethod]
        public async Task When_ButtonGetsItsOwnCornerRadius_ResizeKeepsIt()
        {
            // DaisyJoin and DaisyButtonGroup set CornerRadius on their buttons; a size change must not undo that.
            var joined = new CornerRadius(0, 8, 8, 0);
            var button = await LoadButtonAsync(DaisySize.Medium);

            button.CornerRadius = joined;
            button.Size = DaisySize.Large;
            Assert.AreEqual(joined, button.CornerRadius);
        }

        private static async Task<DaisyButton> LoadButtonAsync(DaisySize size)
        {
            var button = new DaisyButton { Content = "Save", Size = size };
            RuntimeTestHelpers.AttachToHost(button);
            await RuntimeTestHelpers.EnsureLoadedAsync(button);
            return button;
        }
    }
}
