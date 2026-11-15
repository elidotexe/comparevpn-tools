#!/usr/bin/env bash
# Starts the ISP lookup app inside its container and keeps its list of networks fresh.
#
# In plain terms, here is what happens:
#   1. It checks the DATASET_REFRESH_INTERVAL setting (default "24h"; "0" turns refreshing off, for servers that
#      mount the dataset and refresh it themselves). A bad value stops the container with a message.
#   2. Unless refreshing is off, a small background loop downloads a fresh copy of the dataset now and then once
#      per interval. A failed download is logged and tried again next time; the app keeps using the data it has.
#   3. It starts the app. The image already holds a copy of the dataset from when it was built, so lookups work
#      straight away, and the app swaps in each new download without a restart.
set -euo pipefail

script_dir="$(cd "$(dirname "$0")" && pwd)"
data_file="${Tools__IspChecker__DataFilePath:-/app/data/ip2asn-combined.tsv.gz}"
interval="${DATASET_REFRESH_INTERVAL:-24h}"

# Step 1: A number, optionally followed by s, m, h or d (the units "sleep" understands).
if [[ ! "$interval" =~ ^[0-9]+[smhd]?$ ]]; then
    echo "DATASET_REFRESH_INTERVAL must be a number with an optional unit (s, m, h, d), e.g. 24h or 0 to turn it off. Got: '$interval'" >&2
    exit 1
fi

# Step 2: Refresh the dataset in the background, forever.
refresh_dataset_forever() {
    while true; do
        bash "$script_dir/download-data.sh" "$(dirname "$data_file")" \
            || echo "Dataset refresh failed; keeping the current data and trying again in $interval." >&2
        sleep "$interval"
    done
}

if [[ "$interval" != "0" ]]; then
    refresh_dataset_forever &
fi

# Step 3: "exec" hands this process over to the app, so the stop signal from the container reaches it directly
# and it can shut down cleanly.
exec dotnet /app/CompareVPN.ISPLookup.dll
