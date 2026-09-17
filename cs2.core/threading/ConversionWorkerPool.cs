using System.Runtime.ExceptionServices;

namespace cs2.core.Threading {
    /// <summary>
    /// Runs indexed work items on a fixed set of dedicated threads, handing out indices through a shared cursor and surfacing the earliest failure; <see cref="Run"/> is not reentrant, so one pool instance serves exactly one run at a time and every consumer creates its own pool per phase.
    /// </summary>
    /// <remarks>
    /// <see cref="Run"/> keeps the state of the active run in the pool itself, so a second run started while one is in progress - from another thread or from a work item on this same pool - is rejected with an <see cref="InvalidOperationException"/> instead of corrupting the running one.
    /// Stopping after a failure is cooperative: up to <see cref="WorkerCount"/> minus one further items may still be started before their workers observe the stop flag, so a <see cref="Run"/> that throws means the phase is incomplete and bodies must tolerate a mid-phase abort.
    /// </remarks>
    public sealed class ConversionWorkerPool {
        /// <summary>
        /// Guards failure bookkeeping shared by every worker thread.
        /// </summary>
        readonly object FailureLock = new object();

        /// <summary>
        /// One while a run occupies the shared run state, zero while the pool is idle; claimed atomically to reject concurrent or reentrant runs.
        /// </summary>
        int RunInProgress;

        /// <summary>
        /// Next item index to hand out; advanced atomically by every worker.
        /// </summary>
        int NextItemIndex;

        /// <summary>
        /// Number of items in the active run.
        /// </summary>
        int ItemCount;

        /// <summary>
        /// Set once any worker fails so the others stop taking new items.
        /// </summary>
        bool StopRequested;

        /// <summary>
        /// Item index of the earliest failure observed in the active run.
        /// </summary>
        int FailedItemIndex;

        /// <summary>
        /// Exception raised by the earliest failing item.
        /// </summary>
        Exception Failure;

        /// <summary>
        /// Initializes a pool that runs at most the supplied number of threads per run.
        /// </summary>
        /// <param name="workerCount">Maximum threads per run; at least one.</param>
        public ConversionWorkerPool(int workerCount) {
            if (workerCount < 1) {
                throw new ArgumentOutOfRangeException(nameof(workerCount), workerCount, "A worker pool needs at least one worker.");
            }

            WorkerCount = workerCount;
        }

        /// <summary>
        /// Gets the maximum number of threads used by one run.
        /// </summary>
        public int WorkerCount { get; }

        /// <summary>
        /// Runs the body once per item index on up to <see cref="WorkerCount"/> threads and blocks until every thread that was started has finished, then rethrows the failure of the lowest failing item.
        /// </summary>
        /// <param name="itemCount">Number of items; the body receives indices <c>0..itemCount-1</c>.</param>
        /// <param name="body">Work for one item, receiving the owning worker index and the item index.</param>
        /// <exception cref="ArgumentOutOfRangeException">The item count is negative.</exception>
        /// <exception cref="ArgumentNullException">The body is null.</exception>
        /// <exception cref="InvalidOperationException">A run is already in progress on this pool, because runs are not reentrant.</exception>
        /// <remarks>
        /// A run that throws leaves the phase incomplete: the stop after a failure is cooperative, so up to <see cref="WorkerCount"/> minus one items beyond the failing one may already have started, and every remaining item is skipped.
        /// </remarks>
        public void Run(int itemCount, Action<int, int> body) {
            if (itemCount < 0) {
                throw new ArgumentOutOfRangeException(nameof(itemCount), itemCount, "Item count must not be negative.");
            }
            if (body == null) {
                throw new ArgumentNullException(nameof(body));
            }
            if (itemCount == 0) {
                return;
            }
            if (Interlocked.CompareExchange(ref RunInProgress, 1, 0) != 0) {
                throw new InvalidOperationException("ConversionWorkerPool.Run is not reentrant; a run is already in progress on this pool.");
            }

            Exception failure = null;
            try {
                NextItemIndex = -1;
                ItemCount = itemCount;
                StopRequested = false;
                FailedItemIndex = int.MaxValue;
                Failure = null;

                int threadCount = Math.Min(WorkerCount, itemCount);
                Thread[] threads = new Thread[threadCount];
                for (int workerIndex = 0; workerIndex < threadCount; workerIndex++) {
                    int ownedWorkerIndex = workerIndex;
                    threads[workerIndex] = new Thread(() => RunWorker(ownedWorkerIndex, body)) {
                        Name = $"cs2-worker-{ownedWorkerIndex}",
                        IsBackground = true
                    };
                }

                int startedCount = 0;
                try {
                    for (int workerIndex = 0; workerIndex < threadCount; workerIndex++) {
                        threads[workerIndex].Start();
                        startedCount++;
                    }
                } finally {
                    if (startedCount < threadCount) {
                        Volatile.Write(ref StopRequested, true);
                    }

                    for (int workerIndex = 0; workerIndex < startedCount; workerIndex++) {
                        threads[workerIndex].Join();
                    }
                }

                failure = Failure;
            } finally {
                Volatile.Write(ref RunInProgress, 0);
            }

            if (failure != null) {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        /// <summary>
        /// Pulls item indices until the cursor is exhausted or a failure requests a stop.
        /// </summary>
        /// <param name="workerIndex">Index owned by this thread for the whole run.</param>
        /// <param name="body">Work for one item.</param>
        void RunWorker(int workerIndex, Action<int, int> body) {
            while (!Volatile.Read(ref StopRequested)) {
                int itemIndex = Interlocked.Increment(ref NextItemIndex);
                if (itemIndex >= ItemCount) {
                    return;
                }

                try {
                    body(workerIndex, itemIndex);
                } catch (Exception exception) {
                    RecordFailure(itemIndex, exception);
                }
            }
        }

        /// <summary>
        /// Keeps the failure with the lowest item index and asks every worker to stop.
        /// </summary>
        /// <param name="itemIndex">Item whose body threw.</param>
        /// <param name="exception">Exception thrown by the body.</param>
        void RecordFailure(int itemIndex, Exception exception) {
            lock (FailureLock) {
                if (itemIndex < FailedItemIndex) {
                    FailedItemIndex = itemIndex;
                    Failure = exception;
                }

                Volatile.Write(ref StopRequested, true);
            }
        }
    }
}
