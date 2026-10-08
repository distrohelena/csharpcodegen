import { TimeSpan } from '../time-span';

/** Infinite sentinels shared by managed timer and wait APIs. */
export class Timeout {
    /** Disables a timeout or periodic timer in numeric overloads. */
    static readonly Infinite = -1;
    /** Disables a timeout or periodic timer in TimeSpan overloads. */
    static readonly InfiniteTimeSpan = TimeSpan.fromMilliseconds(-1);
}
