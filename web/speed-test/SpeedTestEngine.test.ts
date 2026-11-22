import { describe, expect, it } from 'vitest';
import { SpeedTestEngine } from './SpeedTestEngine';
import type { RunningTest, TestListeners, TestSettings, TestSummary } from './SpeedTestEngine';

/** A pretend library test: records its settings, and lets each test decide what it reports and when. */
class FakeTest implements RunningTest {
    public listeners: TestListeners | null = null;
    public paused = false;

    constructor(public readonly settings: TestSettings) {}

    start(listeners: TestListeners): void { this.listeners = listeners; }
    pause(): void { this.paused = true; }

    get played(): boolean { return this.listeners !== null; }

    stepStarted(stepIndex: number): void { this.listeners?.stepStarted(stepIndex); }
    report(summary: TestSummary): void { this.listeners?.resultsChanged({ getSummary: () => summary }); }
    finishWith(summary: TestSummary): void { this.listeners?.finished({ getSummary: () => summary }); }
}

function engineWithFake(): { engine: SpeedTestEngine; test: () => FakeTest } {
    let created: FakeTest | null = null;
    const engine = new SpeedTestEngine(settings => (created = new FakeTest(settings)));
    return { engine, test: () => { if (!created) throw new Error('No test was created'); return created; } };
}

describe('SpeedTestEngine', () => {
    it("tests against Cloudflare's servers without reporting results to Cloudflare, and starts it itself", () => {
        const { engine, test } = engineWithFake();
        void engine.run('full');
        expect(test().settings.measurementTargets).toEqual(['https://speed.cloudflare.com']);
        expect(test().settings.logAimApiUrl).toBeNull();
        expect(test().settings.logMeasurementApiUrl).toBeNull();
        expect(test().settings.autoStart).toBe(false);
        expect(test().played).toBe(true);
    });

    it('never asks for a download or upload larger than the 25 MB Cloudflare allows, and the quick test stops at 10 MB', () => {
        const largest = (mode: 'full' | 'quick') =>
            Math.max(...SpeedTestEngine.stepsFor(mode).map(step => (step.type === 'latency' ? 0 : step.bytes)));
        expect(largest('full')).toBe(25e6);
        expect(largest('quick')).toBe(1e7);
        expect(SpeedTestEngine.stepsFor('full').some(step => step.type === 'upload')).toBe(true);
    });

    it('always ends: the full test after at most a minute, the quick test after half a minute', () => {
        const full = engineWithFake();
        void full.engine.run('full');
        expect(full.test().settings.abortTestDuration).toBe(60_000);
        const quick = engineWithFake();
        void quick.engine.run('quick');
        expect(quick.test().settings.abortTestDuration).toBe(30_000);
    });

    it('turns the finished figures into megabits per second and milliseconds', async () => {
        const { engine, test } = engineWithFake();
        const running = engine.run('full');
        test().finishWith({ download: 471_100_000, upload: 51_800_000, latency: 14.6, jitter: 10.6, downLoadedLatency: 35.1, upLoadedLatency: 16.1 });
        expect(await running).toEqual({
            outcome: 'finished',
            measurement: { downloadMbps: 471.1, uploadMbps: 51.8, latencyMs: 14.6, jitterMs: 10.6, downloadLoadedLatencyMs: 35.1, uploadLoadedLatencyMs: 16.1 },
        });
    });

    it('reports figures it could not measure as null', async () => {
        const { engine, test } = engineWithFake();
        const running = engine.run('quick');
        test().finishWith({ download: 20_000_000, latency: Number.NaN });
        expect(await running).toEqual({
            outcome: 'finished',
            measurement: { downloadMbps: 20, uploadMbps: null, latencyMs: null, jitterMs: null, downloadLoadedLatencyMs: null, uploadLoadedLatencyMs: null },
        });
    });

    it('reports a busy-connection latency of 0 as not measured: the downloads were too short to load the line', async () => {
        const { engine, test } = engineWithFake();
        const running = engine.run('quick');
        test().finishWith({ download: 370_000_000, latency: 15, downLoadedLatency: 0, upLoadedLatency: 20.5 });
        const outcome = await running;
        expect(outcome.outcome === 'finished' && outcome.measurement.downloadLoadedLatencyMs).toBeNull();
        expect(outcome.outcome === 'finished' && outcome.measurement.uploadLoadedLatencyMs).toBe(20.5);
    });

    it('fails when no download speed was measured, e.g. without a connection', async () => {
        const { engine, test } = engineWithFake();
        const running = engine.run('full');
        test().finishWith({ latency: 12 });
        expect(await running).toEqual({ outcome: 'failed' });
    });

    it('reports the figures so far and how far through its steps it is', () => {
        const { engine, test } = engineWithFake();
        const heard: unknown[] = [];
        void engine.run('quick', progress => heard.push(progress));
        test().stepStarted(4);
        test().report({ download: 50_000_000, latency: 15 });
        expect(heard).toEqual([
            { downloadMbps: 50, uploadMbps: null, latencyMs: 15, stepsDone: 4, stepsTotal: SpeedTestEngine.stepsFor('quick').length },
        ]);
    });

    it('stops when asked, pausing the library, and ignores a late finish', async () => {
        const { engine, test } = engineWithFake();
        const running = engine.run('full');
        engine.stop();
        test().finishWith({ download: 100_000_000 });
        expect(await running).toEqual({ outcome: 'stopped' });
        expect(test().paused).toBe(true);
    });

    it('runs one test at a time, and can run again once the first has ended', async () => {
        const { engine, test } = engineWithFake();
        const first = engine.run('quick');
        expect(() => engine.run('quick')).toThrow('already running');
        test().finishWith({ download: 10_000_000 });
        await first;
        const second = engine.run('quick');
        test().finishWith({ download: 12_000_000 });
        expect((await second).outcome).toBe('finished');
    });

    it('does nothing when stopped while no test is running', () => {
        expect(() => new SpeedTestEngine(() => { throw new Error('not used'); }).stop()).not.toThrow();
    });
});
