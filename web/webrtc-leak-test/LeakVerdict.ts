import type { IceCandidate } from './IceCandidate';
import type { ProbeResult } from './WebRtcProbe';

/**
 * What a lookup found out about an address: the number of the network it belongs to (null when unknown) and
 * whether that network is a hosting, cloud or VPN company rather than a home or mobile provider.
 */
export interface AddressNetwork {
    readonly address: string;
    readonly asn: number | null;
    readonly isHosting: boolean;
}

/** The answer: WebRTC reveals another connection, it doesn't, or the browser has WebRTC turned off. */
export type LeakOutcome = 'leak' | 'no-leak' | 'webrtc-blocked';

/**
 * Decides whether WebRTC leaks a different connection from the one websites normally see, like comparing the
 * return address on an envelope with the one printed on the letter inside.
 *
 * In plain terms, here is what happens:
 *   1. It lists the public addresses WebRTC revealed that differ from the address websites see, so they can be
 *      looked up.
 *   2. With those lookups, it counts an address as a leak unless it belongs to the same network as the address
 *      websites see. That way a home connection with both an IPv4 and an IPv6 address from one provider isn't a
 *      leak, but a real IPv6 address showing next to a VPN's IPv4 address is.
 *   3. It notes local network addresses the browser shows openly (most browsers hide them behind a random name)
 *      and whether the address websites see looks like a VPN.
 * An address that couldn't be looked up still counts as a leak: it is a second address all the same. If the
 * browser has WebRTC turned off, nothing can leak through it.
 */
export class LeakVerdict {
    private constructor(
        public readonly outcome: LeakOutcome,
        public readonly leakedAddresses: readonly AddressNetwork[],
        public readonly visibleLocalAddresses: readonly string[],
        /** Whether the address websites see belongs to a hosting, cloud or VPN network. */
        public readonly vpnLikely: boolean,
        /** Whether WebRTC revealed any public address at all (false when the address server couldn't be reached). */
        public readonly foundPublicAddress: boolean,
    ) {}

    /** Every public address WebRTC revealed, each once, in the order the browser reported them. */
    public static revealedAddresses(probe: ProbeResult): string[] {
        return publicAddresses(probe);
    }

    /** Step 1: The public addresses WebRTC revealed, each once, apart from "seenAddress" (the one websites see). */
    public static addressesToLookUp(seenAddress: string, probe: ProbeResult): string[] {
        return publicAddresses(probe).filter((address) => address !== seenAddress.toLowerCase());
    }

    /**
     * Steps 2–3: Decides the answer.
     * @param seen the address websites see, with its network
     * @param probe what WebRTC revealed
     * @param lookups the networks of the addresses from addressesToLookUp (missing ones count as unknown)
     */
    public static decide(seen: AddressNetwork, probe: ProbeResult, lookups: readonly AddressNetwork[]): LeakVerdict {
        if (probe.outcome === 'unavailable') return new LeakVerdict('webrtc-blocked', [], [], seen.isHosting, false);
        // Step 2
        const leaked = LeakVerdict.addressesToLookUp(seen.address, probe)
            .map((address) => lookups.find((lookup) => lookup.address.toLowerCase() === address) ?? unknownNetwork(address))
            .filter((other) => !sameNetwork(seen, other));
        // Step 3
        const local = probe.candidates.filter((candidate) => candidate.addressKind() === 'local').map((candidate) => candidate.address);
        return new LeakVerdict(
            leaked.length > 0 ? 'leak' : 'no-leak',
            leaked,
            unique(local),
            seen.isHosting,
            publicAddresses(probe).length > 0,
        );
    }
}

// ───── Helpers for comparing addresses (steps 1–3) ─────

/**
 * Every public address WebRTC revealed, each once. Relay candidates are skipped: they show a relay server's
 * address, not the visitor's.
 */
function publicAddresses(probe: ProbeResult): string[] {
    if (probe.outcome === 'unavailable') return [];
    const revealed = probe.candidates.filter((candidate: IceCandidate) => candidate.type !== 'relay' && candidate.addressKind() === 'public');
    return unique(revealed.map((candidate) => candidate.address));
}

/** Two addresses are on the same network only when both networks are known and have the same number. */
function sameNetwork(seen: AddressNetwork, other: AddressNetwork): boolean {
    return seen.asn !== null && seen.asn === other.asn;
}

function unknownNetwork(address: string): AddressNetwork {
    return { address, asn: null, isHosting: false };
}

function unique(addresses: string[]): string[] {
    return [...new Set(addresses)];
}
