using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Flowery.Uno.Kanban.Controls;
using Flowery.Uno.Kanban.Interfaces;
using Microsoft.UI.Dispatching;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flowery.Uno.RuntimeTests.Controls
{
    // An edit starts a debounced auto-save. An edit that still waits for it is saved at once, on the board it was made on
    // and into the store it was meant for, when the board leaves the tree, is replaced, changes its store or loads a
    // board, and when the app goes to the background; the timer does not save later into whatever is current then.
    [TestClass]
    public class Given_KanbanAutoSave
    {
        [TestMethod]
        public Task When_BoardUnloadsWithAPendingAutoSave_TheEditIsSavedAtOnce() =>
            UnloadWithPendingAutoSaveAsync(onHomeView: false);

        [TestMethod]
        public Task When_BoardUnloadsOnTheHomeViewWithAPendingAutoSave_TheEditIsSavedBeforeTheLastBoardIsCleared() =>
            UnloadWithPendingAutoSaveAsync(onHomeView: true);

        [TestMethod]
        public Task When_BoardIsReplacedWithAPendingAutoSave_TheReplacedBoardIsSaved() =>
            ReplaceWithPendingAutoSaveAsync(replaceStore: false);

        [TestMethod]
        public Task When_StoreIsReplacedWithAPendingAutoSave_TheEditGoesToTheOldStore() =>
            ReplaceWithPendingAutoSaveAsync(replaceStore: true);

        [TestMethod]
        public async Task When_AppGoesToTheBackgroundWithAPendingAutoSave_TheEditIsSavedAtOnce()
        {
            var (kanban, task, store) = await LoadBoardAsync();
            var edited = kanban.Board;
            var timer = EditWithPendingAutoSave(kanban, task, "Edited");
            try
            {
                // On a phone the board stays loaded in the background, where the system may end the app at any time.
                FlowKanban.RaiseAppEnteringBackground();

                Assert.AreSame(edited, store.SavedBoards.Single());
                Assert.IsFalse(timer.IsRunning);

                // An unloaded board no longer listens.
                await UnloadAsync(kanban);
                store.SavedBoards.Clear();
                task.Title = "Edited after the unload";
                Assert.IsTrue(timer.IsRunning, "The edit did not start an auto-save.");
                FlowKanban.RaiseAppEnteringBackground();
                Assert.AreEqual(0, store.SavedBoards.Count);
            }
            finally
            {
                timer.Stop();
            }
        }

        [TestMethod]
        public async Task When_OpenBoardIsDeletedWithAPendingAutoSave_ItIsNotSavedBack()
        {
            var (kanban, task, store) = await LoadBoardAsync();
            var edited = kanban.Board;
            var timer = EditWithPendingAutoSave(kanban, task, "Edited");

            Assert.IsTrue(kanban.TryDeleteBoard(edited.Id, out var error), error?.Message);

            Assert.AreNotSame(edited, kanban.Board);
            Assert.AreEqual(0, store.SavedBoards.Count);
            Assert.IsFalse(timer.IsRunning);
            Assert.IsFalse(store.ListBoards().Any(board => board.Id == edited.Id), "The deleted board was saved again.");
        }

        [TestMethod]
        public async Task When_OpenBoardIsLoadedAgainWithAPendingAutoSave_TheLoadedBoardHasTheEdit()
        {
            var (kanban, task, _) = await LoadBoardAsync();
            var edited = kanban.Board;
            EditWithPendingAutoSave(kanban, task, "Edited");

            Assert.IsTrue(kanban.TryLoadBoard(edited.Id, out var error), error?.Message);

            Assert.AreNotSame(edited, kanban.Board);
            Assert.AreEqual("Edited", FindTask(kanban, task.Id).Title);
        }

        [TestMethod]
        public async Task When_OpenBoardIsOpenedFromTheHomeViewWithAPendingAutoSave_TheOpenedBoardHasTheEdit()
        {
            var (kanban, task, _) = await LoadBoardAsync();
            var edited = kanban.Board;
            EditWithPendingAutoSave(kanban, task, "Edited");

            // Home and back to the same board before the auto-save came; the board loads on a worker thread.
            kanban.CurrentView = FlowKanbanView.Home;
            kanban.OpenBoardCommand.Execute(kanban.Boards.Single(board => board.Id == edited.Id));
            for (var attempt = 0; attempt < 50 && kanban.CurrentView != FlowKanbanView.Board; attempt++)
            {
                await Task.Delay(100);
            }

            Assert.AreEqual(FlowKanbanView.Board, kanban.CurrentView, "The board did not open.");
            Assert.AreNotSame(edited, kanban.Board);
            Assert.AreEqual("Edited", FindTask(kanban, task.Id).Title);
        }

        private static async Task UnloadWithPendingAutoSaveAsync(bool onHomeView)
        {
            var (kanban, task, store) = await LoadBoardAsync();
            var edited = kanban.Board;
            var timer = EditWithPendingAutoSave(kanban, task, "Edited");
            // The user leaves for the home view before the auto-save came, then the board leaves the tree.
            if (onHomeView)
            {
                kanban.CurrentView = FlowKanbanView.Home;
            }

            // Before, the timer stayed on and saved later, into the storage of whatever ran then.
            await UnloadAsync(kanban);

            Assert.AreSame(edited, store.SavedBoards.Single());
            Assert.IsFalse(timer.IsRunning);
            // The save comes before the home view clears the last board, so the next start opens the home view.
            if (onHomeView)
            {
                Assert.IsNull(kanban.LastBoardId);
            }
            else
            {
                Assert.AreEqual(edited.Id, kanban.LastBoardId);
            }
        }

        private static async Task ReplaceWithPendingAutoSaveAsync(bool replaceStore)
        {
            var (kanban, task, store) = await LoadBoardAsync();
            var edited = kanban.Board;
            var lastBoardId = kanban.LastBoardId;
            var timer = EditWithPendingAutoSave(kanban, task, "Edited");
            var otherStore = new CountingBoardStore();
            try
            {
                // Before, the timer saved later: the board open then, into the store set then.
                if (replaceStore)
                {
                    kanban.BoardStore = otherStore;
                }
                else
                {
                    kanban.Board = new FlowKanbanData { Columns = { new FlowKanbanColumnData { Title = "Other" } } };
                }

                Assert.AreSame(edited, store.SavedBoards.Single());
                Assert.AreEqual(0, otherStore.SavedBoards.Count);
                // A replaced board is no longer open, so it does not become the last board; with a new store it stays open.
                Assert.AreEqual(replaceStore ? edited.Id : lastBoardId, kanban.LastBoardId);
            }
            finally
            {
                timer.Stop();
            }
        }

        // A loaded board on its board view, saved in its store, with no save counted yet.
        private static async Task<(FlowKanban Kanban, FlowTask Task, CountingBoardStore Store)> LoadBoardAsync()
        {
            var task = new FlowTask { Title = "Task" };
            var board = new FlowKanbanData
            {
                Title = "Auto-save",
                Columns = { new FlowKanbanColumnData { Title = "Todo", Tasks = { task } } }
            };
            var store = new CountingBoardStore();
            Assert.IsTrue(store.TrySaveBoard(board, out var error), error?.Message);
            var kanban = new FlowKanban { Board = board, BoardStore = store };
            RuntimeTestHelpers.AttachToHost(kanban);
            await RuntimeTestHelpers.EnsureLoadedAsync(kanban);

            // Earlier tests share the state storage, and with it the settings.
            kanban.AutoSaveAfterEdits = true;
            kanban.CurrentView = FlowKanbanView.Board;
            store.SavedBoards.Clear();
            return (kanban, task, store);
        }

        // An edit whose auto-save waits. The debounce of 800 ms becomes minutes, so a slow runner does not save the edit
        // before the step under test does.
        private static DispatcherQueueTimer EditWithPendingAutoSave(FlowKanban kanban, FlowTask task, string title)
        {
            task.Title = title + " (draft)";
            var timer = AutoSaveTimer(kanban);
            timer.Stop();
            timer.Interval = TimeSpan.FromMinutes(5);
            task.Title = title;
            Assert.IsTrue(timer.IsRunning, "The edit did not start an auto-save.");
            return timer;
        }

        private static DispatcherQueueTimer AutoSaveTimer(FlowKanban kanban)
        {
            var field = typeof(FlowKanban).GetField("_autoSaveTimer", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "FlowKanban has no _autoSaveTimer field.");
            var timer = field.GetValue(kanban) as DispatcherQueueTimer;
            Assert.IsNotNull(timer, "The edit did not create the auto-save timer.");
            return timer;
        }

        // WinUI raises Unloaded after the element left the tree, not while it is removed.
        private static async Task UnloadAsync(FlowKanban kanban)
        {
            var unloaded = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnUnloaded(object sender, RoutedEventArgs e) => unloaded.TrySetResult(null);
            kanban.Unloaded += OnUnloaded;
            try
            {
                RuntimeTestHelpers.GetHostPanel().Children.Clear();
                await unloaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }
            finally
            {
                kanban.Unloaded -= OnUnloaded;
            }
        }

        private static FlowTask FindTask(FlowKanban kanban, string taskId) =>
            kanban.Board.Columns.SelectMany(column => column.Tasks).Single(task => task.Id == taskId);

        private sealed class CountingBoardStore : IBoardStore
        {
            private readonly FlowKanbanBoardStore _inner = new(new InMemoryStateStorage());

            public List<FlowKanbanData> SavedBoards { get; } = new();

            public IReadOnlyList<FlowBoardMetadata> ListBoards() => _inner.ListBoards();

            public bool TryLoadBoard(string boardId, out FlowKanbanData? board, out Exception? error) =>
                _inner.TryLoadBoard(boardId, out board, out error);

            public bool TrySaveBoard(FlowKanbanData board, out Exception? error)
            {
                SavedBoards.Add(board);
                return _inner.TrySaveBoard(board, out error);
            }

            public bool TryDeleteBoard(string boardId, out Exception? error) => _inner.TryDeleteBoard(boardId, out error);

            public bool TryExportBoard(string boardId, out string? json, out Exception? error) =>
                _inner.TryExportBoard(boardId, out json, out error);
        }
    }
}
