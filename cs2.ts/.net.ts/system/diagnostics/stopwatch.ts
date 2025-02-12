import { TimeSpan } from "../time-span";

export class Stopwatch {
    private _startTimeMillis = 0;
    private _running: boolean = false;
    private _totalTimeRunning = 0;
    
    get IsRunning() {
        return this._running;
    }

    get Elapsed() {
        if (this._running) {
            this.updateTime();
        }

        return new TimeSpan(0, 0, 0, 0, this._totalTimeRunning);
    }

    static StartNew(): Stopwatch {
        return new Stopwatch().Start();
    }

    Start(): Stopwatch {
        this._running = true;
        this._startTimeMillis = performance.now();
        
        return this;
    }

    Restart() {
        this.Start();
    }

    /**
     */
    Stop(): void {
        this.updateTime();

        this._running = false;
    }

    private updateTime() {
        this._totalTimeRunning = performance.now() - this._startTimeMillis;
    }
}
