using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Flowery.Uno.Kanban.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Flowery.Uno.RuntimeTests.Controls.KanbanTestTree;

namespace Flowery.Uno.RuntimeTests.Controls
{
    [TestClass]
    public class Given_KanbanHome
    {
        private static readonly string[] TileMenuKeys =
        [
            "Kanban_Home_Open",
            "Kanban_Home_Rename",
            "Kanban_Home_Duplicate",
            "Kanban_Home_Export",
            "Kanban_Home_Delete"
        ];

        [TestMethod]
        public async Task When_HomeViewShowsABoard_ItsTileMenuNamesItsEntriesAndOpensTheBoard()
        {
            var store = new FlowKanbanBoardStore(new InMemoryStateStorage());
            var saved = new FlowKanbanData { Title = "Saved", Columns = { new FlowKanbanColumnData { Title = "Todo" } } };
            Assert.IsTrue(store.TrySaveBoard(saved, out var saveError), saveError?.Message);
            var kanban = new FlowKanban { BoardStore = store };
            RuntimeTestHelpers.AttachToHost(kanban);
            await RuntimeTestHelpers.EnsureLoadedAsync(kanban);
            kanban.CurrentView = FlowKanbanView.Home;

            var items = await GetTileMenuItemsAsync(kanban);
            var board = kanban.Boards.Single();
            foreach (var item in items)
            {
                Assert.AreEqual(kanban.Localization[item.LocalizationKey], item.Text, item.LocalizationKey);
                Assert.IsNotNull(item.Command, $"{item.LocalizationKey} has no command.");
                Assert.AreSame(board, item.CommandParameter, item.LocalizationKey);
            }

            // The board loads on a worker thread and comes back through the dispatcher.
            var open = items[0];
            open.Command.Execute(open.CommandParameter);
            for (var attempt = 0; attempt < 50 && kanban.CurrentView != FlowKanbanView.Board; attempt++)
            {
                await Task.Delay(100);
            }

            Assert.AreEqual(FlowKanbanView.Board, kanban.CurrentView, "The board did not open.");
            Assert.AreEqual(saved.Id, kanban.Board.Id);
        }

        [TestMethod]
        public async Task When_HomeViewIsAttachedAgain_ItsTileMenuStillOpensTheBoard()
        {
            var board = new FlowBoardMetadata { Id = "board", Title = "Board" };
            var opened = new List<object?>();
            var home = new FlowKanbanHome
            {
                Boards = new ObservableCollection<FlowBoardMetadata> { board },
                OpenBoardCommand = new CommandRecorder(opened)
            };
            var host = new ContentControl { Content = home };
            RuntimeTestHelpers.AttachToHost(host);
            await RuntimeTestHelpers.CompleteLayoutAsync(host);

            // As a tab switch or a cached page does.
            host.Content = null;
            await RuntimeTestHelpers.CompleteLayoutAsync(host);
            host.Content = home;
            await RuntimeTestHelpers.CompleteLayoutAsync(host, home);

            var open = (await GetTileMenuItemsAsync(home))[0];
            Assert.AreEqual(home.Localization["Kanban_Home_Open"], open.Text);
            Assert.AreSame(board, open.CommandParameter);
            open.Command.Execute(open.CommandParameter);

            Assert.AreSame(board, opened.Single());
        }

        // The menu of the first board tile, in the order of the template.
        private static async Task<IReadOnlyList<DaisyMenuFlyoutItem>> GetTileMenuItemsAsync(FrameworkElement root)
        {
            DaisyButton? menuButton = null;
            for (var attempt = 0; attempt < 50 && menuButton == null; attempt++)
            {
                menuButton = FindDescendants<DaisyButton>(root).FirstOrDefault(button => button.Name == "PART_BoardMenuButton");
                if (menuButton == null)
                {
                    await Task.Delay(100);
                }
            }

            Assert.IsNotNull(menuButton, "No board tile was realized.");
            var flyout = menuButton.Flyout as MenuFlyout;
            Assert.IsNotNull(flyout, "The board tile has no menu.");
            var items = flyout.Items.OfType<DaisyMenuFlyoutItem>().ToList();
            CollectionAssert.AreEqual(TileMenuKeys, items.Select(item => item.LocalizationKey).ToList());
            return items;
        }

        private sealed class CommandRecorder(List<object?> parameters) : ICommand
        {
            public event EventHandler? CanExecuteChanged { add { } remove { } }

            public bool CanExecute(object? parameter) => true;

            public void Execute(object? parameter) => parameters.Add(parameter);
        }
    }
}
