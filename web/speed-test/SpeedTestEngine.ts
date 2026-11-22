import CloudflareSpeedTest from '@cloudflare/speedtest';

/** The figures a speed test's results report, as the speed test library names them (speeds in bits per second). */
export interface TestSummary {
    readonly download?: number;
    readonly upload?: number;
    readonly latency?: number;
    readonly jitter?: number;
    readonly downLoadedLatency?: number;
    readonly upLoadedLatency?: number;
}

/** The results the library builds up while a test runs. */
export interface TestResults {
    getSummary(): TestSummary;
}

/** One measurement step, as the library takes them: latency pings, or downloads/uploads of a given size. */
export type TestStep =
    | { readonly type: 'latency'; readonly numPackets: number }
    | { readonly type: 'download' | 'upload'; readonly bytes: number; readonly count: number; readonly bypassMinDuration?: boolean };

/** The settings the engine gives the library. */
export interface TestSettings {
    readonly autoStart: false;
    readonly measurementTargets: string[];
    readonly measurements: TestStep[];
    readonly logAimApiUrl: null;
    readonly logMeasurementApiUrl: null;
    readonly abortTestDuration: number;
}

/** What a running test tells the engine: a step starting, new figures, and the end of the test. */
export interface TestListeners {
    stepStarted(stepIndex: number): void;
    resultsChanged(results: TestResults): void;
    finished(results: TestResults): void;
}

/** A test from the speed test library (@cloudflare/speedtest), reduced to what the engine uses. */
export interface RunningTest {
    start(listeners: TestListeners): void;
    pause(): void;
}

/** Creates a test with the given settings; tests pass a pretend one. */
export type RunningTestFactory = (settings: TestSettings) => RunningTest;

/** "full" measures with requests of up to 25 MB; "quick" stops at 10 MB, using less data (e.g. on mobile data). */
export type SpeedTestMode = 'full' | 'quick';

/** A finished test's figures. Only the download speed is certain; the others are null when they couldn't be measured. */
export interface SpeedMeasurement {
    readonly downloadMbps: number;
    readonly uploadMbps: number | null;
    /** How long a tiny request takes to go there and back while the connection is idle. */
    readonly latencyMs: number | null;
    /** How much that time varies from one request to the next. */
    readonly jitterMs: number | null;
    /** The same time while the connection is busy downloading, and while it is busy uploading. */
    readonly downloadLoadedLatencyMs: number | null;
    readonly uploadLoadedLatencyMs: number | null;
}

/** The figures so far while a test runs, and how far through its steps it is. */
export interface SpeedTestProgress {
    readonly downloadMbps: number | null;
    readonly uploadMbps: number | null;
    readonly latencyMs: number | null;
    readonly stepsDone: number;
    readonly stepsTotal: number;
}

/** The answer: the figures, a test that measured nothing usable, or a test the visitor stopped. */
export type SpeedTestOutcome =
    | { readonly outcome: 'finished'; readonly measurement: SpeedMeasurement }
    | { readonly outcome: 'failed' }
    | { readonly outcome: 'stopped' };

/**
 * Measures the visitor's connection speed, like timing how long it takes to fill buckets of different sizes.
 *
 * In plain terms, here is what happens:
 *   1. It prepares a test against Cloudflare's speed test servers, which are close to almost everyone. Cloudflare
 *      sees the visitor's address, as any speed test server does. The library's own reporting of results to
 *      Cloudflare is switched off.
 *   2. The test pings the server while the connection is idle, then downloads and uploads ever larger pieces of
 *      data, pinging again while the connection is busy. It moves on once a piece takes about a second, so a slow
 *      connection doesn't use much data.
 *   3. While it runs, it reports the figures so far.
 *   4. When it finishes, it turns the library's figures into megabits per second and milliseconds.
 * If the test measured no download speed (no connection, or the server couldn't be reached), it answers "failed".
 * A time limit makes sure it always ends. The visitor can stop it at any time.
 */
export class SpeedTestEngine {
    /** Cloudflare's speed test servers, which allow any website to time requests to them. */
    public static readonly defaultServer = 'https://speed.cloudflare.com';

    private stopRunning: (() => void) | null = null;

    /**
     * @param createTest creates the library's test; tests pass a pretend one
     * @param server the speed test server's address
     */
    constructor(
        private readonly createTest: RunningTestFactory = openCloudflareTest,
        private readonly server = SpeedTestEngine.defaultServer,
    ) {}

