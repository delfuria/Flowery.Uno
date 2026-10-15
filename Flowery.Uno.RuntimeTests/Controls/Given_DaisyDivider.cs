using System.Threading.Tasks;
using Flowery.Controls;
using Flowery.Enums;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
    }
}
