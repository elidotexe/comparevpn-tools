/** What kind of address a candidate shows: one on the internet, one inside a home or office network, or none. */
export type AddressKind = 'public' | 'local' | 'hidden';

/**
 * How the browser found the address: on the device itself ("host"), as seen by the address server ("srflx" and
 * "prflx"), or through a relay server ("relay").
 */
export type CandidateType = 'host' | 'srflx' | 'prflx' | 'relay';

const candidateTypes: readonly CandidateType[] = ['host', 'srflx', 'prflx', 'relay'];

function isCandidateType(text: string): text is CandidateType {
    return candidateTypes.some((type) => type === text);
}

/**
 * One address the browser offers when it prepares a WebRTC connection (an "ICE candidate"), read from the line of
 * text the browser reports. WebRTC is the browser feature behind video calls; to set up a call, the browser
 * lists every address another computer could reach it on, and any website can ask for that list.
 *
 * In plain terms, here is what happens:
 *   1. It reads the address and how it was found from the browser's line of text.
 *   2. It says whether the address is public (visible on the internet, the one a leak test looks for), local
 *      (inside a home or office network) or hidden (the browser shows a random stand-in name instead).
 * A line it doesn't recognise is skipped rather than guessed at.
 */
export class IceCandidate {
    constructor(
        public readonly address: string,
        public readonly type: CandidateType,
    ) {}

    /**
     * Reads a candidate line such as "candidate:1 1 udp 1677729535 203.0.113.5 46154 typ srflx ...". Returns
     * null for an empty, cut-off or unknown line.
     */
    public static parse(line: string): IceCandidate | null {
        // Fields: foundation, component, protocol, priority, address, port, "typ", type, then optional extras.
        const fields = line.trim().replace(/^a=/, '').replace(/^candidate:/, '').split(/\s+/);
        if (fields.length < 8 || fields[6] !== 'typ') return null;
        const [address, type] = [fields[4], fields[7]];
        return isCandidateType(type) ? new IceCandidate(address.toLowerCase(), type) : null;
    }

    /** Whether this candidate's address is public, local or hidden. */
    public addressKind(): AddressKind {
        return IceCandidate.kindOf(this.address);
    }

    /**
     * Whether an address is public, local or hidden. Anything that isn't a valid IPv4 or IPv6 address (such as the
     * browser's random "….local" names) counts as hidden, never as public.
     */
    public static kindOf(address: string): AddressKind {
        const ipv4 = readIpv4(address);
        if (ipv4) return isLocalIpv4(ipv4) ? 'local' : 'public';
        if (looksLikeIpv6(address)) return isLocalIpv6(address.toLowerCase()) ? 'local' : 'public';
        return 'hidden';
    }
}

// ───── Helpers for telling local addresses from public ones (step 2) ─────

/** The four numbers of an IPv4 address, or null when it isn't one. */
function readIpv4(address: string): number[] | null {
    const parts = address.split('.');
    if (parts.length !== 4 || !parts.every((part) => /^\d{1,3}$/.test(part))) return null;
    const numbers = parts.map(Number);
    return numbers.every((number) => number <= 255) ? numbers : null;
}

/**
 * Ranges that never appear on the internet: "this network" (0.x), private networks (10.x, 172.16–31.x, 192.168.x),
 * the device itself (127.x), self-assigned addresses (169.254.x) and the addresses providers share between
 * customers behind one public address (100.64–127.x).
 */
function isLocalIpv4([first, second]: number[]): boolean {
    return (
        first === 0 ||
        first === 10 ||
        first === 127 ||
        (first === 169 && second === 254) ||
        (first === 172 && second >= 16 && second <= 31) ||
        (first === 192 && second === 168) ||
        (first === 100 && second >= 64 && second <= 127)
    );
}

function looksLikeIpv6(address: string): boolean {
    return address.includes(':') && /^[0-9a-f:.]+$/i.test(address);
}

/**
 * The device itself (::1), "no address" (::), private networks (fc00::/7, starting fc or fd), link-only
 * addresses (fe80::/10, starting fe8–feb), and IPv4 addresses written in IPv6 form (::ffff:a.b.c.d), which follow
 * the IPv4 rules.
 */
function isLocalIpv6(address: string): boolean {
    if (address === '::1' || address === '::') return true;
    const mappedIpv4 = address.startsWith('::ffff:') ? readIpv4(address.slice('::ffff:'.length)) : null;
    if (mappedIpv4) return isLocalIpv4(mappedIpv4);
    return /^f[cd]/.test(address) || /^fe[89ab]/.test(address);
}