    /** The measurement steps for a mode: latency, then growing downloads and uploads (see step 2). */
    public static stepsFor(mode: SpeedTestMode): TestStep[] {
        const steps: TestStep[] = [
            { type: 'latency', numPackets: 1 },
            { type: 'download', bytes: 1e5, count: 1, bypassMinDuration: true },
            { type: 'latency', numPackets: 20 },
            { type: 'download', bytes: 1e5, count: 9 },
            { type: 'download', bytes: 1e6, count: 8 },
            { type: 'upload', bytes: 1e5, count: 8 },
            { type: 'upload', bytes: 1e6, count: 6 },
            { type: 'download', bytes: 1e7, count: 6 },
            { type: 'upload', bytes: 1e7, count: 4 },
        ];
        // Cloudflare refuses single downloads above 25 MB, so the full test stops there.
        if (mode === 'full') steps.push({ type: 'download', bytes: 25e6, count: 4 }, { type: 'upload', bytes: 25e6, count: 4 });
        return steps;
    }

    /** Steps 1–4: Runs one test and returns its outcome. "onProgress" hears the figures so far. */
    public run(mode: SpeedTestMode, onProgress: (progress: SpeedTestProgress) => void = () => {}): Promise<SpeedTestOutcome> {
        if (this.stopRunning) throw new Error('A speed test is already running.');
        // Step 1
        const steps = SpeedTestEngine.stepsFor(mode);
        const test = this.createTest({
            autoStart: false,
            measurementTargets: [this.server],
            measurements: steps,
            logAimApiUrl: null,
            logMeasurementApiUrl: null,
            // The full test usually takes under 30 seconds; the limit ends a stuck test with what it has so far.
            abortTestDuration: mode === 'full' ? 60_000 : 30_000,
        });
        return new Promise<SpeedTestOutcome>(resolve => {
            const finish = (outcome: SpeedTestOutcome): void => {
                this.stopRunning = null;
                resolve(outcome);
            };
            this.stopRunning = () => {
                test.pause();
                finish({ outcome: 'stopped' });
            };
            let stepsDone = 0;
            // Step 2: Starting the test runs its steps one after another.
            test.start({
                // Step 3
                stepStarted: stepIndex => { stepsDone = stepIndex; },
                resultsChanged: results => onProgress(progressFrom(results.getSummary(), stepsDone, steps.length)),
                // Step 4
                finished: results => {
                    if (!this.stopRunning) return;
                    const measurement = measurementFrom(results.getSummary());
                    finish(measurement ? { outcome: 'finished', measurement } : { outcome: 'failed' });
                },
            });
        });
    }

    /** Stops the running test, which then answers "stopped". Does nothing when no test is running. */
    public stop(): void {
        this.stopRunning?.();
    }
}

// ───── Helpers for reading the library's figures (steps 3 and 4) ─────

function progressFrom(summary: TestSummary, stepsDone: number, stepsTotal: number): SpeedTestProgress {
    return {
        downloadMbps: toMbps(summary.download),
        uploadMbps: toMbps(summary.upload),
        latencyMs: valueOrNull(summary.latency),
        stepsDone,
        stepsTotal,
    };
}

/** The finished figures, or null when there is no download speed to report. */
function measurementFrom(summary: TestSummary): SpeedMeasurement | null {
    const downloadMbps = toMbps(summary.download);
    if (downloadMbps === null || downloadMbps <= 0) return null;
    return {
        downloadMbps,
        uploadMbps: toMbps(summary.upload),
        latencyMs: valueOrNull(summary.latency),
        jitterMs: valueOrNull(summary.jitter),
        downloadLoadedLatencyMs: loadedLatency(summary.downLoadedLatency),
        uploadLoadedLatencyMs: loadedLatency(summary.upLoadedLatency),
    };
}

/**
 * A latency measured while the connection was busy, or null when there was none: the library reports 0 when its
 * requests were too short to load the connection (e.g. the quick test on a fast line).
 */
function loadedLatency(milliseconds: number | undefined): number | null {
    const value = valueOrNull(milliseconds);
    return value !== null && value > 0 ? value : null;
}

/** Bits per second to megabits per second. */
function toMbps(bitsPerSecond: number | undefined): number | null {
    const value = valueOrNull(bitsPerSecond);
    return value === null ? null : value / 1e6;
}

function valueOrNull(value: number | undefined): number | null {
    return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

/** Creates the real test from the speed test library. */
function openCloudflareTest(settings: TestSettings): RunningTest {
    const test = new CloudflareSpeedTest({ ...settings, measurements: [...settings.measurements] });
    return {
        start: listeners => {
            test.onPhaseChange = ({ measurementId }) => listeners.stepStarted(measurementId);
            test.onResultsChange = () => listeners.resultsChanged(test.results);
            test.onFinish = results => listeners.finished(results);
            // Most errors (one failed request) don't stop the library's test; a test that measured nothing fails.
            test.onError = () => {};
            test.play();
        },
        pause: () => test.pause(),
    };
}
