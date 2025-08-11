using NxGraph.Graphs;

namespace NxGraph.Fsm
{
    /// <summary>
    /// A state machine that executes a graph of nodes with full lifecycle/observer support.
    /// </summary>
    public class StateMachine<TAgent>(Graph graph, IAsyncStateMachineObserver? observer = null)
        : StateMachine(graph, observer), IAgentSettable<TAgent>
    {
        public void SetAgent(TAgent agent) => Graph.SetAgent(agent);
    }

    /// <summary>
    /// Non-generic StateMachine.
    /// </summary>
    public class StateMachine : State
    {
        protected readonly IGraph Graph;
        private readonly IAsyncStateMachineObserver? _observer;

        private readonly object _lifecycleLock = new();

        private NodeId _current;
        private readonly NodeId _initial;

        private volatile ExecutionStatus _status = ExecutionStatus.Created;
        private bool _autoReset = true;
        private int _executeGate;

        /// <summary>Public execution status (volatile for visibility).</summary>
        public ExecutionStatus Status => _status;

        public StateMachine(IGraph graph, IAsyncStateMachineObserver? observer = null)
        {
            Graph = graph;
            _observer = observer;
            _initial = graph.StartNode.Id;
            _current = _initial;
        }

        /// <summary>Enable/disable auto-reset to Ready after a terminal state.</summary>
        public void SetAutoReset(bool enabled)
        {
            lock (_lifecycleLock) _autoReset = enabled;
        }

        /// <summary>
        /// Reset to the initial node. Disallowed while Running/Transitioning.
        /// Status moves: (any non-running) -> Resetting -> Ready.
        /// </summary>
        public async ValueTask<Result> Reset(CancellationToken ct = default)
        {
            bool notify;

            lock (_lifecycleLock)
            {
                switch (_status)
                {
                    case ExecutionStatus.Running or ExecutionStatus.Transitioning:
                        throw new InvalidOperationException("Cannot reset while running or transitioning.");
                    case ExecutionStatus.Created or ExecutionStatus.Ready:
                        return Result.Success;
                }

                _status = ExecutionStatus.Resetting;
                _current = _initial;
                notify = true;
            }

            if (notify && _observer is not null)
                await _observer.OnStateMachineReset(Graph.Id, ct).ConfigureAwait(false);

            lock (_lifecycleLock)
            {
                _status = ExecutionStatus.Ready;
            }

            return Result.Success;
        }

        protected override async ValueTask OnEnterAsync(CancellationToken ct)
        {
            if (Interlocked.Exchange(ref _executeGate, 1) == 1) // Prevent re-entrance
            {
                throw new InvalidOperationException("StateMachine is already executing.");
            }

            // First entry or re-entry after Ready: Starting -> (enter first node) -> Running
            await TransitionTo(ExecutionStatus.Starting);
            _current = _initial;
            if (_observer is not null)
            {
                await _observer.OnStateEntered(_current, ct).ConfigureAwait(false);
            }

            await TransitionTo(ExecutionStatus.Running);
        }

        protected override async ValueTask<Result> OnRunAsync(CancellationToken ct)
        {
            try
            {
                Result result = await InternalRunAsync(ct).ConfigureAwait(false);

                // If we get here, loop has returned a terminal result already.
                // Machine completion notification + optional auto-reset-to-Ready.
                await TransitionTo(result == Result.Success ? ExecutionStatus.Completed : ExecutionStatus.Failed);

                if (_autoReset) await Reset(ct);

                return result;
            }
            catch (OperationCanceledException)
            {
                await TransitionTo(ExecutionStatus.Cancelled);

                if (_observer is not null)
                    await _observer.OnStateMachineCancelled(Graph.Id, ct).ConfigureAwait(false);

                if (_autoReset) TransitionToReady();

                throw;
            }
            catch (Exception ex)
            {
                // Unexpected error outside node execution (node failures are returned as Failure below)
                if (_observer is not null)
                    await _observer.OnStateFailed(_current, ex, ct).ConfigureAwait(false);

                await TransitionTo(ExecutionStatus.Failed);

                if (_observer is not null)
                    await _observer.OnStateMachineCompleted(Graph.Id, Result.Failure, ct).ConfigureAwait(false);

                if (_autoReset) TransitionToReady();

                throw;
            }
        }

