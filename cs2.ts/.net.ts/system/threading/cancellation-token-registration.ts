/** Detaches one cancellation callback without changing the token's cancellation state. */
export class CancellationTokenRegistration {
    /** Cleanup retained until the registration is disposed. */
    private release: (() => void) | undefined;
    /** Associates a registration with its unsubscribe operation. */
    constructor(release: () => void = () => {}) { this.release = release; }
    /** Removes the callback once; repeated disposal is harmless. */
    public Dispose(): void {
        const release = this.release;
        this.release = undefined;
        if (release) release();
    }
}
