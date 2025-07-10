import { Stopwatch } from "../diagnostics/stopwatch";

export class Thread {
    private worker: Worker | null = null;
    private isRunning: boolean = false;

    static Sleep(milliseconds: number): void {
        const sharedBuffer = new SharedArrayBuffer(4); // 4 bytes for an Int32
        const int32Array = new Int32Array(sharedBuffer);
        Atomics.wait(int32Array, 0, 0, milliseconds);
    }

    constructor(workerFunction: (state: any) => void) {
        let fnCode = workerFunction.toString();
        const endArg = fnCode.indexOf(')');
        if (endArg !== -1) {
            fnCode = fnCode.substring(endArg + 1);
        }

        const test = Stopwatch.toString();
        console.log(test);

        const workerBlob = new Blob(
            [
                `
            const Thread = {
              Sleep: (milliseconds) => {
                const sharedBuffer = new SharedArrayBuffer(4); // 4 bytes for an Int32
                const int32Array = new Int32Array(sharedBuffer);
                Atomics.wait(int32Array, 0, 0, milliseconds);
              }
            };
  
            self.onmessage = function(event) {
                ${fnCode};
            };
          `
            ],
            { type: "application/javascript" }
        );
        const workerUrl = URL.createObjectURL(workerBlob);
        this.worker = new Worker(workerUrl);
    }

    start(): void {
        if (this.isRunning) {
            throw new Error("Thread is already running.");
        }
        if (this.worker) {
            this.isRunning = true;
            this.worker.postMessage("start");
        }
    }

    stop(): void {
        if (this.worker) {
            this.worker.terminate();
            this.worker = null;
            this.isRunning = false;
        }
    }

    isAlive(): boolean {
        return this.isRunning;
    }
}
