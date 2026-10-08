/** Shared cancellation state; tokens retain this state after their source is disposed. */
export class CancellationState {
    /** Whether a cancellation request has been published. */
    public Canceled = false;
    /** Whether the owning source has released its registrations. */
    public Disposed = false;
    /** Registered callbacks keyed by monotonically increasing registration identifiers. */
    public readonly Callbacks = new Map<number, () => void>();
    /** Identifier allocated to the next registration. */
    public NextId = 0;
    /** Creates shared state with the token's permanent cancellation capability. */
    constructor(public readonly CanCancel: boolean) {}
}
