import { describe, expect, it } from 'vitest';
import { RunHistory } from './RunHistory';
import type { SpeedRun } from './RunHistory';
import type { SpeedMeasurement } from './SpeedTestEngine';

const comcast = { asn: 7922, networkName: 'Comcast Xfinity', countryCode: 'US', isHosting: false };
const tmobile = { asn: 21928, networkName: 'T-Mobile', countryCode: 'US', isHosting: false };

function run(id: string, testedAt: string, figures: Partial<SpeedMeasurement>, extra: Partial<SpeedRun> = {}): SpeedRun {
    return {
        id, testedAt, mode: 'full', connection: comcast, note: null, vpn: null,
        measurement: { downloadMbps: 100, uploadMbps: 20, latencyMs: 15, jitterMs: 2, downloadLoadedLatencyMs: 30, uploadLoadedLatencyMs: 20, ...figures },
        ...extra,
    };
}

describe('RunHistory', () => {
    it('lists runs newest first, whatever order they were given in', () => {
        const history = new RunHistory([run('a', '2026-11-20T10:00:00Z', {}), run('c', '2026-11-22T10:00:00Z', {}), run('b', '2026-11-21T10:00:00Z', {})]);
        expect(history.latestFirst().map(r => r.id)).toEqual(['c', 'b', 'a']);
    });

    it('narrows to one network and lists the networks, the most used first', () => {
        const history = new RunHistory([
            run('home1', '2026-11-20T10:00:00Z', {}),
            run('phone', '2026-11-21T10:00:00Z', {}, { connection: tmobile }),
            run('home2', '2026-11-22T10:00:00Z', {}),
            run('unknown', '2026-11-22T11:00:00Z', {}, { connection: null }),
        ]);
        expect(history.forNetwork(7922).latestFirst().map(r => r.id)).toEqual(['home2', 'home1']);
        expect(history.forNetwork(null).latestFirst().map(r => r.id)).toEqual(['unknown']);
        expect(history.networks()).toEqual([
            { asn: 7922, name: 'Comcast Xfinity', runs: 2 },
            { asn: null, name: null, runs: 1 },
            { asn: 21928, name: 'T-Mobile', runs: 1 },
        ]);
    });

    it('summarises speeds with the median, best, worst and how steady they are', () => {
        const history = new RunHistory([
            run('1', '2026-11-18T10:00:00Z', { downloadMbps: 400 }),
            run('2', '2026-11-19T10:00:00Z', { downloadMbps: 460 }),
            run('3', '2026-11-20T10:00:00Z', { downloadMbps: 470 }),
            run('4', '2026-11-21T10:00:00Z', { downloadMbps: 480 }),
            run('5', '2026-11-22T10:00:00Z', { downloadMbps: 200 }),
        ]);
        // Distances from the median (460): 60, 0, 10, 20, 260 → their median is 20, which is 4.3% of 460.
        const summary = history.summary('downloadMbps');
        expect(summary).toMatchObject({ count: 5, median: 460, best: 480, worst: 200 });
        expect(summary?.variationPercent).toBeCloseTo(4.35, 1);
    });

    it('takes the middle two values for an even number of runs', () => {
        const history = new RunHistory([run('1', '2026-11-20T10:00:00Z', { downloadMbps: 100 }), run('2', '2026-11-21T10:00:00Z', { downloadMbps: 200 })]);
        expect(history.summary('downloadMbps')?.median).toBe(150);
    });

    it('calls the lowest latency the best', () => {
        const history = new RunHistory([run('1', '2026-11-20T10:00:00Z', { latencyMs: 12 }), run('2', '2026-11-21T10:00:00Z', { latencyMs: 40 })]);
        expect(history.summary('latencyMs')).toMatchObject({ best: 12, worst: 40 });
    });

    it('leaves runs that did not measure a figure out of its summary, and has none without runs', () => {
        const history = new RunHistory([run('1', '2026-11-20T10:00:00Z', { uploadMbps: null }), run('2', '2026-11-21T10:00:00Z', { uploadMbps: 30 })]);
        expect(history.summary('uploadMbps')).toMatchObject({ count: 1, median: 30 });
        expect(new RunHistory([]).summary('downloadMbps')).toBeNull();
    });

    it('writes a CSV file with a header, rounded numbers and empty fields for missing figures', () => {
        const history = new RunHistory([
            run('1', '2026-11-22T10:00:00Z', { downloadMbps: 471.123, uploadMbps: null }, {
                vpn: { pairId: 'p', role: 'with-vpn', vpnName: 'Mullvad', server: 'Amsterdam', protocol: 'WireGuard' },
            }),
        ]);
        const [header, row, end] = history.toCsv().split('\r\n');
        expect(header).toBe('tested_at,mode,download_mbps,upload_mbps,latency_ms,jitter_ms,download_loaded_latency_ms,upload_loaded_latency_ms,network,asn,country,hosting_network,vpn_test,vpn,vpn_server,vpn_protocol,note');
        expect(row).toBe('2026-11-22T10:00:00Z,full,471.12,,15,2,30,20,Comcast Xfinity,7922,US,no,with-vpn,Mullvad,Amsterdam,WireGuard,');
        expect(end).toBe('');
    });

    it('quotes notes with commas or quotes, and stops a spreadsheet running a note as a formula', () => {
        const csvNote = (note: string) => new RunHistory([run('1', '2026-11-22T10:00:00Z', {}, { note })]).toCsv().split('\r\n')[1].split(',US,no,,,,,')[1];
        expect(csvNote('Wi-Fi, upstairs')).toBe('"Wi-Fi, upstairs"');
        expect(csvNote('the "fast" router')).toBe('"the ""fast"" router"');
        expect(csvNote('=HYPERLINK("x")')).toBe(`"'=HYPERLINK(""x"")"`);
        expect(csvNote('-5 dB')).toBe("'-5 dB");
    });
});
