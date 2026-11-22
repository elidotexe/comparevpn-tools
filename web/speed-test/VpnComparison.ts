import type { SpeedMeasurement } from './SpeedTestEngine';

/** The connection a test ran on, as a lookup of the visitor's own address found it (e.g. GET /api/tools/isp). */
export interface ConnectionSeen {
    readonly address: string;
    readonly asn: number | null;
    readonly isHosting: boolean;
}

/**
 * Whether the second test really ran through a VPN:
 * - "vpn-detected": the address moved to a hosting or VPN network
 * - "same-address": nothing changed, so the VPN was probably off
 * - "connection-changed": the address changed, but not to a known hosting network (some VPNs use home addresses)
 * - "baseline-on-vpn": the first test, meant to run without a VPN, already ran through a hosting network
 */
export type VpnCheck = 'vpn-detected' | 'same-address' | 'connection-changed' | 'baseline-on-vpn';

/** How the test with the VPN compares with the one without. */
export interface SpeedDifference {
    /** The share of the speed without the VPN that remained with it: 82 means the VPN kept 82%. */
    readonly downloadKeptPercent: number;
    readonly uploadKeptPercent: number | null;
    /** How many milliseconds the VPN added to each request (negative when it answered faster). */
    readonly latencyAddedMs: number | null;
}

/**
 * Compares a speed test run without a VPN with one run through it, like weighing a parcel with and without its
 * packaging.
 *
 * In plain terms, here is what happens:
 *   1. After the first test, it checks that the connection isn't already a VPN, so the comparison starts from the
 *      real home connection.
 *   2. After the second test, it checks that the address really changed to a VPN, rather than trusting that the
 *      visitor switched it on.
 *   3. It works out how much of the download and upload speed the VPN kept, and how much delay it added.
 * The addresses are only compared, never kept: a saved result needs only the outcome of the check.
 */
export class VpnComparison {
    /** Step 1: Whether the first test's connection looks like a VPN already (a hosting or VPN network). */
    public static baselineLooksLikeVpn(withoutVpn: ConnectionSeen): boolean {
        return withoutVpn.isHosting;
    }

    /** Step 2: Whether the second test ran through a VPN, judged from the two connections. */
    public static check(withoutVpn: ConnectionSeen, withVpn: ConnectionSeen): VpnCheck {
        if (VpnComparison.baselineLooksLikeVpn(withoutVpn)) return 'baseline-on-vpn';
        if (withVpn.address === withoutVpn.address) return 'same-address';
        return withVpn.isHosting ? 'vpn-detected' : 'connection-changed';
    }

    /** Step 3: The speed kept and the delay added by the VPN. */
    public static compare(withoutVpn: SpeedMeasurement, withVpn: SpeedMeasurement): SpeedDifference {
        return {
            downloadKeptPercent: (withVpn.downloadMbps / withoutVpn.downloadMbps) * 100,
            uploadKeptPercent:
                withVpn.uploadMbps !== null && withoutVpn.uploadMbps !== null && withoutVpn.uploadMbps > 0
                    ? (withVpn.uploadMbps / withoutVpn.uploadMbps) * 100
                    : null,
            latencyAddedMs:
                withVpn.latencyMs !== null && withoutVpn.latencyMs !== null ? withVpn.latencyMs - withoutVpn.latencyMs : null,
        };
    }
}
