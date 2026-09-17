using cs2.core.Threading;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies the fixed worker pool that drives every parallel converter phase.
/// </summary>
public sealed class ConversionWorkerPoolTests {
    /// <summary>
    /// Every item index is handed out exactly once and worker indices stay within the pool size.
    /// </summary>
    [Fact]
    public void Run_ProcessesEveryItemExactlyOnce() {
        int[] hits = new int[1000];
        int[] workerHits = new int[4];
        ConversionWorkerPool pool = new ConversionWorkerPool(4);

        pool.Run(hits.Length, (workerIndex, itemIndex) => {
            Interlocked.Increment(ref hits[itemIndex]);
            Interlocked.Increment(ref workerHits[workerIndex]);
        });

        Assert.All(hits, hit => Assert.Equal(1, hit));
        Assert.Equal(hits.Length, workerHits.Sum());
    }

    /// <summary>
    /// A worker index is never shared by two threads at the same time.
    /// </summary>
    [Fact]
    public void Run_OwnsEachWorkerIndexByOneThread() {
        int[] ownerThread = new int[3];
        int violations = 0;
        ConversionWorkerPool pool = new ConversionWorkerPool(3);

        pool.Run(300, (workerIndex, itemIndex) => {
            int current = Environment.CurrentManagedThreadId;
            int previous = Interlocked.CompareExchange(ref ownerThread[workerIndex], current, 0);
            if (previous != 0 && previous != current) {
                Interlocked.Increment(ref violations);
            }
        });

        Assert.Equal(0, violations);
    }

    /// <summary>
    /// The failure with the lowest item index is rethrown after every thread has stopped.
    /// </summary>
    [Fact]
    public void Run_RethrowsLowestFailingItem() {
        ConversionWorkerPool pool = new ConversionWorkerPool(4);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => pool.Run(64, (workerIndex, itemIndex) => {
            if (itemIndex == 40 || itemIndex == 7) {
                throw new InvalidOperationException($"item {itemIndex}");
            }

            Thread.SpinWait(2000);
        }));

        Assert.Equal("item 7", failure.Message);
    }

    /// <summary>
    /// After a failure no new items are started.
    /// </summary>
    [Fact]
    public void Run_StopsHandingOutItemsAfterFailure() {
        int started = 0;
        ConversionWorkerPool pool = new ConversionWorkerPool(1);

        Assert.Throws<InvalidOperationException>(() => pool.Run(100, (workerIndex, itemIndex) => {
            Interlocked.Increment(ref started);
            throw new InvalidOperationException("stop");
        }));

        Assert.Equal(1, started);
    }

    /// <summary>
    /// A single worker runs items strictly in index order on one thread.
    /// </summary>
    [Fact]
    public void Run_WithOneWorker_RunsInOrder() {
        List<int> order = new List<int>();
        ConversionWorkerPool pool = new ConversionWorkerPool(1);

        pool.Run(10, (workerIndex, itemIndex) => order.Add(itemIndex));

        Assert.Equal(Enumerable.Range(0, 10), order);
    }

    /// <summary>
    /// Invalid sizes and bodies are rejected up front, and zero items is a no-op.
    /// </summary>
    [Fact]
    public void Run_ValidatesArguments() {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConversionWorkerPool(0));
        ConversionWorkerPool pool = new ConversionWorkerPool(2);
        Assert.Throws<ArgumentNullException>(() => pool.Run(1, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => pool.Run(-1, (workerIndex, itemIndex) => { }));
        pool.Run(0, (workerIndex, itemIndex) => throw new InvalidOperationException("never"));
    }
}
