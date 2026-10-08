import { ArgumentNullException } from '../argument-null.exception';
import { OperationCanceledException } from '../operation-canceled.exception';
import { CancellationState } from './cancellation-state';
import { CancellationTokenRegistration } from './cancellation-token-registration';
/** Read-only view of a cooperative cancellation request. */
export class CancellationToken {
    /** Shared state for tokens that can never be canceled. */
    private static readonly EmptyState = new CancellationState(false);
    /** Token representing an operation with no cancellation source. */
    public static readonly None = new CancellationToken();
    /** State shared with the source or owned by a pre-canceled token. */
    private readonly state: CancellationState;
    /** Creates a permanent token, or binds the internal shared state of a source. */
    constructor(canceled = false, state?: CancellationState) {
        this.state = state ?? (canceled ? new CancellationState(true) : CancellationToken.EmptyState);
        if (canceled) this.state.Canceled = true;
    }
    /** Reports whether this token has ever received a cancellation request. */
    public get IsCancellationRequested(): boolean { return this.state.Canceled; }
    /** Reports whether cancellation is possible, even after cancellation or disposal. */
    public get CanBeCanceled(): boolean { return this.state.CanCancel; }
    /** Stops a cooperating operation with an error retaining this token. */
    public ThrowIfCancellationRequested(): void {
        if (this.state.Canceled) throw new OperationCanceledException(this);
    }
    /** Registers synchronously; an already-canceled token invokes the callback immediately. */
    public Register(callback: (state?: unknown) => void, state?: unknown): CancellationTokenRegistration {
        if (callback == null) throw new ArgumentNullException('callback');
        if (this.state.Canceled) {
            callback(state);
            return new CancellationTokenRegistration();
        }
        if (!this.state.CanCancel || this.state.Disposed) return new CancellationTokenRegistration();
        const id = this.state.NextId++;
        this.state.Callbacks.set(id, () => callback(state));
        return new CancellationTokenRegistration(() => this.state.Callbacks.delete(id));
    }
}
