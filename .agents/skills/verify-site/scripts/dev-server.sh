#!/usr/bin/env bash
set -euo pipefail

command=${1:?usage: dev-server.sh start|doctor|stop [project] [url]}
project=${2:-docs}
url=${3:-http://127.0.0.1:8080}
port=${url##*:}
port=${port%%/*}
state="artifacts/verify/dev-server-$port"
mkdir -p "$state"

for required in dotnet curl ps awk grep; do
    command -v "$required" > /dev/null || { echo "dev-server.sh needs '$required' on PATH." >&2; exit 2; }
done

started_at() {
    ps -o lstart= -p "$1" 2> /dev/null | awk '{$1=$1; print}'
}

live_members() {
    ps -A -o pgid=,pid=,stat=,command= | awk -v g="$(cat "$state/pgid")" '$1 == g && $3 !~ /^Z/'
}

ours() {
    [[ -f "$state/pgid" && -f "$state/started" ]] || return 1
    local leader current
    leader=$(cat "$state/pgid")
    current=$(started_at "$leader")
    [[ -z "$current" || "$current" == "$(cat "$state/started")" ]]
}

group_alive() {
    ours && [[ -n "$(live_members)" ]]
}

forget_if_reused() {
    if [[ -f "$state/pgid" ]] && ! ours; then
        echo "process $(cat "$state/pgid") was started after this dev server; it is not ours. Forgetting $state." >&2
        rm -f "$state/pgid" "$state/started"
    fi
}

wait_for_group_exit() {
    local seconds=$1
    for _ in $(seq 1 "$seconds"); do
        group_alive || return 0
        sleep 1
    done
    ! group_alive
}

case "$command" in
start)
    forget_if_reused
    if group_alive; then
        echo "already running: process group $(cat "$state/pgid")" >&2
        exit 1
    fi
    if curl -s -o /dev/null --max-time 2 "$url/"; then
        echo "$url is already answering; it belongs to another process. Pick another url." >&2
        exit 1
    fi
    set -m
    ASPNETCORE_URLS="$url" bash -c 'exec dotnet watch --project "$1" --non-interactive' _ "$project" \
        > "$state/watch.log" 2>&1 < /dev/null &
    leader=$!
    set +m
    echo "$leader" > "$state/pgid"
    started_at "$leader" > "$state/started"
    for _ in $(seq 1 180); do
        if grep -q 'Dev server started' "$state/watch.log" 2> /dev/null; then
            echo "ready: $url (process group $leader, log $state/watch.log)"
            exit 0
        fi
        if ! group_alive; then
            echo "dev server exited before it was ready:" >&2
            tail -40 "$state/watch.log" >&2
            exit 1
        fi
        sleep 1
    done
    echo "dev server not ready after 180 s:" >&2
    tail -40 "$state/watch.log" >&2
    exit 1
    ;;
doctor)
    forget_if_reused
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
    forget_if_reused
    [[ -f "$state/pgid" ]] || { echo "nothing started from $state"; exit 0; }
    pgid=$(cat "$state/pgid")
    if group_alive; then
        kill -INT -- "-$pgid" 2> /dev/null || true
        if ! wait_for_group_exit 20; then
            ours && kill -TERM -- "-$pgid" 2> /dev/null || true
            wait_for_group_exit 10 || { ours && kill -KILL -- "-$pgid" 2> /dev/null; } || true
        fi
    fi
    if group_alive; then
        echo "process group $pgid still has processes:" >&2
        live_members >&2
        exit 1
    fi
    rm -f "$state/pgid" "$state/started"
    echo "stopped process group $pgid"
    ;;
*)
    echo "unknown command: $command" >&2
    exit 2
    ;;
esac
