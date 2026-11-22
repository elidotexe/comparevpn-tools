import type { SpeedMeasurement } from './SpeedTestEngine';

/** From best to worst. */
export type BufferbloatGrade = 'A+' | 'A' | 'B' | 'C' | 'D' | 'F';

/**
 * Grades how much slower a connection answers while it is busy, the delay known as "bufferbloat". It is why a
 * video call stutters or a game lags when someone else in the house starts a big download, even on a fast line.
 *
 * In plain terms, here is what happens:
 *   1. It takes the time a tiny request needs while the connection is idle, and the same time while it is busy
 *      downloading and while it is busy uploading.
 *   2. It works out how much the busier of the two adds.
 *   3. It turns that extra delay into a school-style grade, A+ to F.
 * Without an idle time or a busy time there is nothing to compare, and it gives no grade.
 */
export class Bufferbloat {
    // The highest extra delay, in milliseconds, for each grade. Anything above the last one is an F.
    private static readonly gradeLimits: readonly [BufferbloatGrade, number][] = [
        ['A+', 5],
        ['A', 30],
        ['B', 60],
        ['C', 200],
        ['D', 400],
    ];

    private constructor(
        /** How many milliseconds a busy connection adds to each request. */
        public readonly increaseMs: number,
        public readonly grade: BufferbloatGrade,
    ) {}

    /** Steps 1–3: The grade for a test's figures, or null when they lack an idle or a busy latency. */
    public static from(measurement: SpeedMeasurement): Bufferbloat | null {
        // Step 1
        const busy = [measurement.downloadLoadedLatencyMs, measurement.uploadLoadedLatencyMs].filter(
            (value): value is number => value !== null,
        );
        if (measurement.latencyMs === null || busy.length === 0) return null;
        // Step 2: A busy connection can answer slightly faster by chance; that counts as no extra delay.
        const increaseMs = Math.max(0, Math.max(...busy) - measurement.latencyMs);
        // Step 3
        return new Bufferbloat(increaseMs, Bufferbloat.gradeFor(increaseMs));
    }

    /** Whether the extra delay is small enough not to disturb calls and games (grade B or better). */
    public isLow(): boolean {
        return this.grade === 'A+' || this.grade === 'A' || this.grade === 'B';
    }

    private static gradeFor(increaseMs: number): BufferbloatGrade {
        return Bufferbloat.gradeLimits.find(([, limit]) => increaseMs < limit)?.[0] ?? 'F';
    }
}
