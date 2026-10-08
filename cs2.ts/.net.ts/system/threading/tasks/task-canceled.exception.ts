import type { CancellationToken } from '../cancellation-token';
import { OperationCanceledException } from '../../operation-canceled.exception';
/** Cancellation of a task, distinguishable from ordinary task faults. */
export class TaskCanceledException extends OperationCanceledException {
    /** Records the token responsible for the task's canceled completion. */
    constructor(token: CancellationToken) {
        super(token, 'A task was canceled.');
        this.name = 'TaskCanceledException';
    }
}
