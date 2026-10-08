const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');
const root = path.resolve(__dirname, '../../cs2.ts/.net.ts');
const ts = require(path.join(root, 'node_modules/typescript'));
require.extensions['.ts'] = (module, file) => module._compile(ts.transpileModule(fs.readFileSync(file, 'utf8'), { compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS } }).outputText, file);
const { Timer } = require(path.join(root, 'system/threading/timer.ts'));
const { TimeSpan } = require(path.join(root, 'system/time-span.ts'));

/** Deterministic monotonic clock that records host scheduling limits and cancellation. */
class TimerClock {
    /** Installs isolated timer globals for one synchronous contract test. */
    constructor() {
        this.Now = 0; this.Next = 0; this.Jobs = new Map(); this.Delays = [];
        this.Set = globalThis.setTimeout; this.Clear = globalThis.clearTimeout;
        this.Performance = Object.getOwnPropertyDescriptor(globalThis, 'performance');
        globalThis.setTimeout = (callback, delay) => {
            assert.ok(delay >= 0 && delay <= 2147483647, 'host timer delay must not overflow');
            const id = ++this.Next; this.Delays.push(delay); this.Jobs.set(id, { callback, due: this.Now + delay }); return id;
        };
        globalThis.clearTimeout = id => this.Jobs.delete(id);
        Object.defineProperty(globalThis, 'performance', { configurable: true, value: { now: () => this.Now } });
    }
    /** Runs due jobs in order while advancing the monotonic clock to the supplied deadline. */
    Advance(milliseconds) {
        const end = this.Now + milliseconds;
        for (;;) {
            const due = [...this.Jobs.entries()].filter(([, job]) => job.due <= end).sort((a, b) => a[1].due - b[1].due)[0];
            if (!due) break;
            this.Now = due[1].due; this.Jobs.delete(due[0]); due[1].callback();
        }
        this.Now = end;
    }
    /** Restores real process globals after the test even when its assertion fails. */
    Restore() {
        globalThis.setTimeout = this.Set; globalThis.clearTimeout = this.Clear;
        Object.defineProperty(globalThis, 'performance', this.Performance);
    }
}
let clock;
test.beforeEach(() => { clock = new TimerClock(); });
test.afterEach(() => { clock.Restore(); });

test('disabled timer arms once and preserves the exact callback state', () => {
    const state = { id: 1 }; const calls = [];
    const timer = new Timer(value => calls.push(value), state, -1, -1);
    clock.Advance(100); assert.deepEqual(calls, []);
    assert.equal(timer.Change(10, -1), true);
    clock.Advance(9); assert.deepEqual(calls, []);
    clock.Advance(1); assert.deepEqual(calls, [state]);
    clock.Advance(100); assert.equal(calls.length, 1);
});
test('periodic timer stops when its callback disposes it', () => {
    let calls = 0;
    const timer = new Timer(() => { calls += 1; if (calls === 3) timer.Dispose(); }, null, 0, 5);
    clock.Advance(100); assert.equal(calls, 3); assert.equal(clock.Jobs.size, 0);
    assert.equal(timer.Change(0, 1), false); timer.Dispose();
});
test('changing and disabling a timer invalidate pending callbacks', () => {
    let calls = 0; const timer = new Timer(() => calls++, null, 10, 1);
    const stale = [...clock.Jobs.values()][0].callback;
    timer.Change(20, 0); stale(); assert.equal(calls, 0);
    clock.Advance(19); assert.equal(calls, 0);
    clock.Advance(1); assert.equal(calls, 1);
    timer.Change(1, 1); timer.Change(-1, 1); clock.Advance(10); assert.equal(calls, 1);
});
test('TimeSpan timeouts truncate fractions and zero period is one-shot', () => {
    let calls = 0; const timer = new Timer(() => calls++, null, TimeSpan.fromMilliseconds(2.9), TimeSpan.Zero);
    clock.Advance(1); assert.equal(calls, 0);
    clock.Advance(1); assert.equal(calls, 1);
    timer.Change(TimeSpan.fromMilliseconds(-1.5), TimeSpan.fromMilliseconds(2));
    clock.Advance(10); assert.equal(calls, 1);
});
test('long CLR delays are chunked rather than overflowing browser timer bounds', () => {
    let calls = 0; const timer = new Timer(() => calls++, null, 4294967294, -1);
    clock.Advance(2147483647); assert.equal(calls, 0);
    clock.Advance(2147483646); assert.equal(calls, 0);
    clock.Advance(1); assert.equal(calls, 1); timer.Dispose();
});
test('callback-only constructor uses the timer as state and waits for Change', () => {
    let state; const timer = new Timer(value => state = value);
    clock.Advance(10); assert.equal(state, undefined);
    timer.Change(0, -1); clock.Advance(0); assert.equal(state, timer);
});
test('invalid CLR timeout bounds and missing callbacks fail before scheduling', () => {
    assert.throws(() => new Timer(null, null, 0, 1));
    for (const value of [-2, NaN, Infinity, 4294967295]) assert.throws(() => new Timer(() => {}, null, value, -1));
    const timer = new Timer(() => {}, null, -1, -1);
    assert.throws(() => timer.Change(0, -2));
    assert.equal(clock.Jobs.size, 0);
});

test('Timeout infinite sentinels keep numeric and TimeSpan timers disabled', () => {
    const { Timeout } = require(path.join(root, 'system/threading/timeout.ts'));
    assert.equal(Timeout.Infinite,-1); assert.equal(Timeout.InfiniteTimeSpan.TotalMilliseconds,-1);
    let calls=0; const numeric = new Timer(() => calls++,null,Timeout.Infinite,Timeout.Infinite);
    const span = new Timer(() => calls++,null,Timeout.InfiniteTimeSpan,Timeout.InfiniteTimeSpan);
    clock.Advance(100000); assert.equal(calls,0); numeric.Dispose(); span.Dispose();
});
