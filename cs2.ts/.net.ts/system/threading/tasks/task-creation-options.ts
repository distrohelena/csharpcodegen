/** CLR task creation flags; browser completion sources support None and asynchronous continuations. */
export enum TaskCreationOptions {
    /** Uses the runtime's ordinary continuation scheduling. */
    None = 0,
    /** Requests fair scheduling when supported by a task scheduler. */
    PreferFairness = 1,
    /** Requests a dedicated long-running execution strategy. */
    LongRunning = 2,
    /** Attaches a task to its enclosing parent task. */
    AttachedToParent = 4,
    /** Prevents child tasks from attaching to a parent. */
    DenyChildAttach = 8,
    /** Hides a current task scheduler from nested tasks. */
    HideScheduler = 16,
    /** Schedules continuations asynchronously; browser Promises already guarantee this. */
    RunContinuationsAsynchronously = 64,
}
