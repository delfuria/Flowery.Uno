using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Flowery.Uno.Kanban.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flowery.Uno.RuntimeTests.Controls
{
    // Visual tree lookups and column loading shared by the Kanban runtime tests.
    internal static class KanbanTestTree
    {
        public static IEnumerable<T> FindDescendants<T>(DependencyObject root)
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

        // Attaches the board, shows its board view and returns the first column that matches and has its template.
        public static async Task<FlowKanbanColumn> LoadColumnAsync(FlowKanban kanban, Func<FlowKanbanColumn, bool> predicate)
        {
            RuntimeTestHelpers.AttachToHost(kanban);
            await RuntimeTestHelpers.EnsureLoadedAsync(kanban);

            // With no saved boards, OnKanbanLoaded switches to the Home view, which hides the columns.
            kanban.CurrentView = FlowKanbanView.Board;

            // Only columns whose template has been applied (layouts that are not visible never apply it).
            for (var attempt = 0; attempt < 50; attempt++)
            {
                var column = FindDescendants<FlowKanbanColumn>(kanban)
                    .FirstOrDefault(control => predicate(control) && FindDescendants<Grid>(control).Any(grid => grid.Name == "PART_ColumnRoot"));
                if (column != null)
                {
                    column.UpdateLayout();
                    return column;
                }

                await Task.Delay(100);
            }

            Assert.Fail("Column control was not realized.");
            return null!;
        }
    }
}
