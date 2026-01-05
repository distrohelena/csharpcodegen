export type EventHandler<TEventArgs = any> = (sender: any, e: TEventArgs) => void;

export class Event {
    private handlers: Array<(...args: any[]) => void> = [];

    public Add(handler: (...args: any[]) => void): void {
        if (!handler) {
            return;
        }

        this.handlers.push(handler);
    }

    public Remove(handler: (...args: any[]) => void): void {
        if (!handler) {
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

    public Clear(): void {
        this.handlers.length = 0;
    }
}
