using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Flowery.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
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

        [TestMethod]
        public async Task When_AnItemIsInvokedThroughUIAutomation_ItIsSelected()
        {
            var category = new SidebarCategory { Name = "Actions", DisplayName = "Actions" };
            var first = new SidebarItem { Id = "first", Name = "First", DisplayName = "First item" };
            var second = new SidebarItem { Id = "second", Name = "Second", DisplayName = "Second item" };
            category.Items.Add(first);
            category.Items.Add(second);
            var sidebar = new FloweryComponentSidebar
            {
                Categories = new ObservableCollection<SidebarCategory> { category },
                Height = 400
            };
            SidebarItem? selected = null;
            sidebar.ItemSelected += (_, args) => selected = args.Item;
            RuntimeTestHelpers.AttachToHost(sidebar);
            await RuntimeTestHelpers.EnsureLoadedAsync(sidebar);

            var host = await WaitForAsync(() => FindDescendants<ContentControl>(sidebar)
                .FirstOrDefault(control => control.Content is Border { Tag: SidebarItem item } && item == second));
            Assert.IsNotNull(host, "The second item should be shown.");
            Assert.IsTrue(host.IsTabStop, "Keyboard users can tab to a sidebar item.");

            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(host);
            Assert.AreEqual("Second item", peer.GetName());
            Assert.AreEqual(AutomationControlType.ListItem, peer.GetAutomationControlType());
            var invoke = peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
            Assert.IsNotNull(invoke, "A sidebar item exposes the Invoke pattern.");

            invoke.Invoke();
            Assert.AreSame(second, sidebar.SelectedItem);
            Assert.AreSame(second, selected);

            second.DisplayName = "Renamed item";
            Assert.AreEqual("Renamed item", peer.GetName(), "The name follows the item's display name.");
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
