import { TimeSpan } from "../time-span";

export class Stopwatch {
    private _startTimeMillis = 0;
    private _running: boolean = false;
    private _totalTimeRunning = 0;

    get isRunning(): boolean {
        return this._running;
    }

    get elapsed(): TimeSpan {
        if (this._running) {
            this.updateTime();
        }

        return new TimeSpan(0, 0, 0, 0, this._totalTimeRunning);
    }

    static startNew(): Stopwatch {
        return new Stopwatch().start();
    }

    start(): Stopwatch {
        this._running = true;
        this._startTimeMillis = performance.now();
        
        return this;
    }

    restart(): void {
        this.start();
    }

    /**
     */
    stop(): void {
        this.updateTime();

        this._running = false;
    }

    private updateTime(): void {
        this._totalTimeRunning = performance.now() - this._startTimeMillis;
    }
}
