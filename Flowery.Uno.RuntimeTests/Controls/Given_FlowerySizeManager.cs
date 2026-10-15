using Flowery.Controls;
using Flowery.Enums;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flowery.Uno.RuntimeTests.Controls
{
    [TestClass]
    public class Given_FlowerySizeManager
    {
        [TestMethod]
        public void When_GetControlHeight_Called_ReturnsStandardHeights()
        {
            var expected = new (DaisySize Size, double Height)[]
            {
                (DaisySize.ExtraSmall, 24d),
                (DaisySize.Small, 28d),
                (DaisySize.Medium, 32d),
                (DaisySize.Large, 34d),
                (DaisySize.ExtraLarge, 36d)
            };

            foreach (var (size, height) in expected)
            {
                Assert.AreEqual(height, FlowerySizeManager.GetControlHeight(size), $"Height for {size}");
            }
        }

        [TestMethod]
        public void When_GetControlHeight_SizeUnknown_ReturnsMediumHeight()
        {
            Assert.AreEqual(32d, FlowerySizeManager.GetControlHeight((DaisySize)999));
        }
    }
}
