import { describe, expect, it } from 'vitest';
import { ActivityCheck } from './ActivityCheck';
import type { Activity } from './ActivityCheck';
import type { SpeedMeasurement } from './SpeedTestEngine';

const fast: SpeedMeasurement = {
    downloadMbps: 470, uploadMbps: 52, latencyMs: 14.6, jitterMs: 6, downloadLoadedLatencyMs: 35, uploadLoadedLatencyMs: 16,
};

/** The activities this connection handles well. */
const suitableFor = (measurement: SpeedMeasurement): Activity[] =>
    ActivityCheck.check(measurement).filter(result => result.suitable).map(result => result.activity);

describe('ActivityCheck', () => {
    it('lists every activity, in a fixed order', () => {
        expect(ActivityCheck.check(fast).map(result => result.activity)).toEqual(['browsing', 'hd-streaming', '4k-streaming', 'video-calls', 'gaming']);
        expect(suitableFor(fast)).toEqual(['browsing', 'hd-streaming', '4k-streaming', 'video-calls', 'gaming']);
    });

    it('needs 1, 5 and 25 Mbps down for browsing, HD and 4K streaming', () => {
        expect(suitableFor({ ...fast, downloadMbps: 0.9 })).toEqual([]);
        expect(suitableFor({ ...fast, downloadMbps: 4.9 })).toEqual(['browsing', 'video-calls', 'gaming']);
        expect(suitableFor({ ...fast, downloadMbps: 24.9 })).not.toContain('4k-streaming');
        expect(suitableFor({ ...fast, downloadMbps: 25 })).toContain('4k-streaming');
    });

    it('needs 3 Mbps both ways and an answer within 150 ms for video calls', () => {
        expect(suitableFor({ ...fast, uploadMbps: 2.9 })).not.toContain('video-calls');
        expect(suitableFor({ ...fast, uploadMbps: null })).not.toContain('video-calls');
        expect(suitableFor({ ...fast, latencyMs: 150, downloadLoadedLatencyMs: null, uploadLoadedLatencyMs: null })).toContain('video-calls');
        expect(suitableFor({ ...fast, latencyMs: 151 })).not.toContain('video-calls');
    });

    it('needs an answer within 50 ms and low bufferbloat for gaming', () => {
        expect(suitableFor({ ...fast, latencyMs: 51 })).not.toContain('gaming');
        expect(suitableFor({ ...fast, latencyMs: null })).not.toContain('gaming');
        expect(suitableFor({ ...fast, downloadLoadedLatencyMs: 120 })).not.toContain('gaming');
        expect(suitableFor({ ...fast, downloadLoadedLatencyMs: null, uploadLoadedLatencyMs: null })).toContain('gaming');
    });
});
