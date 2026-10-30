#!/usr/bin/env bash
#
# Boots the local dedicated zone servers from a Linux Server build.
#
# Each zone is its own process on its own port (SafeZone 7770, Wilderness 7771,
# Tutorial 7772, TestZone 7773). Any process already holding a zone's port is killed first, so a
# re-run never fails with "Address already in use". Run the whole shard with no
# arguments, or pass the zones you want.
#
# Usage:
#   ./run-servers.sh                 # start every zone
#   ./run-servers.sh Tutorial        # start one zone
#   ./run-servers.sh SafeZone Wilderness
#   ./run-servers.sh --list          # show known zones
#
# The server binary defaults to ./build/server.x86_64 and can be overridden with
# SERVER_BINARY or the first --binary <path> flag.
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Headless by default: no window, no graphics device — a zone server does not render anything.
# Override with SERVER_FLAGS="" if you need to attach a renderer while debugging.
SERVER_FLAGS="${SERVER_FLAGS:--batchmode -nographics}"
ALL_ZONES=(SafeZone Wilderness Tutorial TestZone)
binary_override=""

# Keep in sync with ZoneSceneBaker's baked per-zone ports.
declare -A ZONE_PORTS=(
    [SafeZone]=7770
    [Wilderness]=7771
    [Tutorial]=7772
    [TestZone]=7773
)

usage() {
    sed -n '2,18p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
}

# Kills whatever is LISTENING on the given port so the new process can bind it.
# Needed because a previous run (or a crash) can leave a server holding the port.
free_port() {
    local port="$1"
    local pids_on_port=""

    if command -v lsof >/dev/null 2>&1; then
        pids_on_port=$(lsof -ti "tcp:$port" -sTCP:LISTEN 2>/dev/null || true)
    fi
    if [[ -z "$pids_on_port" ]] && command -v fuser >/dev/null 2>&1; then
        # fuser prints pids to stdout, space separated.
        pids_on_port=$(fuser "$port/tcp" 2>/dev/null | tr -s ' ' '\n' | grep -E '^[0-9]+$' || true)
    fi
    if [[ -z "$pids_on_port" ]] && command -v ss >/dev/null 2>&1; then
        pids_on_port=$(ss -ltnpH "sport = :$port" 2>/dev/null | grep -oP 'pid=\K[0-9]+' | sort -u || true)
    fi

    if [[ -n "$pids_on_port" ]]; then
        echo "  Port $port held by pid(s): $(echo $pids_on_port | tr '\n' ' ') — killing."
        # shellcheck disable=SC2086  # pids_on_port is a newline/space list of pids.
        kill $pids_on_port 2>/dev/null || true
        sleep 0.5
    fi
}

zones=()
while [[ $# -gt 0 ]]; do
    case "$1" in
        --help|-h)
            usage
            exit 0
            ;;
        --list)
            printf '%s\n' "${ALL_ZONES[@]}"
            exit 0
            ;;
        --binary)
            binary_override="${2:?--binary needs a path}"
            shift 2
            ;;
        -*)
            echo "Unknown option: $1" >&2
            usage >&2
            exit 2
            ;;
        *)
            zones+=("$1")
            shift
            ;;
    esac
done

if [[ ${#zones[@]} -eq 0 ]]; then
    zones=("${ALL_ZONES[@]}")
fi

# Resolve the binary: an explicit --binary/SERVER_BINARY wins; otherwise look next to this script
# (copied into the build folder) and then under build/ (script run from the repo root).
if [[ -n "$binary_override" ]]; then
    server_binary="$binary_override"
elif [[ -n "${SERVER_BINARY:-}" ]]; then
    server_binary="$SERVER_BINARY"
elif [[ -x "$SCRIPT_DIR/build/server.x86_64" ]]; then
    server_binary="$SCRIPT_DIR/build/server.x86_64"
else
    server_binary="$SCRIPT_DIR/server.x86_64"
fi

if [[ ! -x "$server_binary" ]]; then
    echo "Server binary not found or not executable: $server_binary" >&2
    echo "Build it first (File > Build Settings > Linux Server / Dedicated Server)," >&2
    echo "or point SERVER_BINARY (or --binary) at the right path." >&2
    exit 1
fi

pids=()
names=()

cleanup() {
    echo
    echo "Stopping ${#pids[@]} zone server(s)..."
    for pid in "${pids[@]}"; do
        kill "$pid" 2>/dev/null || true
    done
    wait 2>/dev/null || true
    echo "Done."
}

trap cleanup INT TERM

# Free the port for each zone we are about to start, so a leftover process can't cause
# "Address already in use" and leave the zone silently offline.
for zone in "${zones[@]}"; do
    port="${ZONE_PORTS[$zone]:-}"
    if [[ -n "$port" ]]; then
        free_port "$port"
    fi
done

for zone in "${zones[@]}"; do
    echo "Starting zone '$zone'..."
    # shellcheck disable=SC2086  # SERVER_FLAGS is intentionally word-split into separate args.
    "$server_binary" $SERVER_FLAGS -zone "$zone" -logFile "zone-$zone.log" &
    pids+=("$!")
    names+=("$zone")
done

echo
echo "Started ${#pids[@]} zone server(s):"
for i in "${!pids[@]}"; do
    printf '  %-12s pid %-7s log: zone-%s.log\n' "${names[$i]}" "${pids[$i]}" "${names[$i]}"
done

echo
echo "Press Ctrl+C to stop all." 

# Wait for every child; if any exits, bring the rest down too.
wait -n
echo "A zone server exited; shutting the rest down."
cleanup
