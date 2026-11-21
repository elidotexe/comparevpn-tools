import { afterEach, describe, expect, it, vi } from 'vitest';
import { WebRtcProbe } from './WebRtcProbe';
import type { CandidateEvent, PeerConnection } from './WebRtcProbe';

/** A pretend browser connection: the test decides which candidate lines it reports, and when. */
class FakeConnection implements PeerConnection {
    public iceGatheringState = 'new';
    public closed = false;
    public dataChannels: string[] = [];
    private readonly candidateListeners: ((event: CandidateEvent) => void)[] = [];
    private readonly stateListeners: (() => void)[] = [];

    constructor(private readonly failOffer = false) {}

    public addEventListener(type: 'icecandidate', listener: (event: CandidateEvent) => void): void;
    public addEventListener(type: 'icegatheringstatechange', listener: () => void): void;
    public addEventListener(type: string, listener: (event: CandidateEvent) => void): void {
        // A state listener ignores what it is given, so it is called with an empty event.
        if (type === 'icecandidate') this.candidateListeners.push(listener);
        else this.stateListeners.push(() => listener({ candidate: null }));
    }

    public createDataChannel(label: string): unknown {
        this.dataChannels.push(label);
        return {};
    }

    public createOffer(): Promise<RTCSessionDescriptionInit> {
        return this.failOffer ? Promise.reject(new Error('blocked by an extension')) : Promise.resolve({ type: 'offer', sdp: '' });
    }

    public setLocalDescription(): Promise<void> {
        return Promise.resolve();
    }

    public close(): void {
        this.closed = true;
    }

    /** Reports a candidate line, or the end of the list when "line" is null. */
    public report(line: string | null): void {
        for (const listener of this.candidateListeners) listener({ candidate: line === null ? null : { candidate: line } });
    }

    public finishGathering(): void {
        this.iceGatheringState = 'complete';
        for (const listener of this.stateListeners) listener();
    }
}

const publicLine = 'candidate:1 1 udp 1677729535 203.0.113.5 46154 typ srflx raddr 0.0.0.0 rport 0';
const hiddenLine = 'candidate:2 1 udp 2113937151 9b36eaac-bb2e-49bb-bb78-21c41c499900.local 59353 typ host';

/** Lets the probe's start-up promises (offer, local description) settle. */
const settle = () => new Promise<void>((resolve) => setTimeout(resolve, 0));

afterEach(() => {
    vi.useRealTimers();
});

describe('WebRTC probe', () => {
    it('asks the given address server, and collects each address the browser reports once', async () => {
        const connection = new FakeConnection();
        const factory = vi.fn(() => connection);
        const running = new WebRtcProbe(['stun:stun.example.org:3478'], factory).run();
        await settle();
        connection.report(publicLine);
        connection.report(hiddenLine);
        connection.report(publicLine);
        connection.report(null);

        const result = await running;
        expect(factory).toHaveBeenCalledWith({ iceServers: [{ urls: ['stun:stun.example.org:3478'] }] });
        expect(connection.dataChannels).toHaveLength(1);
        expect(result.outcome).toBe('finished');
        expect(result.outcome === 'finished' && result.candidates.map((candidate) => candidate.address))
            .toEqual(['203.0.113.5', '9b36eaac-bb2e-49bb-bb78-21c41c499900.local']);
        expect(result.outcome === 'finished' && result.timedOut).toBe(false);
        expect(connection.closed).toBe(true);
    });

    it('also stops when the browser says it has finished gathering', async () => {
        const connection = new FakeConnection();
        const running = new WebRtcProbe(undefined, () => connection).run();
        await settle();
        connection.report(publicLine);
        connection.finishGathering();
        const result = await running;
        expect(result.outcome === 'finished' && result.candidates).toHaveLength(1);
    });

    it('skips lines it cannot read, such as the empty line some browsers send at the end', async () => {
        const connection = new FakeConnection();
        const running = new WebRtcProbe(undefined, () => connection).run();
        await settle();
        connection.report('');
        connection.report('garbage');
        connection.report(null);
        const result = await running;
        expect(result.outcome === 'finished' && result.candidates).toEqual([]);
    });

    it('gives up waiting after the time limit and keeps what it found', async () => {
        vi.useFakeTimers();
        const connection = new FakeConnection();
        const running = new WebRtcProbe(undefined, () => connection, 5000).run();
        await vi.advanceTimersByTimeAsync(0);
        connection.report(publicLine);
        await vi.advanceTimersByTimeAsync(5000);
        const result = await running;
        expect(result.outcome === 'finished' && result.timedOut).toBe(true);
        expect(result.outcome === 'finished' && result.candidates).toHaveLength(1);
        expect(connection.closed).toBe(true);
    });

    it('reports WebRTC as unavailable when the browser has it turned off', async () => {
        expect((await new WebRtcProbe(undefined, () => null).run()).outcome).toBe('unavailable');
        const refusing = () => {
            throw new DOMException('WebRTC is disabled', 'NotSupportedError');
        };
        expect((await new WebRtcProbe(undefined, refusing).run()).outcome).toBe('unavailable');
    });

    it('reports WebRTC as unavailable when an extension or setting blocks the connection attempt', async () => {
        const connection = new FakeConnection(true);
        expect((await new WebRtcProbe(undefined, () => connection).run()).outcome).toBe('unavailable');
        expect(connection.closed).toBe(true);
    });

    it("uses Cloudflare's public address server unless told otherwise", () => {
        expect(WebRtcProbe.defaultServers).toEqual(['stun:stun.cloudflare.com:3478']);
    });
});
