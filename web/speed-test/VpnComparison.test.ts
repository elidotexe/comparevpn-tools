import { describe, expect, it } from 'vitest';
import { VpnComparison } from './VpnComparison';
import type { ConnectionSeen } from './VpnComparison';
import type { SpeedMeasurement } from './SpeedTestEngine';

const home: ConnectionSeen = { address: '73.1.2.3', asn: 7922, isHosting: false };
const vpn: ConnectionSeen = { address: '185.159.157.10', asn: 209103, isHosting: true };

const figures = (downloadMbps: number, uploadMbps: number | null, latencyMs: number | null): SpeedMeasurement => ({
    downloadMbps, uploadMbps, latencyMs, jitterMs: null, downloadLoadedLatencyMs: null, uploadLoadedLatencyMs: null,
});

describe('VpnComparison', () => {
    it('spots a first test that already ran through a VPN', () => {
        expect(VpnComparison.baselineLooksLikeVpn(home)).toBe(false);
        expect(VpnComparison.baselineLooksLikeVpn(vpn)).toBe(true);
        expect(VpnComparison.check(vpn, { ...vpn, address: '185.159.157.11' })).toBe('baseline-on-vpn');
    });

    it('detects the VPN when the address moved to a hosting network', () => {
        expect(VpnComparison.check(home, vpn)).toBe('vpn-detected');
    });

    it('says the VPN seems off when the address stayed the same', () => {
        expect(VpnComparison.check(home, home)).toBe('same-address');
    });

    it('only says the connection changed when the new address is not a known hosting network', () => {
        expect(VpnComparison.check(home, { address: '98.1.2.3', asn: 7922, isHosting: false })).toBe('connection-changed');
    });

    it('works out the speed kept and the delay added', () => {
        const difference = VpnComparison.compare(figures(470, 52, 14.6), figures(385.4, 38.48, 48.6));
        expect(difference.downloadKeptPercent).toBeCloseTo(82);
        expect(difference.uploadKeptPercent).toBeCloseTo(74);
        expect(difference.latencyAddedMs).toBeCloseTo(34);
    });

    it('leaves out figures one of the tests could not measure', () => {
        expect(VpnComparison.compare(figures(100, null, 15), figures(80, 20, null))).toEqual({
            downloadKeptPercent: 80, uploadKeptPercent: null, latencyAddedMs: null,
        });
        expect(VpnComparison.compare(figures(100, 0, 15), figures(80, 20, 15)).uploadKeptPercent).toBeNull();
    });
});
