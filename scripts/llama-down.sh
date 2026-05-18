#!/usr/bin/env bash
# Stop the two llama.cpp llama-server instances launched by llama-up.sh.
# Removes PID files. Logs are left in place for post-mortem; clean them
# manually with `rm .runtime/llama-*.log` if you want a fresh start.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RUNTIME_DIR="$REPO_ROOT/.runtime"

log() { echo "llama-down: $*"; }

stop_one() {
	local name="$1"
	local pid_file="$2"

	if [[ ! -f "$pid_file" ]]; then
		log "$name: no PID file, nothing to stop"
		return 0
	fi

	local pid
	pid="$(cat "$pid_file" 2>/dev/null || true)"
	if [[ -z "$pid" ]]; then
		log "$name: empty PID file, removing"
		rm -f "$pid_file"
		return 0
	fi

	if ! kill -0 "$pid" 2>/dev/null; then
		log "$name: pid=$pid not running, cleaning stale PID file"
		rm -f "$pid_file"
		return 0
	fi

	log "$name: sending SIGTERM to pid=$pid"
	kill -TERM "$pid" 2>/dev/null || true

	# Wait up to 10s for graceful shutdown.
	local deadline=$(( $(date +%s) + 10 ))
	while (( $(date +%s) < deadline )); do
		if ! kill -0 "$pid" 2>/dev/null; then
			break
		fi
		sleep 1
	done

	if kill -0 "$pid" 2>/dev/null; then
		log "$name: pid=$pid did not exit on SIGTERM, sending SIGKILL"
		kill -KILL "$pid" 2>/dev/null || true
		sleep 1
	fi

	rm -f "$pid_file"
	log "$name: stopped"
}

stop_one "llama-chat"  "$RUNTIME_DIR/llama-chat.pid"
stop_one "llama-embed" "$RUNTIME_DIR/llama-embed.pid"