        protected override ValueTask OnExitAsync(CancellationToken ct)
        {
            Volatile.Write(ref _executeGate, 0);
            return ValueTask.CompletedTask;
        }

        private async ValueTask<Result> InternalRunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (!Graph.TryGetNode(_current, out Node? node))
                    throw new InvalidOperationException($"Node '{_current}' not found.");

                Result result = await node.Logic.ExecuteAsync(ct).ConfigureAwait(false);
                switch (result)
                {
                    case Result.Success:
                    {
                        if (_observer is not null)
                            await _observer.OnStateExited(_current, ct).ConfigureAwait(false);

                        NodeId next;

                        if (node.Logic is IDirector director)
                        {
                            next = director.SelectNext();
                            if (next.Equals(NodeId.Default))
                            {
                                return Result.Success;
                            }
                        }
                        else
                        {
                            if (!Graph.TryGetTransition(_current, out Transition edge))
                            {
                                if (_observer is not null)
                                {
                                    await _observer.OnStateFailed(
                                        _current,
                                        new InvalidOperationException($"No transition found for state '{_current}'."),
                                        ct
                                    ).ConfigureAwait(false);
                                }

                                return Result.Failure;
                            }

                            if (edge.IsEmpty)
                            {
                                return Result.Success; // terminal
                            }

                            next = edge.Destination;
                        }

                        await TransitionTo(ExecutionStatus.Transitioning);

                        if (_observer is not null)
                            await _observer.OnTransition(_current, next, ct).ConfigureAwait(false);

                        _current = next;

                        if (_observer is not null)
                            await _observer.OnStateEntered(_current, ct).ConfigureAwait(false);

                        await TransitionTo(ExecutionStatus.Running);

                        continue;
                    }
                    case Result.Failure:
                        return Result.Failure;
                    default:
                        throw new InvalidOperationException("Unknown node result.");
                }
            }

            ct.ThrowIfCancellationRequested();
            return Result.Failure;
        }

        /// <summary>
        /// Centralized status transition. Sets the status under lock and then
        /// kicks machine-lifecycle notifications outside the lock.
        /// </summary>
        private async Task TransitionTo(ExecutionStatus next)
        {
            ExecutionStatus prev;
            bool changed;

            lock (_lifecycleLock)
            {
                prev = _status;
                if (prev == next) return;
                _status = next;
                changed = true;
            }

            if (changed && _observer is not null)
            {
                await NotifyMachineStatusChangeAsync(prev, next).ConfigureAwait(false);
            }
        }

        private async ValueTask NotifyMachineStatusChangeAsync(ExecutionStatus prev, ExecutionStatus next)
        {
            if (_observer is null) return;

            NodeId graphId = Graph.Id;
            await _observer.StateMachineStatusChanged(graphId, prev, next).ConfigureAwait(false);
            switch (next)
            {
                case ExecutionStatus.Starting:
                    await _observer.OnStateMachineStarted(graphId).ConfigureAwait(false);
                    break;

                case ExecutionStatus.Completed:
                    await _observer.OnStateMachineCompleted(graphId, Result.Success).ConfigureAwait(false);
                    break;

                case ExecutionStatus.Failed:
                    await _observer.OnStateMachineCompleted(graphId, Result.Failure).ConfigureAwait(false);
                    break;

                case ExecutionStatus.Cancelled:
                    await _observer.OnStateMachineCancelled(graphId).ConfigureAwait(false);
                    break;

                case ExecutionStatus.Resetting:
                    await _observer.OnStateMachineReset(graphId).ConfigureAwait(false);
                    break;

                case ExecutionStatus.Ready:
                case ExecutionStatus.Created:
                case ExecutionStatus.Running:
                case ExecutionStatus.Transitioning:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(next), next, null);
            }
        }

        private void TransitionToReady()
        {
            lock (_lifecycleLock)
            {
                _current = _initial;
                _status = ExecutionStatus.Ready;
            }
        }
    }
}