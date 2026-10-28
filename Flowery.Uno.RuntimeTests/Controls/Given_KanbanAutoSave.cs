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
            try
            {
                Assert.IsTrue(kanban.TryDeleteBoard(edited.Id, out var error), error?.Message);

                Assert.AreNotSame(edited, kanban.Board);
                Assert.AreEqual(0, store.SavedBoards.Count);
                Assert.IsFalse(timer.IsRunning);
                Assert.IsFalse(store.ListBoards().Any(board => board.Id == edited.Id), "The deleted board was saved again.");
            }
            finally
            {
                timer.Stop();
            }
        }

        [TestMethod]
        public async Task When_OpenBoardIsLoadedAgainWithAPendingAutoSave_TheLoadedBoardHasTheEdit()
        {
            var (kanban, task, _) = await LoadBoardAsync();
            var edited = kanban.Board;
            var timer = EditWithPendingAutoSave(kanban, task, "Edited");
            try
            {
                Assert.IsTrue(kanban.TryLoadBoard(edited.Id, out var error), error?.Message);

                Assert.AreNotSame(edited, kanban.Board);
                Assert.AreEqual("Edited", FindTask(kanban, task.Id).Title);
            }
            finally
            {
                timer.Stop();
            }
        }

        [TestMethod]
        public async Task When_OpenBoardIsOpenedFromTheHomeViewWithAPendingAutoSave_TheOpenedBoardHasTheEdit()
        {
            var (kanban, task, _) = await LoadBoardAsync();
            var edited = kanban.Board;
            var timer = EditWithPendingAutoSave(kanban, task, "Edited");
            try
            {
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
            finally
            {
                timer.Stop();
            }
        }

        [TestMethod]
        public async Task When_BoardWithAnIdThatIsNoGuidUnloadsOnTheHomeView_NoSaveComesAfterTheLastBoardIsCleared()
        {
            var (kanban, task, store) = await LoadBoardAsync(boardId: "sprint-42");
            var timer = EditWithPendingAutoSave(kanban, task, "Edited");
            try
            {
                kanban.CurrentView = FlowKanbanView.Home;
                await UnloadAsync(kanban);

                // The save gives the board a valid ID, a change of the board that the save already holds. Before, it
                // requested another save, which set the cleared last board ID again later.
                Assert.AreNotEqual("sprint-42", kanban.Board.Id);
                Assert.AreSame(kanban.Board, store.SavedBoards.Single());
                Assert.IsFalse(timer.IsRunning, "The save requested another one.");
                Assert.IsNull(kanban.LastBoardId);
            }
            finally
            {
                timer.Stop();
            }
        }

        [TestMethod]
        public void When_BoardAndThenItsStoreAreSetBeforeTheBoardLoads_NothingIsSavedIntoAnotherStore()
        {
            var task = new FlowTask { Title = "Task" };
            var archive = new FlowKanbanColumnData { Title = "Archive" };
            var board = new FlowKanbanData
            {
                Columns = { new FlowKanbanColumnData { Title = "Todo", Tasks = { task } }, archive },
                ArchiveColumnId = archive.Id
            };
            var store = new CountingBoardStore();

            // As in the object initializer of a host: the board, then the store it lives in. Showing the board marks its
            // archive column, which is no edit.
            var kanban = new FlowKanban { Board = board, BoardStore = store };
            try
            {
                Assert.IsFalse(IsAutoSavePending(kanban), "Marking the archive column requested an auto-save.");

                // A change made before the board loads is saved later, into the store set by then.
                task.Title = "Edited";
                Assert.IsTrue(IsAutoSavePending(kanban), "The edit did not start an auto-save.");
                var otherStore = new CountingBoardStore();
                kanban.BoardStore = otherStore;

                Assert.AreEqual(0, store.SavedBoards.Count);
                Assert.IsTrue(IsAutoSavePending(kanban), "The change of the store saved the board at once.");
                Assert.IsFalse(
                    StateStorageProvider.Instance.GetKeys("kanban.board.").Any(key => key.EndsWith(board.Id, StringComparison.Ordinal)),
                    "The board was saved into the default store.");
            }
            finally
            {
                AutoSaveTimerOrNull(kanban)?.Stop();
            }
        }

        [TestMethod]
        public void When_BoardIsReplacedBeforeItLoads_AWaitingSaveIsNotFlushedIntoTheStore()
        {
            var task = new FlowTask { Title = "Task" };
            var first = new FlowKanbanData { Columns = { new FlowKanbanColumnData { Title = "Todo", Tasks = { task } } } };
            var store = new CountingBoardStore();
            var kanban = new FlowKanban { BoardStore = store, Board = first };
            try
            {
                // An edit before the board loads comes from setting it up, not from the user.
                task.Title = "Edited";
                Assert.IsTrue(IsAutoSavePending(kanban), "The edit did not start an auto-save.");

                kanban.Board = new FlowKanbanData { Columns = { new FlowKanbanColumnData { Title = "Other" } } };

                Assert.AreEqual(0, store.SavedBoards.Count, "The replaced board was saved at once.");
                Assert.IsTrue(IsAutoSavePending(kanban), "The waiting save was dropped.");
            }
            finally
            {
                AutoSaveTimerOrNull(kanban)?.Stop();
            }
        }

        [TestMethod]
        public async Task When_TheBoardResolvesAssigneeDetailsOrACardIsSelected_NoAutoSaveIsRequested()
        {
            var (kanban, task, store) = await LoadBoardAsync();
            var cardEdits = new List<string?>();
            void OnCardEdited(object? sender, FlowTask edited) => cardEdits.Add(edited.Title);
            try
            {
                task.AssigneeId = "person-1";
                task.Assignee = "Mina";
                AutoSaveTimer(kanban).Stop();
                store.SavedBoards.Clear();
                kanban.CardEdited += OnCardEdited;

                // The adapter supplies the name the task has already, so only details that are not saved change.
                var avatar = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                kanban.AssigneeAdapter = new FlowKanbanAssigneeAdapter(new[]
                {
                    new FlowKanbanAssignee("person-1", "Mina", avatar, new[] { "Designer" })
                });
                await kanban.RefreshAssigneesAsync();
                Assert.AreSame(avatar, task.AssigneeAvatarSource);
                CollectionAssert.AreEqual(new[] { "Designer" }, task.AssigneeRoles.ToArray());
                Assert.AreEqual(0, cardEdits.Count, "Resolved assignee details raised CardEdited.");

                task.IsSelected = true;

                Assert.IsFalse(IsAutoSavePending(kanban), "View state that is not saved requested an auto-save.");
                Assert.AreEqual(0, store.SavedBoards.Count);
            }
            finally
            {
                kanban.CardEdited -= OnCardEdited;
                AutoSaveTimerOrNull(kanban)?.Stop();
            }
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

        // A loaded board on its board view, saved in its store, with no save counted yet. A board with its own ID is not
        // saved first, as the store would give it a valid one.
        private static async Task<(FlowKanban Kanban, FlowTask Task, CountingBoardStore Store)> LoadBoardAsync(string? boardId = null)
        {
            var task = new FlowTask { Title = "Task" };
            var board = new FlowKanbanData
            {
                Title = "Auto-save",
                Columns = { new FlowKanbanColumnData { Title = "Todo", Tasks = { task } } }
            };
            var store = new CountingBoardStore();
            if (boardId == null)
            {
                Assert.IsTrue(store.TrySaveBoard(board, out var error), error?.Message);
            }
            else
            {
                board.Id = boardId;
            }

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
            var timer = AutoSaveTimerOrNull(kanban);
            Assert.IsNotNull(timer, "The edit did not create the auto-save timer.");
            return timer;
        }

        private static DispatcherQueueTimer? AutoSaveTimerOrNull(FlowKanban kanban)
        {
            var field = typeof(FlowKanban).GetField("_autoSaveTimer", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "FlowKanban has no _autoSaveTimer field.");
            return field.GetValue(kanban) as DispatcherQueueTimer;
        }

        private static bool IsAutoSavePending(FlowKanban kanban) => AutoSaveTimerOrNull(kanban)?.IsRunning == true;

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
