import { ArgumentNullException } from '../../argument-null.exception';
import { ArgumentOutOfRangeException } from '../../argument-out-of-range.exception';
import { InvalidOperationException } from '../../invalid-operation.exception';
import { NotSupportedException } from '../../not-supported.exception';
import { CancellationToken } from '../cancellation-token';
import { TaskCanceledException } from './task-canceled.exception';
import { TaskCreationOptions } from './task-creation-options';
import { TaskStatusRegistry } from './task-status-registry';
/** Completes a browser Task exactly once, with a result, fault, or cancellation. */
export class TaskCompletionSource<T> {
    /** Promise observed by converted callers. */
    public readonly Task: Promise<T>;
    /** Whether one completion operation has already claimed the promise. */
    private settled = false;
    /** Promise resolver captured at construction. */
    private resolve!: (value: T | PromiseLike<T>) => void;
    /** Promise rejection operation captured at construction. */
    private reject!: (reason: unknown) => void;
    /** Creates a pending task with browser-compatible completion scheduling. */
    constructor(options: TaskCreationOptions = TaskCreationOptions.None) {
        if (!Number.isInteger(options) || options < 0 || options > 68 || (options & ~68) !== 0) throw new ArgumentOutOfRangeException('creationOptions');
        if ((options & TaskCreationOptions.AttachedToParent) !== 0) throw new NotSupportedException('Browser Tasks do not support parent-task attachment.');
        this.Task = new Promise<T>((resolve, reject) => { this.resolve = resolve; this.reject = reject; });
    }
    /** Claims successful completion, leaving a previous completion intact. */
    public TrySetResult(value: T): boolean {
        if (this.settled) return false;
        this.settled = true;
        TaskStatusRegistry.Set(this.Task, false);
        this.resolve(value);
        return true;
    }
    /** Completes successfully or throws if the task was already completed. */
    public SetResult(value: T): void {
        if (!this.TrySetResult(value)) throw new InvalidOperationException('The task has already completed.');
    }
    /** Claims a fault without replacing the supplied failure object. */
    public TrySetException(error: unknown): boolean {
        if (error == null) throw new ArgumentNullException('exception');
        if (this.settled) return false;
        this.settled = true;
        TaskStatusRegistry.Set(this.Task, false);
        this.reject(error);
        return true;
    }
    /** Faults the task or throws if another completion already won. */
    public SetException(error: unknown): void {
        if (!this.TrySetException(error)) throw new InvalidOperationException('The task has already completed.');
    }
    /** Claims cancellation and retains its token even when that token was not itself canceled. */
    public TrySetCanceled(token: CancellationToken = CancellationToken.None): boolean {
        if (token == null) throw new ArgumentNullException('cancellationToken');
        if (this.settled) return false;
        this.settled = true;
        TaskStatusRegistry.Set(this.Task, true);
        this.reject(new TaskCanceledException(token));
        return true;
    }
    /** Cancels the task or throws if it already has a terminal outcome. */
    public SetCanceled(token: CancellationToken = CancellationToken.None): void {
        if (!this.TrySetCanceled(token)) throw new InvalidOperationException('The task has already completed.');
    }
}
