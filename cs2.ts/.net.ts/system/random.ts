// @ts-nocheck
export class Random {
    private static readonly shared = new Random();

    /** Mirrors the process-wide .NET Random.Shared instance. */
    public static get Shared(): Random {
        return Random.shared;
    }

    /** Returns a pseudo-random number in the half-open interval [0, 1). */
    public NextDouble(): number {
        return Math.random();
    }
}
