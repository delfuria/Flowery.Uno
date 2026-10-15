using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Flowery.Uno.Kanban.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flowery.Uno.RuntimeTests.Controls
{
    [TestClass]
    public class Given_KanbanAssignees
    {
        [TestMethod]
        public void When_AssigneeCreated_NameAndRolesAreNormalized()
        {
            var assignee = new FlowKanbanAssignee("person-42", "  Mina  ", roles: new[] { " Designer ", "designer", "", "Reviewer" });

            Assert.AreEqual("person-42", assignee.Id);
            Assert.AreEqual("Mina", assignee.DisplayName);
            CollectionAssert.AreEqual(new[] { "Designer", "Reviewer" }, assignee.Roles.ToArray());
            Assert.AreEqual("person-7", new FlowKanbanAssignee("person-7", " ").DisplayName);
            Assert.ThrowsExactly<ArgumentException>(() => new FlowKanbanAssignee(" ", "Nobody"));
        }

        [TestMethod]
        public async Task When_AdapterHasDirectList_IdsAreCaseSensitiveAndFirstEntryWins()
        {
            var adapter = new FlowKanbanAssigneeAdapter(new[]
            {
                new FlowKanbanAssignee("person-1", "First"),
                new FlowKanbanAssignee("Person-1", "Second"),
                new FlowKanbanAssignee("person-1", "Duplicate")
            });

            var assignees = await adapter.GetAssigneesAsync();

            Assert.HasCount(2, assignees);
            Assert.AreEqual("First", (await adapter.ResolveAssigneeAsync("person-1"))!.DisplayName);
            Assert.AreEqual("Second", (await adapter.ResolveAssigneeAsync("Person-1"))!.DisplayName);
            Assert.IsNull(await adapter.ResolveAssigneeAsync("PERSON-1"));
            Assert.IsNull(await adapter.ResolveAssigneeAsync(" "));
        }

        [TestMethod]
        public async Task When_SetAssignees_ReplacesListAndRaisesAssigneesChanged()
        {
            var adapter = new FlowKanbanAssigneeAdapter();
            var changes = 0;
            adapter.AssigneesChanged += (_, _) => changes++;

            adapter.SetAssignees(new[] { new FlowKanbanAssignee("a", "Alpha") });

            Assert.AreEqual(1, changes);
            Assert.AreEqual("Alpha", (await adapter.GetAssigneesAsync()).Single().DisplayName);
        }

        [TestMethod]
        public async Task When_ResolverReturnsAnotherId_AdapterThrows()
        {
            var adapter = new FlowKanbanAssigneeAdapter((id, _) =>
                Task.FromResult<FlowKanbanAssignee?>(new FlowKanbanAssignee(id.ToUpperInvariant(), "Wrong")));

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => adapter.ResolveAssigneeAsync("person-1"));
        }

        [TestMethod]
        public async Task When_BoardRefreshes_TasksGetNamesAvatarsAndRoles_AndUnknownIdsStay()
        {
            var known = new FlowTask { Title = "Known", AssigneeId = "person-1", Assignee = "Old name" };
            var unknown = new FlowTask { Title = "Unknown", AssigneeId = "Person-1", Assignee = "Other" };
            var kanban = CreateBoard(known, unknown, new FlowTask { Title = "Unassigned" });
            kanban.AssigneeAdapter = new FlowKanbanAssigneeAdapter(new[]
            {
                new FlowKanbanAssignee("person-1", "Mina", roles: new[] { "Designer" })
            });

            await LoadAsync(kanban);
            await kanban.RefreshAssigneesAsync();

            Assert.AreEqual("Mina", known.Assignee);
            CollectionAssert.AreEqual(new[] { "Designer" }, known.AssigneeRoles.ToArray());
            Assert.AreEqual("Person-1", unknown.AssigneeId, "Unknown IDs are kept exactly.");
            Assert.AreEqual("Other", unknown.Assignee, "Unknown IDs keep their last known name.");
            Assert.IsEmpty(unknown.AssigneeRoles);

            var options = kanban.AssigneeFilterOptions.ToList();
            Assert.HasCount(2, options);
            var resolved = options.Single(option => option.Id == "person-1");
            Assert.AreEqual("Mina", resolved.DisplayName);
            Assert.IsTrue(resolved.IsResolved);
            var unresolved = options.Single(option => option.Id == "Person-1");
            Assert.IsFalse(unresolved.IsResolved);
            Assert.IsTrue(unresolved.DisplayName.StartsWith("Other (", StringComparison.Ordinal), unresolved.DisplayName);
        }

        [TestMethod]
        public async Task When_ResolverOnlyAdapter_ResolvesSavedIdsWithoutAddingChoices()
        {
            var task = new FlowTask { Title = "Saved", AssigneeId = "person-9", Assignee = "Old" };
            var kanban = CreateBoard(task);
            var requested = new List<string>();
            kanban.AssigneeAdapter = new FlowKanbanAssigneeAdapter((id, _) =>
            {
                requested.Add(id);
                return Task.FromResult<FlowKanbanAssignee?>(new FlowKanbanAssignee(id, "Resolved", roles: new[] { "Reviewer" }));
            });

            await LoadAsync(kanban);
            requested.Clear();
            await kanban.RefreshAssigneesAsync();

            CollectionAssert.AreEqual(new[] { "person-9" }, requested);
            Assert.AreEqual("Resolved", task.Assignee);
            CollectionAssert.AreEqual(new[] { "Reviewer" }, task.AssigneeRoles.ToArray());
        }

        [TestMethod]
        public async Task When_LoadFails_FailureIsReported_AndTaskDataStays()
        {
            var task = new FlowTask { Title = "Saved", AssigneeId = "person-1", Assignee = "Kept" };
            var kanban = CreateBoard(task);
            var failures = new List<FlowKanbanAssigneeAdapterFailedEventArgs>();
            kanban.AssigneeAdapterFailed += (_, args) => failures.Add(args);
            kanban.AssigneeAdapter = new FlowKanbanAssigneeAdapter(
                _ => Task.FromException<IReadOnlyList<FlowKanbanAssignee>>(new InvalidOperationException("Service down")));

            await LoadAsync(kanban);
            failures.Clear();
            await kanban.RefreshAssigneesAsync();

            Assert.HasCount(1, failures);
            Assert.AreEqual(FlowKanbanAssigneeAdapterOperation.LoadAssignees, failures[0].Operation);
            Assert.AreEqual("Service down", failures[0].Exception.Message);
            Assert.IsNull(failures[0].AssigneeId);
            Assert.AreEqual("person-1", task.AssigneeId);
            Assert.AreEqual("Kept", task.Assignee);
        }

        [TestMethod]
        public async Task When_ResolveFailsOrReturnsAnotherId_FailureNamesTheId()
        {
            var failing = new FlowTask { Title = "Failing", AssigneeId = "fails", Assignee = "Kept" };
            var mismatched = new FlowTask { Title = "Mismatch", AssigneeId = "mismatch", Assignee = "Kept too" };
            var kanban = CreateBoard(failing, mismatched);
            var failures = new List<FlowKanbanAssigneeAdapterFailedEventArgs>();
            kanban.AssigneeAdapterFailed += (_, args) => failures.Add(args);
            kanban.AssigneeAdapter = new FlowKanbanAssigneeAdapter((id, _) => id == "fails"
                ? Task.FromException<FlowKanbanAssignee?>(new InvalidOperationException("Lookup failed"))
                : Task.FromResult<FlowKanbanAssignee?>(new FlowKanbanAssignee("other", "Other")));

            await LoadAsync(kanban);
            failures.Clear();
            await kanban.RefreshAssigneesAsync();

            Assert.HasCount(2, failures);
            Assert.IsTrue(failures.All(failure => failure.Operation == FlowKanbanAssigneeAdapterOperation.ResolveAssignee));
            CollectionAssert.AreEquivalent(new[] { "fails", "mismatch" }, failures.Select(failure => failure.AssigneeId).ToArray());
            Assert.AreEqual("Kept", failing.Assignee);
            Assert.AreEqual("Kept too", mismatched.Assignee);
        }

        [TestMethod]
        public async Task When_AdapterReportsChange_BoardReloads()
        {
            var task = new FlowTask { Title = "Task", AssigneeId = "a", Assignee = "Alpha" };
            var kanban = CreateBoard(task);
            var adapter = new FlowKanbanAssigneeAdapter(new[] { new FlowKanbanAssignee("a", "Alpha") });
            kanban.AssigneeAdapter = adapter;
            await LoadAsync(kanban);

            adapter.SetAssignees(new[] { new FlowKanbanAssignee("a", "Alpha Renamed") });

            await WaitForAsync(() => task.Assignee == "Alpha Renamed");
            Assert.AreEqual("Alpha Renamed", task.Assignee);
        }

        [TestMethod]
        public async Task When_AdapterIsReplaced_OlderLoadDoesNotUpdateBoard()
        {
            var task = new FlowTask { Title = "Task", AssigneeId = "a", Assignee = "Saved" };
            var kanban = CreateBoard(task);
            await LoadAsync(kanban);

            var slowLoad = new TaskCompletionSource<IReadOnlyList<FlowKanbanAssignee>>();
            kanban.AssigneeAdapter = new FlowKanbanAssigneeAdapter(_ => slowLoad.Task);
            kanban.AssigneeAdapter = new FlowKanbanAssigneeAdapter(new[] { new FlowKanbanAssignee("a", "Current") });
            await kanban.RefreshAssigneesAsync();
            Assert.AreEqual("Current", task.Assignee);

            slowLoad.SetResult(new[] { new FlowKanbanAssignee("a", "Stale") });
            await Task.Delay(50);

            Assert.AreEqual("Current", task.Assignee);
        }

        [TestMethod]
        public void When_BoardJsonIsLoaded_AssigneeIdsAreKeptExactly()
        {
            var board = new FlowKanbanData
            {
                Columns =
                {
                    new FlowKanbanColumnData
                    {
                        Title = "Todo",
                        Tasks =
                        {
                            new FlowTask { Title = "Mixed case", AssigneeId = " Person-42 " },
                            new FlowTask { Title = "Control char", AssigneeId = "bad\u0001id" }
                        }
                    }
                }
            };
            var json = JsonSerializer.Serialize(board, FlowKanbanJsonContext.Default.FlowKanbanData);

            Assert.IsTrue(FlowKanbanBoardSanitizer.TryLoadFromJson(json, out var loaded));
            var tasks = loaded!.Columns.Single().Tasks;
            Assert.AreEqual(" Person-42 ", tasks[0].AssigneeId);
            Assert.IsNull(tasks[1].AssigneeId);
        }

        [TestMethod]
        public void When_SettingsAreSaved_TheyLoadFromTheSharedKey()
        {
            var first = new FlowKanban { ConfirmCardRemovals = false, EnableUndoRedo = true };
            Assert.IsTrue(first.TrySaveSettings(out var saveError), saveError?.Message);

            var second = new FlowKanban();
            Assert.IsTrue(second.TryLoadSettings(forceReload: true, out var loadError), loadError?.Message);

            Assert.IsFalse(second.ConfirmCardRemovals);
            Assert.IsTrue(second.EnableUndoRedo);

            first.ConfirmCardRemovals = true;
            first.EnableUndoRedo = false;
            Assert.IsTrue(first.TrySaveSettings(out _));
        }

        private static FlowKanban CreateBoard(params FlowTask[] tasks)
        {
            var column = new FlowKanbanColumnData { Title = "Todo" };
            foreach (var task in tasks)
            {
                column.Tasks.Add(task);
            }

            return new FlowKanban { Board = new FlowKanbanData { Columns = { column } } };
        }

        private static async Task LoadAsync(FlowKanban kanban)
        {
            RuntimeTestHelpers.AttachToHost(kanban);
            await RuntimeTestHelpers.EnsureLoadedAsync(kanban);
            kanban.CurrentView = FlowKanbanView.Board;
        }

        private static async Task WaitForAsync(Func<bool> condition)
        {
            for (var attempt = 0; attempt < 50 && !condition(); attempt++)
            {
                await Task.Delay(20);
            }
        }
    }
}
