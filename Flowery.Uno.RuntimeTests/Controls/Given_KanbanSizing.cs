using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Flowery.Controls;
using Flowery.Enums;
using Flowery.Uno.Kanban.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.Foundation;

namespace Flowery.Uno.RuntimeTests.Controls
{
    [TestClass]
    public class Given_KanbanSizing
    {
        [TestMethod]
        public async Task When_CardIsNarrow_EndDateWrapsInsteadOfClipping()
        {
            var task = new FlowTask
            {
                Title = "Launch search filters",
                PlannedStartDate = DateTime.Today.AddDays(-81),
                PlannedEndDate = DateTime.Today.AddDays(-8)
            };
            var card = new FlowTaskCard { Task = task, Width = 160 };

            RuntimeTestHelpers.AttachToHost(card);
            await RuntimeTestHelpers.EnsureLoadedAsync(card);

            var dateRow = await WaitForAsync(() => FindDescendants<Flowery.Controls.WrapPanel>(card).FirstOrDefault(panel => panel.Name == "PART_DateRow"));
            Assert.IsNotNull(dateRow, "The date row was not realized.");
            Assert.IsTrue(card.HasDateRange);

            var dates = await WaitForAsync(() =>
            {
                card.UpdateLayout();
                var laidOut = FindDescendants<DaisyIconText>(dateRow).Where(text => text.ActualWidth > 0).ToList();
                return laidOut.Count == 2 ? laidOut : null;
            });
            var found = string.Join(", ", FindDescendants<DaisyIconText>(dateRow).Select(text => $"'{text.Text}' {text.ActualWidth}x{text.ActualHeight} {text.Visibility}"));
            Assert.IsNotNull(dates, $"Both dates should be laid out. Row {dateRow.ActualWidth}x{dateRow.ActualHeight}, footer {card.HasFooter}, dates: {found}");
            foreach (var date in dates)
            {
                var bounds = date.TransformToVisual(dateRow).TransformBounds(new Rect(0, 0, date.ActualWidth, date.ActualHeight));
                Assert.IsLessThanOrEqualTo(dateRow.ActualWidth + 0.5, bounds.Right, $"'{date.Text}' ends outside the {dateRow.ActualWidth} px date row.");
            }

            var start = dates[0].TransformToVisual(dateRow).TransformPoint(default);
            var end = dates[1].TransformToVisual(dateRow).TransformPoint(default);
            Assert.IsGreaterThan(start.Y, end.Y, "The end date should move to the next line.");
            Assert.AreEqual(0d, end.X, 0.5, "The wrapped end date should start at the left edge.");
        }

        [TestMethod]
        public async Task When_SidebarButtonsGrow_SidebarFitsThem()
        {
            var previousButtonWidth = 0d;
            var kanban = new FlowKanban { Board = new FlowKanbanData { Columns = { new FlowKanbanColumnData { Title = "Todo" } } } };

            RuntimeTestHelpers.AttachToHost(kanban);
            await RuntimeTestHelpers.EnsureLoadedAsync(kanban);

            var sidebar = await WaitForAsync(() => FindDescendants<Border>(kanban).FirstOrDefault(border => border.Name == "PART_SidebarBorder"));
            Assert.IsNotNull(sidebar, "The sidebar was not realized.");

            foreach (var size in new[] { DaisySize.Small, DaisySize.Large, DaisySize.ExtraLarge })
            {
                // With a global size of Large the Gallery shows these buttons at the Large height (36 px).
                foreach (var button in FindDescendants<DaisyButton>(sidebar))
                {
                    button.Size = size;
                }

                var buttons = await WaitForAsync(() =>
                {
                    kanban.UpdateLayout();
                    var laidOut = FindDescendants<DaisyButton>(sidebar).Where(button => button.ActualWidth > 0).ToList();
                    return laidOut.Count > 0 && laidOut.Max(button => button.ActualWidth) > previousButtonWidth ? laidOut : null;
                });
                var found = string.Join(", ", FindDescendants<DaisyButton>(sidebar).Select(button => $"{button.Size} {button.ActualWidth}"));
                Assert.IsNotNull(buttons, $"{size}: sidebar buttons should grow beyond {previousButtonWidth} px; found {found}.");

                var available = sidebar.ActualWidth - sidebar.Padding.Left - sidebar.Padding.Right
                    - sidebar.BorderThickness.Left - sidebar.BorderThickness.Right;
                foreach (var button in buttons)
                {
                    Assert.IsLessThanOrEqualTo(available + 0.5, button.ActualWidth, $"{size}: a {button.ActualWidth} px button does not fit into {available} px.");
                }

                previousButtonWidth = buttons.Max(button => button.ActualWidth);
                Assert.IsGreaterThanOrEqualTo(56d, sidebar.ActualWidth, $"{size}: the sidebar keeps its minimum width.");
            }
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
