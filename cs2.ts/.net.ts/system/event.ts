export type EventHandler<TEventArgs = any> = (sender: any, e: TEventArgs) => void;

export class Event {
    private handlers: Array<(...args: any[]) => void> = [];

    public constructor(
        private readonly addHook?: (handler: (...args: any[]) => void) => void,
        private readonly removeHook?: (handler: (...args: any[]) => void) => void
    ) {
    }

    public Add(handler: (...args: any[]) => void): void {
        if (!handler) {
            return;
        }

        if (this.addHook) {
            this.addHook(handler);
            return;
        }

        this.handlers.push(handler);
    }

    public Remove(handler: (...args: any[]) => void): void {
        if (!handler) {
            return;
        }

        if (this.removeHook) {
            this.removeHook(handler);
            return;
        }

        for (let i = this.handlers.length - 1; i >= 0; i--) {
            if (this.handlers[i] === handler) {
                this.handlers.splice(i, 1);
                return;
            }
        }
    }

    public Emit(...args: any[]): void {
        const snapshot = this.handlers.slice();
        for (let i = 0; i < snapshot.length; i++) {
            snapshot[i](...args);
        }
    }

    public Invoke(...args: any[]): void {
        this.Emit(...args);
    }

    /** Returns a stable snapshot of the current invocation list. */
    public GetInvocationList(): Array<(...args: any[]) => void> {
        return this.handlers.slice();
    }

    public Clear(): void {
        this.handlers.length = 0;
    }
}
