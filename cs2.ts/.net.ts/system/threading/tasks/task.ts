import { ArgumentNullException } from '../../argument-null.exception';
import { ArgumentException } from '../../argument.exception';
import { ArgumentOutOfRangeException } from '../../argument-out-of-range.exception';
import { TimeSpan } from '../../time-span';
import { CancellationToken } from '../cancellation-token';
import { CancellationTokenRegistration } from '../cancellation-token-registration';
import { CancellationTimer } from '../cancellation-timer';
import { TaskCompletionSource } from './task-completion-source';
import { TaskStatusRegistry } from './task-status-registry';

/** Browser tasks use native promise completion and asynchronous continuations. */
export type Task<T = unknown> = Promise<T>;

/** Framework task operations used by converted browser workflows. */
export const Task = {
    /** Stable successfully completed task for operations with no result. */
    CompletedTask: Promise.resolve(),
    /** Delays without blocking, releasing its timer and registration on completion or cancellation. */
    Delay(delay: number | TimeSpan, cancellationToken: CancellationToken = CancellationToken.None): Promise<void> {
        const milliseconds = CancellationTimer.Milliseconds(delay);
        if (cancellationToken == null) throw new ArgumentNullException('cancellationToken');
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<void>(cancellationToken);
        if (milliseconds === 0) return Task.CompletedTask;
        const completion = new TaskCompletionSource<void>();
        let timer: CancellationTimer | undefined;
        let registration: CancellationTokenRegistration | undefined;
        registration = cancellationToken.Register(() => {
            timer?.Dispose();
            registration?.Dispose();
            completion.TrySetCanceled(cancellationToken);
        });
        if (milliseconds !== -1 && !cancellationToken.IsCancellationRequested) {
            timer = new CancellationTimer(milliseconds, () => {
                registration?.Dispose();
                completion.TrySetResult(undefined);
            });
        }
        return completion.Task;
    },
    /** Produces a successful promise using the browser's normal promise resolution rules. */
    FromResult<T>(value: T): Promise<Awaited<T>> { return Promise.resolve(value); },
    /** Produces a faulted task retaining the exact supplied failure. */
    FromException<T = never>(error: unknown): Promise<T> {
        const completion = new TaskCompletionSource<T>();
        completion.SetException(error);
        return completion.Task;
    },
    /** Produces a canceled task only for a token whose cancellation has been requested. */
    FromCanceled<T = never>(token: CancellationToken): Promise<T> {
        if (token == null) throw new ArgumentNullException('cancellationToken');
        if (!token.IsCancellationRequested) throw new ArgumentOutOfRangeException('cancellationToken');
        const completion = new TaskCompletionSource<T>();
        completion.SetCanceled(token);
        return completion.Task;
    },
    /** Waits for every input, preserves result order, and gives faults precedence over cancellation. */
    WhenAll<T>(tasks: Iterable<PromiseLike<T>>): Promise<Awaited<T>[]> {
        if (tasks == null) throw new ArgumentNullException('tasks');
        const inputs = Array.from(tasks);
        if (inputs.some(task => task == null)) throw new ArgumentException('The tasks collection contains a null task.', 'tasks');
        return Promise.allSettled(inputs).then(results => {
            const values: Awaited<T>[] = [];
            let failed = false;
            let failure: unknown;
            let canceled = false;
            let cancellation: unknown;
            for (let index = 0; index < results.length; index++) {
                const result = results[index];
                if (result.status === 'fulfilled') values.push(result.value);
                else if (TaskStatusRegistry.IsCanceled(inputs[index], result.reason)) {
                    if (!canceled) { canceled = true; cancellation = result.reason; }
                } else if (!failed) { failed = true; failure = result.reason; }
            }
            if (failed) throw failure;
            if (canceled) throw cancellation;
            return values;
        });
    },
};
