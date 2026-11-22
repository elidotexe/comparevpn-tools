import { Bufferbloat } from './Bufferbloat';
import type { SpeedMeasurement } from './SpeedTestEngine';

/** The everyday activities a result is checked against. */
export type Activity = 'browsing' | 'hd-streaming' | '4k-streaming' | 'video-calls' | 'gaming';

/** Whether a connection handles an activity well. */
export interface ActivityResult {
    readonly activity: Activity;
    readonly suitable: boolean;
}

/**
 * Says what a connection is good for, in everyday terms, from a speed test's figures, like a label on a
 * washing machine listing which loads it can take.
 *
 * In plain terms, here is what happens:
 *   1. Browsing needs 1 Mbps down, HD streaming 5 Mbps, and 4K streaming 25 Mbps.
 *   2. Video calls need 3 Mbps both ways and requests answered within 150 ms.
 *   3. Online gaming needs 3 Mbps down, requests answered within 50 ms, and little extra delay while the
 *      connection is busy (bufferbloat grade B or better, when it was measured).
 * A figure the test couldn't measure counts against the activities that need it.
 */
export class ActivityCheck {
    /** Steps 1–3: Each activity, in the order above, with whether this connection handles it well. */
    public static check(measurement: SpeedMeasurement): ActivityResult[] {
        const { downloadMbps, uploadMbps, latencyMs } = measurement;
        const answersWithin = (limitMs: number) => latencyMs !== null && latencyMs <= limitMs;
        const bufferbloat = Bufferbloat.from(measurement);
        return [
            // Step 1
            { activity: 'browsing', suitable: downloadMbps >= 1 },
            { activity: 'hd-streaming', suitable: downloadMbps >= 5 },
            { activity: '4k-streaming', suitable: downloadMbps >= 25 },
            // Step 2
            { activity: 'video-calls', suitable: downloadMbps >= 3 && (uploadMbps ?? 0) >= 3 && answersWithin(150) },
            // Step 3
            { activity: 'gaming', suitable: downloadMbps >= 3 && answersWithin(50) && (bufferbloat?.isLow() ?? true) },
        ];
    }
}
