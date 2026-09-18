using cs2.core.Threading;
using System.Globalization;

namespace cs2.cpp.tests;

/// <summary>
/// Verifies the single-thread background work wrapper used to overlap converter startup phases.
/// </summary>
public sealed class BackgroundWorkTests {
    /// <summary>
    /// The body runs on a differently named thread and Wait returns after it completes.
    /// </summary>
    [Fact]
    public void Wait_RunsBodyOnNamedThreadAndCompletes() {
        string observedThreadName = null;
        int callerThreadId = Environment.CurrentManagedThreadId;
        int workerThreadId = 0;

        BackgroundWork work = new BackgroundWork("cs2-test-work", () => {
            observedThreadName = Thread.CurrentThread.Name;
            workerThreadId = Environment.CurrentManagedThreadId;
        });
        work.Wait();

        Assert.True(work.IsCompleted);
        Assert.Equal("cs2-test-work", observedThreadName);
        Assert.NotEqual(callerThreadId, workerThreadId);
    }

    /// <summary>
    /// An exception thrown by the body surfaces from Wait with the original type and message, every time Wait is called.
    /// </summary>
    [Fact]
    public void Wait_RethrowsBodyFailure() {
        BackgroundWork work = new BackgroundWork("cs2-test-failure", () => throw new InvalidOperationException("boom"));

        InvalidOperationException first = Assert.Throws<InvalidOperationException>(() => work.Wait());
        InvalidOperationException second = Assert.Throws<InvalidOperationException>(() => work.Wait());

        Assert.Equal("boom", first.Message);
        Assert.Equal("boom", second.Message);
    }

    /// <summary>
    /// Join waits without surfacing the failure so callers can clean up before rethrowing.
    /// </summary>
    [Fact]
    public void Join_DoesNotThrow() {
        BackgroundWork work = new BackgroundWork("cs2-test-join", () => throw new InvalidOperationException("boom"));

        work.Join();

        Assert.True(work.IsCompleted);
    }

    /// <summary>
    /// The worker thread adopts the creating thread's culture, so culture-sensitive formatting inside the body never falls back to the process-wide default thread culture.
    /// </summary>
    [Fact]
    public void Wait_CopiesCreatingThreadCultureToWorker() {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        string observedCultureName = null;
        string observedUiCultureName = null;
        try {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("de-DE");
            BackgroundWork work = new BackgroundWork("cs2-test-culture", () => {
                observedCultureName = Thread.CurrentThread.CurrentCulture.Name;
                observedUiCultureName = Thread.CurrentThread.CurrentUICulture.Name;
            });
            work.Wait();
        } finally {
            Thread.CurrentThread.CurrentCulture = originalCulture;
            Thread.CurrentThread.CurrentUICulture = originalUiCulture;
        }

        Assert.Equal("de-DE", observedCultureName);
        Assert.Equal("de-DE", observedUiCultureName);
    }

    /// <summary>
    /// Construction rejects missing names and bodies instead of starting a broken thread.
    /// </summary>
    [Fact]
    public void Constructor_RejectsInvalidArguments() {
        Assert.Throws<ArgumentException>(() => new BackgroundWork("", () => { }));
        Assert.Throws<ArgumentNullException>(() => new BackgroundWork("cs2-test-null", null));
    }
}
