import { AggregateException } from '../aggregate.exception';
import { ObjectDisposedException } from '../object-disposed.exception';
import { TimeSpan } from '../time-span';
import { CancellationState } from './cancellation-state';
import { CancellationTimer } from './cancellation-timer';
import { CancellationToken } from './cancellation-token';
import { CancellationTokenRegistration } from './cancellation-token-registration';
/** Owns cancellation requests and callback lifetime independently of outstanding token values. */
export class CancellationTokenSource {
    /** State observed by every token issued by this source. */
    private readonly state = new CancellationState(true);
    /** Stable token instance handed to cooperating operations. */
    private readonly token = new CancellationToken(false, this.state);
    /** Timer for a scheduled cancellation request. */
    private timer: CancellationTimer | undefined;
    /** Registrations connecting this source to parent tokens. */
    private readonly links: CancellationTokenRegistration[] = [];
    /** Creates a source and optionally schedules cancellation after a duration. */
    constructor(delay?: number | TimeSpan) { if (delay !== undefined) this.CancelAfter(delay); }
    /** Returns the source's token while the source remains usable. */
    public get Token(): CancellationToken { this.ThrowIfDisposed(); return this.token; }
    /** Reports cancellation independently of whether resources have been disposed. */
    public get IsCancellationRequested(): boolean { return this.state.Canceled; }
    /** Publishes cancellation synchronously, running callbacks in reverse registration order. */
    public Cancel(throwOnFirstException = false): void {
        this.ThrowIfDisposed();
        if (this.state.Canceled) return;
        this.state.Canceled = true;
        this.timer?.Dispose();
        this.timer = undefined;
        const failures: unknown[] = [];
        for (const [id, callback] of [...this.state.Callbacks].reverse()) {
            if (!this.state.Callbacks.delete(id)) continue;
            try { callback(); }
            catch (error) {
                if (throwOnFirstException) { this.state.Callbacks.clear(); throw error; }
                failures.push(error);
            }
        }
        if (failures.length) throw new AggregateException(failures);
    }
    /** Replaces a scheduled cancellation; minus one disables the timer. */
    public CancelAfter(delay: number | TimeSpan): void {
        this.ThrowIfDisposed();
        const milliseconds = CancellationTimer.Milliseconds(delay);
        if (this.state.Canceled) return;
        this.timer?.Dispose();
        this.timer = milliseconds === -1 ? undefined : new CancellationTimer(milliseconds, () => this.Cancel());
    }
    /** Releases timers and registrations without issuing a cancellation request. */
    public Dispose(): void {
        if (this.state.Disposed) return;
        this.state.Disposed = true;
        this.timer?.Dispose();
        this.timer = undefined;
        this.state.Callbacks.clear();
        for (const link of this.links) link.Dispose();
        this.links.length = 0;
    }
    /** Creates a source canceled by any supplied parent token; disposal detaches every link. */
    public static CreateLinkedTokenSource(...tokens: (CancellationToken | readonly CancellationToken[])[]): CancellationTokenSource {
        const parents = tokens.flat() as CancellationToken[];
        if (!parents.length || parents.some(token => token == null)) throw new TypeError('At least one valid cancellation token is required.');
        const source = new CancellationTokenSource();
        for (const token of parents) source.links.push(token.Register(() => source.Cancel()));
        return source;
    }
    /** Enforces the source lifetime for operations that require its resources. */
    private ThrowIfDisposed(): void {
        if (this.state.Disposed) throw new ObjectDisposedException('CancellationTokenSource');
    }
}
