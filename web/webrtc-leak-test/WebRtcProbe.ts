import { IceCandidate } from './IceCandidate';

/** What the browser reports for each address it finds; "candidate" is null once the list is complete. */
export interface CandidateEvent {
    readonly candidate: { readonly candidate: string } | null;
}

/** The parts of the browser's WebRTC connection (RTCPeerConnection) the probe uses. */
export interface PeerConnection {
    readonly iceGatheringState: string;
    addEventListener(type: 'icecandidate', listener: (event: CandidateEvent) => void): void;
    addEventListener(type: 'icegatheringstatechange', listener: () => void): void;
    createDataChannel(label: string): unknown;
    createOffer(): Promise<RTCSessionDescriptionInit>;
    setLocalDescription(description: RTCSessionDescriptionInit): Promise<void>;
    close(): void;
}

/** Opens a WebRTC connection with the given settings, or returns null when the browser has no WebRTC. */
export type PeerConnectionFactory = (settings: RTCConfiguration) => PeerConnection | null;

/** The probe's answer: WebRTC is unavailable, or the addresses it revealed (possibly none). */
export type ProbeResult =
    | { readonly outcome: 'unavailable' }
    | { readonly outcome: 'finished'; readonly candidates: readonly IceCandidate[]; readonly timedOut: boolean };

/**
 * Finds out which addresses the browser reveals through WebRTC, the browser feature behind video calls. Any
 * website can ask for these addresses without a permission prompt, which is how a VPN user's real address can
 * leak.
 *
 * In plain terms, here is what happens:
 *   1. It starts preparing a WebRTC connection that never connects to anyone and uses no camera or microphone.
 *   2. Preparing makes the browser list the addresses it could be reached on. To find its public address, the
 *      browser asks an address server (a "STUN server"), which replies with the address it saw. The server sees
 *      the visitor's address, so the one used is named in the settings: Cloudflare's public server by default.
 *   3. It collects each address once, until the browser says the list is complete or the time limit passes.
 *   4. It closes the connection and returns what it found.
 * If the browser has WebRTC turned off, or a setting or extension blocks it, it answers "unavailable": then
 * nothing can leak through WebRTC.
 */
export class WebRtcProbe {
    /** Cloudflare's free public address server, which needs no account. */
    public static readonly defaultServers: readonly string[] = ['stun:stun.cloudflare.com:3478'];

    /**
     * @param stunServers the address servers the browser asks for its public address
     * @param createConnection opens the browser's connection; tests pass a pretend one
     * @param timeLimitMs how long to wait for the browser to finish listing addresses
     */
    constructor(
        private readonly stunServers: readonly string[] = WebRtcProbe.defaultServers,
        private readonly createConnection: PeerConnectionFactory = openBrowserConnection,
        private readonly timeLimitMs = 5000,
    ) {}

    /** Steps 1–4: Returns the addresses the browser revealed, or "unavailable". */
    public async run(): Promise<ProbeResult> {
        // Step 1: A browser without WebRTC, or with it switched off, has nothing to reveal.
        const connection = this.open();
        if (!connection) return { outcome: 'unavailable' };
        try {
            // Steps 2 and 3: Listen first, so no address reported during set-up is missed.
            const gathering = this.collectCandidates(connection);
            if (await startGathering(connection)) return await gathering.result;
            gathering.stop();
            return { outcome: 'unavailable' };
        } finally {
            // Step 4
            connection.close();
        }
    }

    private open(): PeerConnection | null {
        try {
            return this.createConnection({ iceServers: [{ urls: [...this.stunServers] }] });
        } catch (error) {
            // Browsers that have WebRTC switched off by a setting or a policy refuse to open the connection.
            if (error instanceof DOMException) return null;
            throw error;
        }
    }

    /**
     * Step 3: "result" resolves with the addresses once the list is complete or the time limit passes; "stop"
     * stops waiting when the browser refused to start.
     */
    private collectCandidates(connection: PeerConnection): { result: Promise<ProbeResult>; stop(): void } {
        const found = new Map<string, IceCandidate>();
        let timer: ReturnType<typeof setTimeout> | undefined;
        const result = new Promise<ProbeResult>((resolve) => {
            const finish = (timedOut: boolean) => {
                clearTimeout(timer);
                resolve({ outcome: 'finished', candidates: [...found.values()], timedOut });
            };
            timer = setTimeout(() => finish(true), this.timeLimitMs);
            connection.addEventListener('icecandidate', (event) => {
                if (event.candidate === null) return finish(false);
                const candidate = IceCandidate.parse(event.candidate.candidate);
                // The same address can be reported more than once (e.g. for each network the device is on).
                if (candidate) found.set(`${candidate.type} ${candidate.address}`, candidate);
            });
            connection.addEventListener('icegatheringstatechange', () => {
                if (connection.iceGatheringState === 'complete') finish(false);
            });
        });
        return { result, stop: () => clearTimeout(timer) };
    }
}

// ───── Helpers for starting the connection (steps 1–2) ─────

/** Opens the browser's own WebRTC connection, or returns null when the browser has none. */
function openBrowserConnection(settings: RTCConfiguration): PeerConnection | null {
    return typeof RTCPeerConnection === 'undefined' ? null : new RTCPeerConnection(settings);
}

/**
 * Step 2: A data channel gives the connection something to set up, and setting the local description makes
 * the browser start listing addresses. Returns false when the browser or an extension refuses.
 */
async function startGathering(connection: PeerConnection): Promise<boolean> {
    try {
        connection.createDataChannel('leak-test');
        await connection.setLocalDescription(await connection.createOffer());
        return true;
    } catch {
        // Privacy extensions and some browser settings block this step on purpose: that is a normal answer
        // ("WebRTC is blocked"), not an error in the page.
        return false;
    }
}
