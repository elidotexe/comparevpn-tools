import type { SpeedMeasurement, SpeedTestMode } from './SpeedTestEngine';

/** The network a test ran on, without the address itself: enough to keep home, mobile and VPN runs apart. */
export interface RunConnection {
    readonly asn: number | null;
    readonly networkName: string | null;
    readonly countryCode: string | null;
    readonly isHosting: boolean;
}

/** For a run that is half of a with/without VPN comparison: which half, and which VPN and server it used. */
export interface VpnRunDetails {
    /** The same for both runs of one comparison. */
    readonly pairId: string;
    readonly role: 'without-vpn' | 'with-vpn';
    readonly vpnName: string | null;
    readonly server: string | null;
    readonly protocol: string | null;
}

/** One saved test. */
export interface SpeedRun {
    readonly id: string;
    /** When the test finished, as an ISO date and time. */
    readonly testedAt: string;
    readonly mode: SpeedTestMode;
    readonly measurement: SpeedMeasurement;
    readonly connection: RunConnection | null;
    readonly note: string | null;
    readonly vpn: VpnRunDetails | null;
}

/** The figures a history can summarise. */
export type RunMetric = 'downloadMbps' | 'uploadMbps' | 'latencyMs';

/** A figure across several runs. "Best" is the highest speed or the lowest latency. */
export interface MetricSummary {
    readonly count: number;
    readonly median: number;
    readonly best: number;
    readonly worst: number;
    /** How far a typical run is from the median, as a percentage of it: small means a steady connection. */
    readonly variationPercent: number;
}

/**
 * A visitor's past speed tests, and what they add up to, like a logbook with a summary at the bottom.
 *
 * In plain terms, here is what happens:
 *   1. It lists the runs newest first.
 *   2. It can narrow them to one network, so home and phone runs aren't mixed.
 *   3. For download, upload or latency, it works out the typical value (the median), the best, the worst and how
 *      steady the connection is.
 *   4. It writes the runs out as a spreadsheet file (CSV).
 * Runs that didn't measure a figure are left out of that figure's summary.
 */
export class RunHistory {
    private readonly runs: readonly SpeedRun[];

    constructor(runs: readonly SpeedRun[]) {
        this.runs = [...runs].sort((a, b) => b.testedAt.localeCompare(a.testedAt));
    }

    /** Step 1: Every run, newest first. */
    public latestFirst(): readonly SpeedRun[] {
        return this.runs;
    }

    /** Step 2: Only the runs on network "asn" (null: runs whose network wasn't known). */
    public forNetwork(asn: number | null): RunHistory {
        return new RunHistory(this.runs.filter(run => (run.connection?.asn ?? null) === asn));
    }

    /** Step 2: The networks in this history, the most used first, with a name and how many runs each has. */
    public networks(): { asn: number | null; name: string | null; runs: number }[] {
        const byNetwork = new Map<number | null, { asn: number | null; name: string | null; runs: number }>();
        for (const run of this.runs) {
            const asn = run.connection?.asn ?? null;
            const entry = byNetwork.get(asn) ?? { asn, name: run.connection?.networkName ?? null, runs: 0 };
            entry.runs++;
            byNetwork.set(asn, entry);
        }
        return [...byNetwork.values()].sort((a, b) => b.runs - a.runs);
    }

    /** Step 3: The summary of one figure, or null when no run measured it. */
    public summary(metric: RunMetric): MetricSummary | null {
        const values = this.runs.map(run => run.measurement[metric]).filter((value): value is number => value !== null);
        if (values.length === 0) return null;
        const middle = median(values);
        const lowerIsBetter = metric === 'latencyMs';
        return {
            count: values.length,
            median: middle,
            best: lowerIsBetter ? Math.min(...values) : Math.max(...values),
            worst: lowerIsBetter ? Math.max(...values) : Math.min(...values),
            // The median distance from the median: one unusual run doesn't make a steady connection look unsteady.
            variationPercent: middle === 0 ? 0 : (median(values.map(value => Math.abs(value - middle))) / middle) * 100,
        };
    }

    /** Step 4: The runs, newest first, as CSV text with a header row. */
    public toCsv(): string {
        const header = [
            'tested_at', 'mode', 'download_mbps', 'upload_mbps', 'latency_ms', 'jitter_ms', 'download_loaded_latency_ms',
            'upload_loaded_latency_ms', 'network', 'asn', 'country', 'hosting_network', 'vpn_test', 'vpn', 'vpn_server',
            'vpn_protocol', 'note',
        ];
        const rows = this.runs.map(run => {
            const m = run.measurement;
            return [
                run.testedAt, run.mode, m.downloadMbps, m.uploadMbps, m.latencyMs, m.jitterMs, m.downloadLoadedLatencyMs,
                m.uploadLoadedLatencyMs, run.connection?.networkName, run.connection?.asn, run.connection?.countryCode,
                run.connection ? (run.connection.isHosting ? 'yes' : 'no') : null, run.vpn?.role, run.vpn?.vpnName,
                run.vpn?.server, run.vpn?.protocol, run.note,
            ];
        });
        return [header, ...rows].map(row => row.map(csvField).join(',')).join('\r\n') + '\r\n';
    }
}

// ───── Helpers for the summary and the file (steps 3 and 4) ─────

function median(values: readonly number[]): number {
    const sorted = [...values].sort((a, b) => a - b);
    const middle = Math.floor(sorted.length / 2);
    return sorted.length % 2 === 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
}

/**
 * One value as a CSV field: empty when missing, numbers rounded to two decimals, and text in quotes when it holds
 * a comma, quote or line break. Text starting with = + - or @ gets a leading apostrophe, so a spreadsheet shows it
 * instead of running it as a formula (a note is typed by the visitor).
 */
function csvField(value: string | number | null | undefined): string {
    if (value === null || value === undefined) return '';
    if (typeof value === 'number') return String(Math.round(value * 100) / 100);
    const text = /^[=+\-@]/.test(value) ? `'${value}` : value;
    return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}
