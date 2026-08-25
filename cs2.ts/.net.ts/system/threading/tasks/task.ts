// @ts-nocheck
export type Task<T> = Promise<T>;

// Value counterpart so `import { Task }` resolves under rollup and `Task.Delay(ms)` works at runtime.
export const Task = {
    Delay(milliseconds: number): Promise<void> {
        return new Promise<void>(resolve => setTimeout(resolve, milliseconds));
    }
};