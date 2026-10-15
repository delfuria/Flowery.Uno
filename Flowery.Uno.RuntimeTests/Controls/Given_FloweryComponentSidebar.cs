using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Flowery.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flowery.Uno.RuntimeTests.Controls
{
    [TestClass]
    public class Given_FloweryComponentSidebar
    {
        [TestMethod]
        public async Task When_SearchChangesWhileItemsAreLoading_ExpandedCategoriesGetTheirItems()
        {
            var categories = new ObservableCollection<SidebarCategory>();
            for (var c = 0; c < 12; c++)
            {
                var category = new SidebarCategory { Name = $"Category{c}", DisplayName = $"Category {c}" };
                for (var i = 0; i < 4; i++)
                {
                    category.Items.Add(new SidebarItem
                    {
                        Id = $"item-{c}-{i}",
                        Name = $"Item{c}_{i}",
                        DisplayName = i == 0 ? $"Tab {c}" : $"Other {c}-{i}"
                    });
                }

                categories.Add(category);
            }

            var sidebar = new FloweryComponentSidebar { Categories = categories, Height = 600 };
            RuntimeTestHelpers.AttachToHost(sidebar);
            await RuntimeTestHelpers.EnsureLoadedAsync(sidebar);

            // Each search text is applied by its own rebuild while the items of the previous one are still queued.
            foreach (var searchText in new[] { "t", "ta", "tab" })
            {
                sidebar.SearchText = searchText;
                await NextDispatchAsync(sidebar.DispatcherQueue);
            }

            var expanders = await WaitForAsync(() =>
            {
                var expanded = FindDescendants<DaisyExpander>(sidebar).Where(expander => expander.IsExpanded).ToList();
                return expanded.Count == categories.Count && expanded.All(HasItems) ? expanded : null;
            });

            var empty = FindDescendants<DaisyExpander>(sidebar).Count(expander => expander.IsExpanded && !HasItems(expander));
            Assert.IsNotNull(expanders, $"Every category matching 'tab' should show its items; {empty} expanded categories stayed empty.");
        }

        private static bool HasItems(DaisyExpander expander) =>
            expander.ExpanderContent is Panel panel && panel.Children.Count > 0;

        private static Task NextDispatchAsync(DispatcherQueue dispatcherQueue)
        {
            var completion = new TaskCompletionSource();
            dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () => completion.SetResult());
            return completion.Task;
        }

        private static async Task<T?> WaitForAsync<T>(Func<T?> probe)
            where T : class
        {
            for (var attempt = 0; attempt < 50; attempt++)
            {
                if (probe() is { } result)
                    return result;

                await Task.Delay(100);
            }

            return null;
        }

        private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
            where T : DependencyObject
        {
            var childCount = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                {
                    yield return match;
                }

                foreach (var descendant in FindDescendants<T>(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
