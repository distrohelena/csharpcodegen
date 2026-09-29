const assert = require('node:assert/strict');
const { test } = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
const previousLoader = require.extensions['.ts'];
require.extensions['.ts'] = (module, filename) => {
    module._compile(ts.transpileModule(fs.readFileSync(filename, 'utf8'), { compilerOptions: {
        module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022,
    } }).outputText, filename);
};
const { Queue } = require(path.join(root, 'system/collections/generic/queue.ts'));
const { TimeSpan } = require(path.join(root, 'system/time-span.ts'));
const { InvalidOperationException } = require(path.join(root, 'system/invalid-operation.exception.ts'));
const { ArgumentNullException } = require(path.join(root, 'system/argument-null.exception.ts'));
const { ArgumentOutOfRangeException } = require(path.join(root, 'system/argument-out-of-range.exception.ts'));
if (previousLoader) require.extensions['.ts'] = previousLoader;
else delete require.extensions['.ts'];

/** Loads the actual stopwatch source with a controllable monotonic clock. */
function stopwatchFixture() {
    let now = 0;
    const exports = {};
    const source = fs.readFileSync(path.join(root, 'system/diagnostics/stopwatch.ts'), 'utf8');
    const output = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText;
    vm.runInNewContext(output, { exports, performance: { now: () => now }, require: () => ({ TimeSpan }) });
    return { Stopwatch: exports.Stopwatch, advance: milliseconds => { now += milliseconds; } };
}

test('retains FIFO order through wrapped storage, growth and repeated drains', () => {
    const queue = new Queue(3);
    for (let round = 0; round < 30; round++) {
        for (let i = 0; i < 5; i++) queue.Enqueue(round * 10 + i);
        assert.equal(queue.Dequeue(), round * 10);
        assert.equal(queue.Dequeue(), round * 10 + 1);
        queue.Enqueue(round * 10 + 5);
        assert.deepEqual(queue.ToArray(), [2, 3, 4, 5].map(i => round * 10 + i));
        for (let i = 2; i < 6; i++) {
            assert.equal(queue.Peek(), round * 10 + i);
            assert.equal(queue.Dequeue(), round * 10 + i);
        }
        assert.equal(queue.Count, 0);
    }
});

test('preserves null, undefined, duplicates and identity without exposing storage', () => {
    const item = { id: 1 };
    const queue = new Queue([null, undefined, item, item, NaN]);
    const copy = queue.ToArray(); copy.length = 0;
    assert.equal(queue.Count, 5);
    assert.equal(queue.Contains(item), true);
    assert.equal(queue.Contains({ id: 1 }), false);
    assert.equal(queue.Contains(NaN), true);
    assert.equal(queue.Dequeue(), null);
    assert.equal(queue.Dequeue(), undefined);
    assert.deepEqual([...queue], [item, item, NaN]);
});

test('rejects empty reads and invalid constructors with native exception identities', () => {
    const queue = new Queue();
    for (const operation of ['Peek', 'Dequeue']) {
        assert.throws(() => queue[operation](), error => error instanceof InvalidOperationException && error.message === 'Queue empty.');
    }
    for (const value of [-1, 1.5, NaN, Infinity, 2147483648]) assert.throws(() => new Queue(value), ArgumentOutOfRangeException);
    for (const value of [null, undefined]) assert.throws(() => new Queue(value), ArgumentNullException);
});

test('clear releases entries and queue remains reusable', () => {
    const queue = new Queue([1, 2, 3]);
    queue.Dequeue(); queue.Clear();
    assert.equal(queue.Count, 0);
    assert.deepEqual(queue.ToArray(), []);
    assert.equal(queue.Contains(2), false);
    queue.Enqueue(4); assert.equal(queue.Dequeue(), 4);
});

for (const operation of ['Enqueue', 'Dequeue', 'Clear']) {
    test('invalidates iterators before and during enumeration after ' + operation, () => {
        const queue = new Queue([1, 2]);
        const before = queue[Symbol.iterator]();
        const during = queue[Symbol.iterator]();
        assert.equal(during.next().value, 1);
        queue[operation](3);
        assert.throws(() => before.next(), InvalidOperationException);
        assert.throws(() => during.next(), InvalidOperationException);
    });
}

test('peek and array snapshots do not invalidate an iterator', () => {
    const queue = new Queue([1, 2]); const iterator = queue[Symbol.iterator]();
    assert.equal(queue.Peek(), 1); queue.ToArray();
    assert.deepEqual(Array.from(iterator), [1, 2]);
});

test('new, repeated stop and reset remain stopped at zero', () => {
    const { Stopwatch, advance } = stopwatchFixture(); const watch = new Stopwatch();
    advance(100); watch.stop(); watch.stop();
    assert.equal(watch.ElapsedMilliseconds, 0); assert.equal(watch.isRunning, false);
    watch.start(); advance(5); watch.reset(); advance(50);
    assert.equal(watch.elapsedTicks, 0); assert.equal(watch.elapsed.TotalMilliseconds, 0);
    assert.equal(watch.isRunning, false);
});

test('start is idempotent and elapsed reads do not discard time', () => {
    const { Stopwatch, advance } = stopwatchFixture(); const watch = Stopwatch.startNew();
    advance(1.25); watch.start();
    assert.equal(watch.elapsed.TotalMilliseconds, 1.25);
    assert.equal(watch.ElapsedMilliseconds, 1);
    advance(2.5);
    assert.equal(watch.elapsed.TotalMilliseconds, 3.75);
    assert.equal(watch.elapsedTicks, 3750);
    assert.equal(watch.isRunning, true);
});

test('stop excludes idle time and start accumulates completed intervals', () => {
    const { Stopwatch, advance } = stopwatchFixture(); const watch = Stopwatch.startNew();
    advance(2.75); watch.stop(); advance(100); watch.stop();
    assert.equal(watch.elapsed.TotalMilliseconds, 2.75);
    watch.start(); advance(3.5); watch.stop();
    assert.equal(watch.elapsed.TotalMilliseconds, 6.25);
    assert.equal(watch.ElapsedMilliseconds, 6);
    advance(10); assert.equal(watch.ElapsedMilliseconds, 6);
});

test('restart discards prior running and stopped intervals', () => {
    const { Stopwatch, advance } = stopwatchFixture(); const watch = Stopwatch.startNew();
    advance(12); watch.restart();
    assert.equal(watch.ElapsedMilliseconds, 0); assert.equal(watch.isRunning, true);
    advance(4); watch.stop(); advance(10); watch.restart(); advance(2);
    assert.equal(watch.ElapsedMilliseconds, 2);
});

test('counter frequency and TimeSpan tick units agree', () => {
    const { Stopwatch, advance } = stopwatchFixture(); const start = Stopwatch.getTimestamp();
    const watch = Stopwatch.startNew(); advance(1234.567);
    assert.equal(Stopwatch.getTimestamp() - start, 1234567);
    assert.equal(watch.elapsedTicks / Stopwatch.Frequency, 1.234567);
    assert.equal(watch.elapsed.Ticks, 12345670);
    assert.equal(watch.ElapsedMilliseconds, 1234);
});
