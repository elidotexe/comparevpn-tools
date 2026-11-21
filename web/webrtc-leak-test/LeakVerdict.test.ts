import { describe, expect, it } from 'vitest';
import { IceCandidate } from './IceCandidate';
import { LeakVerdict } from './LeakVerdict';
import type { AddressNetwork } from './LeakVerdict';
import type { ProbeResult } from './WebRtcProbe';

// What websites see: a VPN server's address on a hosting network.
const vpn: AddressNetwork = { address: '198.51.100.20', asn: 64500, isHosting: true };
// The visitor's real home connection, on IPv4 and IPv6 from the same provider.
const home: AddressNetwork = { address: '203.0.113.5', asn: 64496, isHosting: false };
const homeIpv6: AddressNetwork = { address: '2001:db8::1234', asn: 64496, isHosting: false };

function finished(...lines: string[]): ProbeResult {
    const candidates = lines.map((line) => IceCandidate.parse(line)).filter((candidate) => candidate !== null);
    return { outcome: 'finished', candidates, timedOut: false };
}

const publicLine = (address: string) => `candidate:1 1 udp 1677729535 ${address} 40000 typ srflx raddr 0.0.0.0 rport 0`;
const hostLine = (address: string) => `candidate:2 1 udp 2113937151 ${address} 50000 typ host`;

describe('which addresses need looking up', () => {
    it('lists each public address WebRTC revealed once, except the one websites already see', () => {
        const probe = finished(publicLine(vpn.address), publicLine(home.address), hostLine(home.address), hostLine('192.168.1.20'));
        expect(LeakVerdict.addressesToLookUp(vpn.address, probe)).toEqual([home.address]);
    });

    it('lists every public address WebRTC revealed, to show on the page', () => {
        const probe = finished(publicLine(vpn.address), publicLine(home.address), hostLine('192.168.1.20'), publicLine(vpn.address));
        expect(LeakVerdict.revealedAddresses(probe)).toEqual([vpn.address, home.address]);
        expect(LeakVerdict.revealedAddresses({ outcome: 'unavailable' })).toEqual([]);
    });

    it('has nothing to look up when WebRTC is unavailable', () => {
        expect(LeakVerdict.addressesToLookUp(vpn.address, { outcome: 'unavailable' })).toEqual([]);
    });
});

describe('the verdict', () => {
    it('finds no leak when WebRTC only shows the address websites already see', () => {
        const verdict = LeakVerdict.decide(vpn, finished(publicLine(vpn.address), hostLine('abc.local')), []);
        expect(verdict.outcome).toBe('no-leak');
        expect(verdict.leakedAddresses).toEqual([]);
        expect(verdict.vpnLikely).toBe(true);
        expect(verdict.foundPublicAddress).toBe(true);
    });

    it("finds a leak when WebRTC reveals the real connection's address behind a VPN", () => {
        const verdict = LeakVerdict.decide(vpn, finished(publicLine(vpn.address), publicLine(home.address)), [home]);
        expect(verdict.outcome).toBe('leak');
        expect(verdict.leakedAddresses).toEqual([home]);
    });

    it('finds a leak when the VPN covers IPv4 but WebRTC reveals the real IPv6 address', () => {
        const verdict = LeakVerdict.decide(vpn, finished(publicLine(homeIpv6.address)), [homeIpv6]);
        expect(verdict.outcome).toBe('leak');
        expect(verdict.leakedAddresses).toEqual([homeIpv6]);
    });

    it('finds no leak when the second address belongs to the same network, such as IPv4 and IPv6 at home', () => {
        const verdict = LeakVerdict.decide(home, finished(publicLine(home.address), publicLine(homeIpv6.address)), [homeIpv6]);
        expect(verdict.outcome).toBe('no-leak');
        expect(verdict.vpnLikely).toBe(false);
    });

    it("counts an address whose network couldn't be looked up as a leak, because it still differs", () => {
        const unknown: AddressNetwork = { address: home.address, asn: null, isHosting: false };
        expect(LeakVerdict.decide(vpn, finished(publicLine(home.address)), [unknown]).outcome).toBe('leak');
        // An address missing from the lookups altogether is treated the same way.
        expect(LeakVerdict.decide(vpn, finished(publicLine(home.address)), []).leakedAddresses)
            .toEqual([{ address: home.address, asn: null, isHosting: false }]);
    });

    it('does not count a network as the same when neither network is known', () => {
        const seenUnknown: AddressNetwork = { address: vpn.address, asn: null, isHosting: false };
        const otherUnknown: AddressNetwork = { address: home.address, asn: null, isHosting: false };
        expect(LeakVerdict.decide(seenUnknown, finished(publicLine(home.address)), [otherUnknown]).outcome).toBe('leak');
    });

    it('finds no leak when the address server could not be reached, and says no public address was found', () => {
        const verdict = LeakVerdict.decide(vpn, finished(hostLine('abc.local')), []);
        expect(verdict.outcome).toBe('no-leak');
        expect(verdict.foundPublicAddress).toBe(false);
    });

    it('says WebRTC is blocked when the browser has it turned off', () => {
        const verdict = LeakVerdict.decide(vpn, { outcome: 'unavailable' }, []);
        expect(verdict.outcome).toBe('webrtc-blocked');
        expect(verdict.leakedAddresses).toEqual([]);
    });

    it('lists local network addresses the browser shows instead of hiding them', () => {
        const verdict = LeakVerdict.decide(vpn, finished(publicLine(vpn.address), hostLine('192.168.1.20'), hostLine('abc.local')), []);
        expect(verdict.visibleLocalAddresses).toEqual(['192.168.1.20']);
    });

    it('ignores public addresses in local candidates that match what websites see (a device with no router)', () => {
        const verdict = LeakVerdict.decide(home, finished(hostLine(home.address), publicLine(home.address)), []);
        expect(verdict.outcome).toBe('no-leak');
        expect(verdict.visibleLocalAddresses).toEqual([]);
    });
});
