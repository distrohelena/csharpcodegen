import { OperationCanceledException } from '../../operation-canceled.exception';
/** Distinguishes canceled tasks from faults that happen to contain a cancellation exception. */
export class TaskStatusRegistry {
    /** Explicit completion classifications for runtime-owned Promises. */
    private static readonly Canceled = new WeakMap<object, boolean>();
    /** Records the terminal classification before promise continuations are released. */
    public static Set(task: object, canceled: boolean): void { this.Canceled.set(task, canceled); }
    /** Uses explicit status when present, otherwise recognizes an async function's cancellation rejection. */
    public static IsCanceled(task: object, reason: unknown): boolean {
        return this.Canceled.get(task) ?? reason instanceof OperationCanceledException;
    }
}
