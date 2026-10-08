import type { Task } from '../../cs2.ts/.net.ts/system/threading/tasks/task';
import { TaskCompletionSource } from '../../cs2.ts/.net.ts/system/threading/tasks/task-completion-source';

/** A generic Task is valid wherever C# expects the non-generic base Task. */
const completion = new TaskCompletionSource<boolean>();
const genericTask: Task<boolean> = completion.Task;
const baseTask: Task = genericTask;
/** Callers of the base Task may await completion but cannot assume a particular result type. */
const opaque: Promise<unknown> = baseTask;
// @ts-expect-error A non-generic Task does not promise an undefined result.
const voidResult: Promise<void> = baseTask;
void opaque;
void voidResult;
