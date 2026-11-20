#!/usr/bin/env bash
# Usage: dev-server.sh start|doctor|stop [project] [url]
#   project: the site project directory (default: docs)
#   url:     the dev server address (default: http://127.0.0.1:8080)
set -euo pipefail

command=${1:?usage: dev-server.sh start|doctor|stop [project] [url]}
project=${2:-docs}
url=${3:-http://127.0.0.1:8080}
port=${url##*:}
port=${port%%/*}
state="artifacts/verify/dev-server-$port"
mkdir -p "$state"

live_members() {
    ps -e -o pgid=,pid=,stat=,args= | awk -v g="$(cat "$state/pgid")" '$1 == g && $3 !~ /^Z/'
}

group_alive() {
    [[ -f "$state/pgid" ]] && [[ -n "$(live_members)" ]]
}

wait_for_group_exit() {
    local seconds=$1
    for _ in $(seq 1 "$seconds"); do
        group_alive || return 0
        timeout 1 tail -f /dev/null || true
    done
    ! group_alive
}

case "$command" in
start)
    if group_alive; then
        echo "already running: process group $(cat "$state/pgid")" >&2
        exit 1
    fi
    if curl -s -o /dev/null --max-time 2 "$url/"; then
        echo "$url is already answering; it belongs to another process. Pick another url." >&2
        exit 1
    fi
    # setsid gives the watch tree its own process group, whose ID is the leader's PID ($$).
    ASPNETCORE_URLS="$url" setsid bash -c 'echo $$ > "$1"; exec dotnet watch --project "$2" --non-interactive' \
        _ "$state/pgid" "$project" >"$state/watch.log" 2>&1 < /dev/null &
    for _ in $(seq 1 180); do
        if grep -q 'Dev server started' "$state/watch.log" 2>/dev/null; then
            echo "ready: $url (process group $(cat "$state/pgid"), log $state/watch.log)"
            exit 0
        fi
        if ! group_alive && [[ -f "$state/pgid" ]]; then
            echo "dev server exited before it was ready:" >&2
            tail -40 "$state/watch.log" >&2
            exit 1
        fi
        timeout 1 tail -f /dev/null || true
    done
    echo "dev server not ready after 180 s:" >&2
    tail -40 "$state/watch.log" >&2
    exit 1
    ;;
doctor)
    group_alive || { echo "not running (no live process group in $state)" >&2; exit 1; }
    code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 "$url/")
    [[ "$code" =~ ^(200|301|302|404)$ ]] || { echo "unhealthy: GET $url/ returned $code" >&2; exit 1; }
    if grep -qE 'error|Unhandled exception' "$state/watch.log"; then
        echo "running, but the log reports errors:" >&2
        grep -nE 'error|Unhandled exception' "$state/watch.log" | tail -10 >&2
        exit 1
    fi
    echo "healthy: GET $url/ -> $code"
    ;;
stop)
    [[ -f "$state/pgid" ]] || { echo "nothing started from $state"; exit 0; }
    pgid=$(cat "$state/pgid")
    # dotnet watch ends its tree on Ctrl+C (SIGINT); SIGTERM stops the site but can leave
    # dotnet watch itself running.
    kill -INT -- "-$pgid" 2>/dev/null || true
    if ! wait_for_group_exit 20; then
        kill -TERM -- "-$pgid" 2>/dev/null || true
        wait_for_group_exit 10 || kill -KILL -- "-$pgid" 2>/dev/null || true
    fi
    if group_alive; then
        echo "process group $pgid still has processes:" >&2
        live_members >&2
        exit 1
    fi
    rm -f "$state/pgid"
    echo "stopped process group $pgid"
    ;;
*)
    echo "unknown command: $command" >&2
    exit 2
    ;;
esac
