/** Deterministic clock that observes timer creation and disposal without waiting in real time. */
class TaskRuntimeClock {
    /** Captures globals, installs a monotonic clock, and tracks pending callbacks. */
    constructor() {
        this.now = 0;
        this.next = 1;
        this.timers = new Map();
        this.setTimeout = globalThis.setTimeout;
        this.clearTimeout = globalThis.clearTimeout;
        this.performance = Object.getOwnPropertyDescriptor(globalThis, 'performance');
        globalThis.setTimeout = (callback, delay) => {
            const id = this.next++;
            this.timers.set(id, { due: this.now + delay, callback });
            return id;
        };
        globalThis.clearTimeout = id => this.timers.delete(id);
        Object.defineProperty(globalThis, 'performance', { configurable: true, value: { now: () => this.now } });
    }
    /** Advances monotonically and executes every due callback, including newly scheduled timer chunks. */
    advance(milliseconds) {
        const end = this.now + milliseconds;
        while (true) {
            const next = [...this.timers].sort((left, right) => left[1].due - right[1].due)[0];
            if (!next || next[1].due > end) break;
            this.now = next[1].due;
            this.timers.delete(next[0]);
            next[1].callback();
        }
        this.now = end;
    }
    /** Restores globals even when a test assertion fails. */
    dispose() {
        globalThis.setTimeout = this.setTimeout;
        globalThis.clearTimeout = this.clearTimeout;
        Object.defineProperty(globalThis, 'performance', this.performance);
    }
}
module.exports = { TaskRuntimeClock };
