import { describe, expect, it } from 'vitest';
import { Bufferbloat } from './Bufferbloat';
import type { SpeedMeasurement } from './SpeedTestEngine';

const measured = (latencyMs: number | null, down: number | null, up: number | null = null): SpeedMeasurement => ({
    downloadMbps: 100, uploadMbps: 20, latencyMs, jitterMs: 2, downloadLoadedLatencyMs: down, uploadLoadedLatencyMs: up,
});

describe('Bufferbloat', () => {
    it('grades the extra delay of the busier direction', () => {
        const result = Bufferbloat.from(measured(14.6, 35.1, 16.1));
        expect(result?.increaseMs).toBeCloseTo(20.5);
        expect(result?.grade).toBe('A');
        expect(Bufferbloat.from(measured(15, 16, 90))?.grade).toBe('C');
    });

    it.each([
        [4.9, 'A+'], [5, 'A'], [29.9, 'A'], [30, 'B'], [59.9, 'B'], [60, 'C'], [199, 'C'], [200, 'D'], [399, 'D'], [400, 'F'],
    ])('an extra %d ms is grade %s', (increase, grade) => {
        expect(Bufferbloat.from(measured(10, 10 + increase))?.grade).toBe(grade);
    });

    it('counts a busy connection that answered faster by chance as no extra delay', () => {
        expect(Bufferbloat.from(measured(20, 18))).toMatchObject({ increaseMs: 0, grade: 'A+' });
    });

    it('uses whichever busy direction was measured', () => {
        expect(Bufferbloat.from(measured(15, null, 20.5))?.grade).toBe('A');
    });

    it('gives no grade without an idle or any busy latency', () => {
        expect(Bufferbloat.from(measured(null, 30))).toBeNull();
        expect(Bufferbloat.from(measured(15, null, null))).toBeNull();
    });

    it('calls grade B or better low', () => {
        expect(Bufferbloat.from(measured(10, 69))?.isLow()).toBe(true);
        expect(Bufferbloat.from(measured(10, 70))?.isLow()).toBe(false);
    });
});
