const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), {
    compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS }
}).outputText, file);
const { ManualResetEventSlim } = require(path.join(root, 'system/threading/manual-reset-event-slim.ts'));
const { ArgumentOutOfRangeException } = require(path.join(root, 'system/argument-out-of-range.exception.ts'));
const { ObjectDisposedException } = require(path.join(root, 'system/object-disposed.exception.ts'));
const { TaskRuntimeClock } = require('./task-runtime-clock.cjs');

test('ManualResetEventSlim resolves delayed waiters and releases their timeout handles', async () => {
    const clock = new TaskRuntimeClock();
    try {
        const signal = new ManualResetEventSlim(false);
        const first = signal.Wait(100);
        const second = signal.Wait(100);
        assert.equal(clock.timers.size, 2);
        clock.advance(4);
        signal.Set();
        assert.equal(await first, true);
        assert.equal(await second, true);
        assert.equal(clock.timers.size, 0);
        assert.equal(signal.IsSet, true);
        assert.equal(await signal.Wait(100), true);
        assert.equal(clock.timers.size, 0);
    } finally { clock.dispose(); }
});

test('ManualResetEventSlim reports timeout, validates timeout values, and rejects pending waits on disposal', async () => {
    const clock = new TaskRuntimeClock();
    try {
        const signal = new ManualResetEventSlim(false);
        const timed = signal.Wait(5);
        assert.equal(clock.timers.size, 1);
        clock.advance(5);
        assert.equal(await timed, false);
        assert.equal(clock.timers.size, 0);
        assert.throws(() => signal.Wait(-2), ArgumentOutOfRangeException);
        assert.throws(() => signal.Wait(0.5), ArgumentOutOfRangeException);
        const pending = signal.Wait(-1);
        signal.Dispose();
        await assert.rejects(pending, ObjectDisposedException);
        assert.throws(() => signal.Set(), ObjectDisposedException);
        assert.throws(() => signal.Reset(), ObjectDisposedException);
        assert.throws(() => signal.Wait(1), ObjectDisposedException);
    } finally { clock.dispose(); }
});