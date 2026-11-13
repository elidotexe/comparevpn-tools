#!/usr/bin/env bash
# Downloads the public-domain iptoasn.com dataset (IPv4 + IPv6).
#
# Usage: scripts/download-data.sh [TARGET_DIR]
#   TARGET_DIR defaults to ./data in the repository (used by local development).
#   In production pass the directory that Tools:IspChecker:DataFilePath points to, e.g.
#   scripts/download-data.sh /var/lib/comparevpn-tools
#
# The dataset is updated hourly upstream; re-run this periodically (e.g. daily cron).
# The file is replaced atomically, so a running app never sees a partial download.
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
target_dir="${1:-$repo_root/data}"
target="$target_dir/ip2asn-combined.tsv.gz"

mkdir -p "$target_dir"
tmp="$(mktemp "$target_dir/ip2asn-combined.XXXXXX")"
trap 'rm -f "$tmp"' EXIT

curl -fsSL --retry 3 https://iptoasn.com/data/ip2asn-combined.tsv.gz -o "$tmp"
gzip -t "$tmp"
chmod 644 "$tmp"
mv "$tmp" "$target"
trap - EXIT

echo "Saved $target ($(du -h "$target" | cut -f1))"
