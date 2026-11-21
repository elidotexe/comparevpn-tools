import { describe, expect, it } from 'vitest';
import { IceCandidate } from './IceCandidate';

// Candidate lines as browsers report them (addresses from the documentation ranges).
const publicIpv4 = 'candidate:842163049 1 udp 1677729535 203.0.113.5 46154 typ srflx raddr 0.0.0.0 rport 0 generation 0 ufrag sXdT network-cost 999';
const publicIpv6 = 'candidate:1 1 UDP 1686052863 2001:DB8::1234 51000 typ srflx raddr :: rport 0';
const hiddenLocal = 'candidate:3094207393 1 udp 2113937151 9b36eaac-bb2e-49bb-bb78-21c41c499900.local 59353 typ host generation 0 ufrag sXdT network-cost 999';
const privateLocal = 'candidate:0 1 UDP 2122252543 192.168.1.20 52734 typ host';
const relayed = 'a=candidate:2 1 udp 41885439 198.51.100.7 3478 typ relay raddr 203.0.113.5 rport 46154';

describe('reading a candidate line', () => {
    it('reads the address and type of a public address found by the address server', () => {
        const candidate = IceCandidate.parse(publicIpv4);
        expect(candidate?.address).toBe('203.0.113.5');
        expect(candidate?.type).toBe('srflx');
        expect(candidate?.addressKind()).toBe('public');
    });

    it('reads IPv6 addresses, written in lower case so the same address is only listed once', () => {
        const candidate = IceCandidate.parse(publicIpv6);
        expect(candidate?.address).toBe('2001:db8::1234');
        expect(candidate?.addressKind()).toBe('public');
    });

    it("recognises the browser's random stand-in name for a hidden local address", () => {
        expect(IceCandidate.parse(hiddenLocal)?.addressKind()).toBe('hidden');
    });

    it('recognises a local network address', () => {
        expect(IceCandidate.parse(privateLocal)?.addressKind()).toBe('local');
    });

    it('accepts the "a=" prefix some browsers keep, and reads relayed candidates', () => {
        const candidate = IceCandidate.parse(relayed);
        expect(candidate?.address).toBe('198.51.100.7');
        expect(candidate?.type).toBe('relay');
    });

    it('ignores empty, cut-off or unknown lines instead of guessing', () => {
        expect(IceCandidate.parse('')).toBeNull();
        expect(IceCandidate.parse('candidate:1 1 udp 1 203.0.113.5')).toBeNull();
        expect(IceCandidate.parse('candidate:1 1 udp 1 203.0.113.5 1 typ something')).toBeNull();
        expect(IceCandidate.parse('not a candidate at all')).toBeNull();
    });
});

describe('telling local addresses from public ones', () => {
    it.each([
        '10.1.2.3', '172.16.0.1', '172.31.255.255', '192.168.0.1', '127.0.0.1', '169.254.10.20', '100.64.0.1',
        '100.127.255.254', '0.0.0.0', '::1', '::', 'fe80::1', 'fd12:3456::1', 'fc00::1', '::ffff:192.168.1.1',
    ])('%s is local', (address) => {
        expect(IceCandidate.kindOf(address)).toBe('local');
    });

    it.each(['8.8.8.8', '172.32.0.1', '100.128.0.1', '203.0.113.5', '2001:4860:4860::8888', '::ffff:8.8.8.8'])(
        '%s is public',
        (address) => {
            expect(IceCandidate.kindOf(address)).toBe('public');
        },
    );

    it('treats anything that is not an address as hidden, never as public', () => {
        expect(IceCandidate.kindOf('abc.local')).toBe('hidden');
        expect(IceCandidate.kindOf('999.1.1.1')).toBe('hidden');
    });
});
