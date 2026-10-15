using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Flowery.Uno.Kanban.Controls
{
    public partial class FlowKanban
    {
        private IFlowKanbanAssigneeAdapter? _trackedAssigneeAdapter;
        private CancellationTokenSource? _assigneeRefreshCancellation;
        private long _assigneeRefreshVersion;

        public static readonly DependencyProperty AssigneeAdapterProperty =
            DependencyProperty.Register(
                nameof(AssigneeAdapter),
                typeof(IFlowKanbanAssigneeAdapter),
                typeof(FlowKanban),
                new PropertyMetadata(null, OnAssigneeAdapterChanged));

        /// <summary>
        /// Adapter used to map host-owned assignee IDs to display names, avatars, and roles.
        /// </summary>
        public IFlowKanbanAssigneeAdapter? AssigneeAdapter
        {
            get => (IFlowKanbanAssigneeAdapter?)GetValue(AssigneeAdapterProperty);
            set => SetValue(AssigneeAdapterProperty, value);
        }

        /// <summary>
        /// Raised when an assignee adapter callback fails.
        /// </summary>
        public event EventHandler<FlowKanbanAssigneeAdapterFailedEventArgs>? AssigneeAdapterFailed;

        private static void OnAssigneeAdapterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FlowKanban kanban)
                return;

            kanban.CancelAssigneeRefresh();
            kanban.DetachAssigneeAdapter();
            kanban.UpdateAssigneeFilterOptions(Array.Empty<FlowTaskAssigneeOption>());
            if (!kanban.IsLoaded)
                return;

            kanban.AttachAssigneeAdapter(e.NewValue as IFlowKanbanAssigneeAdapter);
            _ = ObserveAssigneeRefreshAsync(kanban.RefreshAssigneesAsync());
        }

        private void AttachAssigneeAdapter(IFlowKanbanAssigneeAdapter? adapter)
        {
            if (ReferenceEquals(_trackedAssigneeAdapter, adapter))
                return;

            DetachAssigneeAdapter();
            if (adapter is null)
                return;

            _trackedAssigneeAdapter = adapter;
            _trackedAssigneeAdapter.AssigneesChanged += OnAssigneesChanged;
        }

        private void DetachAssigneeAdapter()
        {
            if (_trackedAssigneeAdapter is null)
                return;

            _trackedAssigneeAdapter.AssigneesChanged -= OnAssigneesChanged;
            _trackedAssigneeAdapter = null;
        }

        private void OnAssigneesChanged(object? sender, EventArgs e)
        {
            var adapter = _trackedAssigneeAdapter;
            if (adapter is null)
                return;

            if (DispatcherQueue is { HasThreadAccess: false } queue)
            {
                queue.TryEnqueue(() => RefreshForChangedAdapter(adapter));
                return;
            }

            RefreshForChangedAdapter(adapter);
        }

        private void RefreshForChangedAdapter(IFlowKanbanAssigneeAdapter adapter)
        {
            if (!IsLoaded || !ReferenceEquals(_trackedAssigneeAdapter, adapter))
                return;

            _ = ObserveAssigneeRefreshAsync(RefreshAssigneesAsync());
        }

        /// <summary>
        /// Reloads assignee data from <see cref="AssigneeAdapter"/>.
        /// Superseded requests and requests canceled by unloading the control do not update the board.
        /// </summary>
        public async Task RefreshAssigneesAsync(CancellationToken cancellationToken = default)
        {
            if (DispatcherQueue is { HasThreadAccess: false } queue)
            {
                await InvokeOnUiThreadAsync(queue, () => RefreshAssigneesAsync(cancellationToken));
                return;
            }

            var adapter = AssigneeAdapter;
            var version = Interlocked.Increment(ref _assigneeRefreshVersion);
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var previousCancellation = Interlocked.Exchange(ref _assigneeRefreshCancellation, cancellation);
            previousCancellation?.Cancel();

            try
            {
                if (adapter is null)
                {
                    ApplyResolvedAssignees(Array.Empty<FlowKanbanAssignee>());
                    return;
                }

                // The awaits below resume on the UI thread, so board data is only changed there.
                IReadOnlyList<FlowKanbanAssignee> suppliedAssignees;
                try
                {
                    suppliedAssignees = await adapter.GetAssigneesAsync(cancellation.Token);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    ReportAssigneeAdapterFailure(adapter, version, FlowKanbanAssigneeAdapterOperation.LoadAssignees, ex);
                    return;
                }

                cancellation.Token.ThrowIfCancellationRequested();
                if (!IsCurrentAssigneeRefresh(version))
                    return;

                var resolvedAssignees = NormalizeResolvedAssignees(suppliedAssignees);
                foreach (var assigneeId in CollectUnresolvedAssigneeIds(resolvedAssignees))
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (!IsCurrentAssigneeRefresh(version))
                        return;

                    FlowKanbanAssignee? resolvedAssignee;
                    try
                    {
                        resolvedAssignee = await adapter.ResolveAssigneeAsync(assigneeId, cancellation.Token);
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ReportAssigneeAdapterFailure(adapter, version, FlowKanbanAssigneeAdapterOperation.ResolveAssignee, ex, assigneeId);
                        continue;
                    }

                    if (resolvedAssignee is not null
                        && !string.Equals(resolvedAssignee.Id, assigneeId, StringComparison.Ordinal))
                    {
                        ReportAssigneeAdapterFailure(
                            adapter,
                            version,
                            FlowKanbanAssigneeAdapterOperation.ResolveAssignee,
                            new InvalidOperationException(
                                $"The assignee callback returned ID '{resolvedAssignee.Id}' for requested ID '{assigneeId}'."),
                            assigneeId);
                        continue;
                    }

                    if (resolvedAssignee is not null)
                    {
                        resolvedAssignees.Add(resolvedAssignee);
                    }
                }

                cancellation.Token.ThrowIfCancellationRequested();
                if (!IsCurrentAssigneeRefresh(version) || !ReferenceEquals(adapter, AssigneeAdapter))
                    return;

                ApplyResolvedAssignees(resolvedAssignees);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;
            }
            finally
            {
                Interlocked.CompareExchange(ref _assigneeRefreshCancellation, null, cancellation);
                cancellation.Dispose();
            }
        }

        private static Task InvokeOnUiThreadAsync(DispatcherQueue queue, Func<Task> action)
        {
            var completion = new TaskCompletionSource();
            var queued = queue.TryEnqueue(async () =>
            {
                try
                {
                    await action();
                    completion.TrySetResult();
                }
                catch (OperationCanceledException ex)
                {
                    completion.TrySetCanceled(ex.CancellationToken);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });

            if (!queued)
            {
                completion.TrySetException(new InvalidOperationException("The UI thread dispatcher is not available."));
            }

            return completion.Task;
        }

        private static List<FlowKanbanAssignee> NormalizeResolvedAssignees(
            IEnumerable<FlowKanbanAssignee>? assignees)
        {
            var normalized = new List<FlowKanbanAssignee>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var assignee in assignees ?? Array.Empty<FlowKanbanAssignee>())
            {
                if (assignee is null)
                    continue;

                if (!seenIds.Add(assignee.Id))
                    continue;

                normalized.Add(new FlowKanbanAssignee(
                    assignee.Id,
                    assignee.DisplayName,
                    assignee.AvatarSource,
                    assignee.Roles));
            }

            return normalized;
        }

        private string[] CollectUnresolvedAssigneeIds(IReadOnlyList<FlowKanbanAssignee> resolvedAssignees)
        {
            if (Board?.Columns == null)
                return Array.Empty<string>();

            var resolvedIds = resolvedAssignees
                .Select(assignee => assignee.Id)
                .ToHashSet(StringComparer.Ordinal);
            var unresolvedIds = new List<string>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var column in Board.Columns)
            {
                if (column?.Tasks == null)
                    continue;

                foreach (var task in column.Tasks)
                {
                    var assigneeId = task?.AssigneeId;
                    if (string.IsNullOrWhiteSpace(assigneeId)
                        || resolvedIds.Contains(assigneeId)
                        || !seenIds.Add(assigneeId))
                    {
                        continue;
                    }

                    unresolvedIds.Add(assigneeId);
                }
            }

            return unresolvedIds.ToArray();
        }

        private void ApplyResolvedAssignees(IReadOnlyList<FlowKanbanAssignee> resolvedAssignees)
        {
            var assigneesById = resolvedAssignees
                .ToDictionary(assignee => assignee.Id, StringComparer.Ordinal);

            if (Board?.Columns != null)
            {
                foreach (var column in Board.Columns)
                {
                    if (column?.Tasks == null)
                        continue;

                    foreach (var task in column.Tasks)
                    {
                        if (task == null)
                            continue;

                        var assigneeId = task.AssigneeId;
                        if (!string.IsNullOrWhiteSpace(assigneeId)
                            && assigneesById.TryGetValue(assigneeId, out var assignee))
                        {
                            if (!string.Equals(task.Assignee, assignee.DisplayName, StringComparison.Ordinal))
                                task.Assignee = assignee.DisplayName;
                            task.AssigneeAvatarSource = assignee.AvatarSource;
                            task.AssigneeRoles = assignee.Roles;
                        }
                        else
                        {
                            task.AssigneeAvatarSource = null;
                            task.AssigneeRoles = Array.Empty<string>();
                        }
                    }
                }
            }

            var filterOptions = resolvedAssignees
                .Select(assignee => new FlowTaskAssigneeOption(assignee.Id, assignee.DisplayName))
                .ToArray();
            UpdateAssigneeFilterOptions(filterOptions);
        }

        private void ReportAssigneeAdapterFailure(
            IFlowKanbanAssigneeAdapter adapter,
            long version,
            FlowKanbanAssigneeAdapterOperation operation,
            Exception exception,
            string? assigneeId = null)
        {
            if (!IsCurrentAssigneeRefresh(version) || !ReferenceEquals(adapter, AssigneeAdapter))
                return;

            AssigneeAdapterFailed?.Invoke(
                this,
                new FlowKanbanAssigneeAdapterFailedEventArgs(operation, exception, assigneeId));
        }

        private bool IsCurrentAssigneeRefresh(long version)
        {
            return version == Volatile.Read(ref _assigneeRefreshVersion);
        }

        private void CancelAssigneeRefresh()
        {
            Interlocked.Increment(ref _assigneeRefreshVersion);
            Interlocked.Exchange(ref _assigneeRefreshCancellation, null)?.Cancel();
        }

        private static async Task ObserveAssigneeRefreshAsync(Task refreshTask)
        {
            try
            {
                await refreshTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // Adapter failures are reported through AssigneeAdapterFailed.
            }
        }
    }
}
