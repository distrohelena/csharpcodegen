import type { CancellationToken } from './threading/cancellation-token';
/** Cancellation failure retaining the token that caused the operation to stop. */
export class OperationCanceledException extends Error {
    /** Token supplied by the canceled operation. */
    public readonly CancellationToken: CancellationToken;
    /** Creates a cancellation error without replacing the original token. */
    constructor(token: CancellationToken, message = 'The operation was canceled.') {
        super(message);
        this.name = 'OperationCanceledException';
        this.CancellationToken = token;
    }
    /** Exposes the CLR message spelling to converted consumers. */
    public get Message(): string { return this.message; }
}
