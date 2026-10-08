const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
/** Loads the maintained TypeScript runtime offline without copying it into generated output. */
require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), {
    compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS }
}).outputText, file);
const { Task } = require(path.join(root, 'system/threading/tasks/task.ts'));
const { TaskCompletionSource } = require(path.join(root, 'system/threading/tasks/task-completion-source.ts'));
const { TaskCreationOptions } = require(path.join(root, 'system/threading/tasks/task-creation-options.ts'));
const { CancellationToken } = require(path.join(root, 'system/threading/cancellation-token.ts'));
const { CancellationTokenSource } = require(path.join(root, 'system/threading/cancellation-token-source.ts'));
const { OperationCanceledException } = require(path.join(root, 'system/operation-canceled.exception.ts'));
const { TaskCanceledException } = require(path.join(root, 'system/threading/tasks/task-canceled.exception.ts'));
const { AggregateException } = require(path.join(root, 'system/aggregate.exception.ts'));
const { TimeSpan } = require(path.join(root, 'system/time-span.ts'));
const { TaskRuntimeClock } = require('./task-runtime-clock.cjs');

test('completion wins once and continuations run asynchronously', async () => {
    const source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    const value = { piece: 'one' };
    let observed = false;
    const result = source.Task.then(result => { observed = true; return result; });
    assert.equal(source.TrySetResult(value), true);
    assert.equal(observed, false);
    assert.equal(source.TrySetResult({ piece: 'two' }), false);
    assert.equal(source.TrySetException(new Error('late')), false);
    assert.equal(source.TrySetCanceled(), false);
    assert.throws(() => source.SetResult(value), { name: 'InvalidOperationException' });
    assert.strictEqual(await result, value);
});

test('fault and cancellation retain their identities and reject later completion', async () => {
    const source = new TaskCompletionSource();
    const failure = new Error('reader failed');
    const observed = assert.rejects(source.Task, error => error === failure);
    assert.equal(source.TrySetException(failure), true);
    assert.equal(source.TrySetResult('late'), false);
    await observed;
    const canceled = new TaskCompletionSource();
    const token = new CancellationToken();
    const rejected = assert.rejects(canceled.Task, error => error instanceof TaskCanceledException && error instanceof OperationCanceledException && error.CancellationToken === token);
    assert.equal(canceled.TrySetCanceled(token), true);
    assert.equal(canceled.TrySetException(failure), false);
    await rejected;
    assert.throws(() => new TaskCompletionSource(4294967296), { name: 'ArgumentOutOfRangeException' });
    assert.throws(() => new TaskCompletionSource(TaskCreationOptions.AttachedToParent), { name: 'NotSupportedException' });
    assert.throws(() => new TaskCompletionSource().TrySetException(null), { name: 'ArgumentNullException' });
});

test('cancellation is synchronous, LIFO, unregisterable and idempotent', () => {
    const source = new CancellationTokenSource();
    const token = source.Token;
    const calls = [];
    token.Register(() => { assert.equal(token.IsCancellationRequested, true); calls.push('first'); });
    const removed = token.Register(() => calls.push('removed'));
    removed.Dispose();
    removed.Dispose();
    token.Register(state => calls.push(state), 'last');
    source.Cancel();
    source.Cancel();
    assert.deepEqual(calls, ['last', 'first']);
    token.Register(() => calls.push('immediate'));
    assert.deepEqual(calls, ['last', 'first', 'immediate']);
    assert.throws(() => token.ThrowIfCancellationRequested(), error => error.CancellationToken === token);
    assert.equal(CancellationToken.None.CanBeCanceled, false);
    assert.equal(CancellationToken.None.IsCancellationRequested, false);
    CancellationToken.None.Register(() => assert.fail('None invoked a callback')).Dispose();
});

test('callback failures aggregate without skipping other registrations', () => {
    const source = new CancellationTokenSource();
    const first = new Error('first');
    const second = new Error('second');
    let ran = false;
    source.Token.Register(() => { ran = true; throw first; });
    source.Token.Register(() => { throw second; });
    assert.throws(() => source.Cancel(), error => error instanceof AggregateException && error.InnerExceptions[0] === second && error.InnerExceptions[1] === first);
    assert.equal(ran, true);
    const immediate = new CancellationTokenSource();
    immediate.Token.Register(() => assert.fail('Should not run after first failure'));
    immediate.Token.Register(() => { throw first; });
    assert.throws(() => immediate.Cancel(true), error => error === first);
});

test('disposing a source releases resources without canceling previously issued tokens', () => {
    const clock = new TaskRuntimeClock();
    try {
        const source = new CancellationTokenSource(100);
        const token = source.Token;
        token.Register(() => assert.fail('Dispose canceled the token'));
        assert.equal(clock.timers.size, 1);
        source.Dispose();
        source.Dispose();
        assert.equal(clock.timers.size, 0);
        assert.equal(token.IsCancellationRequested, false);
        assert.equal(token.CanBeCanceled, true);
        assert.throws(() => source.Token, { name: 'ObjectDisposedException' });
        assert.throws(() => source.Cancel(), { name: 'ObjectDisposedException' });
        assert.throws(() => source.CancelAfter(1), { name: 'ObjectDisposedException' });
    } finally { clock.dispose(); }
});

