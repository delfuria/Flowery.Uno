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
    [TestClass]
    public class Given_KanbanColumnTemplate
    {
        [TestMethod]
        public async Task When_StandardLayout_ColumnUsesSharedSurface()
        {
            var column = CreateColumn();
            var kanban = new FlowKanban { Board = new FlowKanbanData { Columns = { column } } };

            var columnControl = await LoadColumnAsync(kanban, control => control.ColumnData == column);

            Assert.IsNull(columnControl.Content, "The column surface comes from the template, not from inline content.");
            Assert.AreEqual(Visibility.Visible, FindNamed<FrameworkElement>(columnControl, "PART_PolicyIcon").Visibility);
            Assert.AreEqual(Visibility.Collapsed, FindNamed<FrameworkElement>(columnControl, "PART_LaneWipIndicator").Visibility);
            Assert.AreEqual(double.PositiveInfinity, FindNamed<ListView>(columnControl, "PART_TasksItemsControl").MaxHeight);
            Assert.AreEqual(kanban.Localization["Kanban_AddCard"], FindNamed<FlowKanbanAddCard>(columnControl, "PART_AddCardBottom").AddCardText);

            var card = await WaitForAsync(() => FindDescendants<FlowTaskCard>(columnControl).FirstOrDefault());
            Assert.IsNotNull(card, "Task cards were not realized.");
            Assert.AreSame(kanban.EditCardCommand, card.EditCommand);
            Assert.AreSame(kanban.RemoveCardCommand, card.CloseCommand);
        }

        [TestMethod]
        public async Task When_SwimlaneLayout_HeaderAndCellsUseSharedSurface()
        {
            var lane = new FlowKanbanLane { Id = "lane-1", Title = "Lane 1" };
            var column = CreateColumn(lane.Id);
            var kanban = new FlowKanban
            {
                Board = new FlowKanbanData
                {
                    GroupBy = FlowKanbanGroupBy.Lane,
                    Lanes = { lane },
                    Columns = { column }
                }
            };

            var header = await LoadColumnAsync(kanban, control => control.ColumnData == column && control.ShowColumnHeader && !control.ShowTasks);
            Assert.AreEqual(Visibility.Visible, FindNamed<FrameworkElement>(header, "PART_PolicyIcon").Visibility, "The swimlane header shows the policy icon like the other layouts.");

            var cell = await WaitForAsync(() => FindDescendants<FlowKanbanColumn>(kanban)
                .FirstOrDefault(control => control.ColumnData == column && control.LaneFilterId == lane.Id
                    && FindDescendants<Grid>(control).Any(grid => grid.Name == "PART_ColumnRoot")));
            Assert.IsNotNull(cell, "The swimlane cell was not realized.");
            Assert.AreEqual(kanban.SwimlaneCellMaxHeight, cell.TaskListMaxHeight);
            Assert.AreEqual(cell.TaskListMaxHeight, FindNamed<ListView>(cell, "PART_TasksItemsControl").MaxHeight, 0.01);
            Assert.AreEqual(Visibility.Visible, FindNamed<FrameworkElement>(cell, "PART_LaneWipIndicator").Visibility, "Lane cells show the lane WIP badge.");
        }

        private static FlowKanbanColumnData CreateColumn(string? laneId = null)
        {
            var column = new FlowKanbanColumnData
            {
                Title = "Review",
                PolicyText = "Definition of done",
                WipLimit = 3
            };
            column.Tasks.Add(new FlowTask { Title = "First", LaneId = laneId });
            column.Tasks.Add(new FlowTask { Title = "Second", LaneId = laneId });
            return column;
        }

        private static async Task<FlowKanbanColumn> LoadColumnAsync(FlowKanban kanban, Func<FlowKanbanColumn, bool> predicate)
        {
            RuntimeTestHelpers.AttachToHost(kanban);
            await RuntimeTestHelpers.EnsureLoadedAsync(kanban);

            // With no saved boards, OnKanbanLoaded switches to the Home view, which hides the columns.
            kanban.CurrentView = FlowKanbanView.Board;

            // Only columns whose template has been applied (layouts that are not visible never apply it).
            var column = await WaitForAsync(() => FindDescendants<FlowKanbanColumn>(kanban)
                .FirstOrDefault(control => predicate(control) && FindDescendants<Grid>(control).Any(grid => grid.Name == "PART_ColumnRoot")));
            Assert.IsNotNull(column, "Column control was not realized.");
            column.UpdateLayout();
            return column;
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

        private static T FindNamed<T>(DependencyObject root, string name)
            where T : FrameworkElement
        {
            var match = FindDescendants<T>(root).FirstOrDefault(element => element.Name == name);
            Assert.IsNotNull(match, $"{name} not found.");
            return match;
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
