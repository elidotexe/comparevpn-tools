#!/usr/bin/env bash
# Downloads the datasets the app reads:
#   - the public-domain iptoasn.com list of networks (IPv4 + IPv6), updated hourly upstream;
#   - the DB-IP "IP to City Lite" location file (CC BY 4.0, credit DB-IP when you show its results), updated monthly.
#
# Usage: scripts/download-data.sh [TARGET_DIR]
#   TARGET_DIR defaults to ./data in the repository (used by local development).
#   In production pass the directory that Tools:IspChecker:DataFilePath points to, e.g.
#   scripts/download-data.sh /var/lib/comparevpn-tools
#   Both files go into that directory, so LocationDataFilePath should point there too.
#   Set SKIP_LOCATION_DATA=1 to download only the list of networks (location is optional).
#
# In plain terms, here is what happens:
#   1. It downloads the list of networks, checks it, and swaps it in. A failure here stops the script with an error.
#   2. Unless SKIP_LOCATION_DATA=1, it checks which month's location file it already has. If that is this month's,
#      there is nothing to do; otherwise it downloads this month's file, or last month's when this month's isn't
#      published yet (DB-IP publishes early in the month).
#   3. It unpacks and checks the location file, then swaps it in. A failure here is only a warning: the app keeps
#      the location file it has, or answers without a location.
#
# Re-run this periodically (e.g. daily cron); the location file is only downloaded again when a new month's is out.
# Files are replaced atomically, so a running app never sees a partial download.
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
target_dir="${1:-$repo_root/data}"
mkdir -p "$target_dir"

# The two download functions run in their own subshell (the "( ... )" bodies), so their clean-up traps and any
# failure stay inside them. Each turns "stop on the first error" on again itself, because the caller may have
# turned it off.

# Step 1: The list of networks.
download_networks() (
    set -euo pipefail
    target="$target_dir/ip2asn-combined.tsv.gz"
    tmp="$(mktemp "$target_dir/ip2asn-combined.XXXXXX")"
    trap 'rm -f "$tmp"' EXIT

    curl -fsSL --retry 3 https://iptoasn.com/data/ip2asn-combined.tsv.gz -o "$tmp"
    gzip -t "$tmp"
    chmod 644 "$tmp"
    mv "$tmp" "$target"
    echo "Saved $target ($(du -h "$target" | cut -f1))"
)

# Step 2: Which month's location file to fetch, if any. Prints the month (YYYY-MM), or nothing when up to date.
# The month is worked out in plain shell arithmetic, so it works with both GNU and BSD/macOS "date".
location_month_to_fetch() {
    local have="$1" year month this_month last_month
    year="$(date -u +%Y)"
    month="$(date -u +%m)"
    this_month="$year-$month"
    month=$((10#$month - 1))
    if (( month == 0 )); then month=12; year=$((year - 1)); fi
    last_month="$(printf '%04d-%02d' "$year" "$month")"

    if [[ "$have" == "$this_month" ]]; then
        return
    fi
    if url_exists "$(location_url "$this_month")"; then
        echo "$this_month"
    elif [[ "$have" != "$last_month" ]]; then
        echo "$last_month"
    fi
}

location_url() { echo "https://download.db-ip.com/free/dbip-city-lite-$1.mmdb.gz"; }

# Quiet on purpose: early in the month this month's file is expected to be missing.
url_exists() { curl -fsIL --retry 2 -o /dev/null "$1"; }

# Steps 2 and 3: The location file.
download_location() (
    set -euo pipefail
    target="$target_dir/dbip-city-lite.mmdb"
    month_file="$target_dir/dbip-city-lite.month"
    have=""
    if [[ -f "$target" && -f "$month_file" ]]; then have="$(cat "$month_file")"; fi

    month="$(location_month_to_fetch "$have")"
    if [[ -z "$month" ]]; then
        if [[ "$have" == "$(date -u +%Y-%m)" ]]; then
            echo "Location file $target is up to date ($have)"
        else
            echo "This month's location file isn't available yet; keeping $target ($have)"
        fi
        exit 0
    fi

    packed="$(mktemp "$target_dir/dbip-city-lite.XXXXXX")"
    unpacked="$(mktemp "$target_dir/dbip-city-lite.XXXXXX")"
    trap 'rm -f "$packed" "$unpacked"' EXIT

    curl -fsSL --retry 3 "$(location_url "$month")" -o "$packed"
    gzip -t "$packed"
    gzip -dc "$packed" > "$unpacked"
    # Every file in this format ends with a description that starts after this marker.
    tail -c 262144 "$unpacked" | grep -aq "MaxMind.com"
    chmod 644 "$unpacked"
    mv "$unpacked" "$target"
    echo "$month" > "$month_file"
    echo "Saved $target ($month, $(du -h "$target" | cut -f1))"
)

download_networks

if [[ "${SKIP_LOCATION_DATA:-0}" != "1" ]]; then
    # Not written as "if ! download_location": bash ignores "set -e" inside an if condition, so a failed step
    # would carry on. Instead let it stop its own subshell, and only then turn the failure into a warning.
    set +e
    download_location
    location_status=$?
    set -e
    if (( location_status != 0 )); then
        echo "Location file download failed; keeping the current location data (if any). Lookups still work." >&2
    fi
fi