test('linked sources cancel from either parent and detach on disposal', () => {
    const first = new CancellationTokenSource();
    const second = new CancellationTokenSource();
    const linked = CancellationTokenSource.CreateLinkedTokenSource(first.Token, second.Token);
    const token = linked.Token;
    second.Cancel();
    assert.equal(token.IsCancellationRequested, true);
    linked.Dispose();
    first.Cancel();
    const parent = new CancellationTokenSource();
    const detached = CancellationTokenSource.CreateLinkedTokenSource([parent.Token]);
    const detachedToken = detached.Token;
    detached.Dispose();
    parent.Cancel();
    assert.equal(detachedToken.IsCancellationRequested, false);
    const immediate = CancellationTokenSource.CreateLinkedTokenSource(new CancellationToken(true));
    assert.equal(immediate.Token.IsCancellationRequested, true);
    immediate.Dispose();
});

test('Delay cancels promptly and releases its timer and registration', async () => {
    const clock = new TaskRuntimeClock();
    try {
        const source = new CancellationTokenSource();
        const token = source.Token;
        const pending = Task.Delay(TimeSpan.fromSeconds(5), token);
        const observed = assert.rejects(pending, error => error instanceof OperationCanceledException && error.CancellationToken === token);
        assert.equal(clock.timers.size, 1);
        source.Cancel();
        await observed;
        assert.equal(clock.timers.size, 0);
        const completedSource = new CancellationTokenSource();
        const complete = Task.Delay(10, completedSource.Token);
        clock.advance(10);
        await complete;
        completedSource.Cancel();
        assert.equal(clock.timers.size, 0);
        const canceled = new CancellationToken(true);
        await assert.rejects(Task.Delay(0, canceled), error => error.CancellationToken === canceled);
        assert.equal(clock.timers.size, 0);
        assert.throws(() => Task.Delay(-2), { name: 'ArgumentOutOfRangeException' });
        assert.throws(() => Task.Delay(0.5), { name: 'ArgumentOutOfRangeException' });
    } finally { clock.dispose(); }
});

test('infinite Delay has no timer and can still be canceled', async () => {
    const clock = new TaskRuntimeClock();
    try {
        const source = new CancellationTokenSource();
        const pending = Task.Delay(-1, source.Token);
        const rejected = assert.rejects(pending, { name: 'TaskCanceledException' });
        assert.equal(clock.timers.size, 0);
        clock.advance(10000);
        source.Cancel();
        await rejected;
    } finally { clock.dispose(); }
});

test('CancelAfter replaces or disables its deadline and long TimeSpans do not overflow', async () => {
    const clock = new TaskRuntimeClock();
    try {
        const source = new CancellationTokenSource(10);
        source.CancelAfter(20);
        clock.advance(10);
        assert.equal(source.IsCancellationRequested, false);
        source.CancelAfter(-1);
        clock.advance(100);
        assert.equal(source.IsCancellationRequested, false);
        source.CancelAfter(2);
        clock.advance(2);
        assert.equal(source.IsCancellationRequested, true);
        assert.equal(clock.timers.size, 0);
        const long = Task.Delay(TimeSpan.fromMilliseconds(4294967294));
        let complete = false;
        long.then(() => complete = true);
        clock.advance(2147483647);
        await Promise.resolve();
        assert.equal(complete, false);
        assert.equal(clock.timers.size, 1);
        clock.advance(2147483647);
        await long;
        assert.equal(clock.timers.size, 0);
    } finally { clock.dispose(); }
});

test('WhenAll waits for every task before publishing a fault', async () => {
    const failed = new TaskCompletionSource();
    const pending = new TaskCompletionSource();
    const failure = new Error('piece failed');
    const combined = Task.WhenAll([failed.Task, pending.Task]);
    let done = false;
    const observed = combined.then(() => assert.fail('Expected failure'), error => { done = true; assert.strictEqual(error, failure); });
    failed.SetException(failure);
    await Promise.resolve();
    await Promise.resolve();
    assert.equal(done, false);
    pending.SetResult('other piece');
    await observed;
});

test('WhenAll preserves input order and faults take precedence over cancellation', async () => {
    const first = new TaskCompletionSource();
    const second = new TaskCompletionSource();
    const ordered = Task.WhenAll([first.Task, second.Task]);
    second.SetResult(2);
    first.SetResult(1);
    assert.deepEqual(await ordered, [1, 2]);
    const token = new CancellationToken(true);
    const fault = new OperationCanceledException(CancellationToken.None, 'Fault, not a canceled task');
    await assert.rejects(Task.WhenAll([Task.FromCanceled(token), Task.FromException(fault)]), error => error === fault);
    await assert.rejects(Task.WhenAll([Task.FromCanceled(token), Task.FromResult(1)]), error => error.CancellationToken === token);
    assert.deepEqual(await Task.WhenAll([]), []);
    assert.throws(() => Task.WhenAll([null]), { name: 'ArgumentException' });
    assert.throws(() => Task.FromCanceled(CancellationToken.None), { name: 'ArgumentOutOfRangeException' });
});
