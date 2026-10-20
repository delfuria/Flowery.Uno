using Flowery.Skia;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace Flowery.Uno.RuntimeTests.Controls
{
    [TestClass]
    public class Given_SkiaImageProcessing
    {
        [TestMethod]
        public void When_GlassEffect_ProcessesPixels_BlurSaturationAndRoundedClipApply()
        {
            using var source = new SKBitmap(new SKImageInfo(64, 64, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(source))
            using (var paint = new SKPaint { Color = SKColors.Lime })
            {
                canvas.Clear(SKColors.Red);
                canvas.DrawRect(32, 0, 32, 64, paint);
            }

            var sharpBytes = SkiaImageProcessor.ApplyGlassEffect(source, 0, 0, SKColors.Transparent, 0);
            var blurredBytes = SkiaImageProcessor.ApplyGlassEffect(source, 4, 0, SKColors.Transparent, 0, cornerRadius: 12);
            Assert.IsNotNull(sharpBytes, "The native processor must produce an encoded image.");
            Assert.IsNotNull(blurredBytes, "The native blur and clipping path must produce an encoded image.");
            using var sharp = SKBitmap.Decode(sharpBytes);
            using var blurred = SKBitmap.Decode(blurredBytes);
            Assert.IsNotNull(sharp);
            Assert.IsNotNull(blurred);
            Assert.AreEqual(source.Width, blurred.Width);
            Assert.AreEqual(source.Height, blurred.Height);

            var sharpLeft = sharp.GetPixel(31, 32);
            var sharpRight = sharp.GetPixel(32, 32);
            var blurredLeft = blurred.GetPixel(31, 32);
            var blurredRight = blurred.GetPixel(32, 32);
            foreach (var pixel in new[] { sharpLeft, sharpRight, blurredLeft, blurredRight })
            {
                Assert.IsTrue(Math.Abs(pixel.Red - pixel.Green) <= 2 && Math.Abs(pixel.Red - pixel.Blue) <= 2,
                    $"Saturation zero must produce grayscale pixels, but rendered {pixel}.");
            }

            var sharpContrast = Math.Abs(sharpLeft.Red - sharpRight.Red);
            var blurredContrast = Math.Abs(blurredLeft.Red - blurredRight.Red);
            Assert.IsTrue(sharpContrast > 80, "The source must contain a strong luminance boundary.");
            Assert.IsTrue(blurredContrast < sharpContrast / 4,
                $"Blur must smooth that boundary: sharp contrast {sharpContrast}, blurred contrast {blurredContrast}.");
            Assert.IsTrue(blurredLeft.Red > sharpLeft.Red + 10 && blurredRight.Red < sharpRight.Red - 10,
                "Blur must mix pixels from both sides of the boundary.");
            Assert.AreEqual((byte)0, blurred.GetPixel(0, 0).Alpha, "Rounded corners must remain transparent.");
            Assert.AreEqual((byte)255, blurredLeft.Alpha, "The interior must remain opaque.");
        }
    }
}
