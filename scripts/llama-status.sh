#!/usr/bin/env bash
# Probe the two llama-server health endpoints and report up/down + PIDs.
# Exit code: 0 if both UP, 1 if any DOWN.
set -euo pipefail

LLAMA_CHAT_PORT="${LLAMA_CHAT_PORT:-8080}"
LLAMA_EMBED_PORT="${LLAMA_EMBED_PORT:-8081}"
LLAMA_HOST="${LLAMA_HOST:-127.0.0.1}"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RUNTIME_DIR="$REPO_ROOT/.runtime"

failures=0

probe() {
	local name="$1"
	local port="$2"
	local pid_file="$3"

	local pid="(no PID file)"
	if [[ -f "$pid_file" ]]; then
		pid="$(cat "$pid_file" 2>/dev/null || echo "(unreadable)")"
		if [[ -n "$pid" ]] && ! kill -0 "$pid" 2>/dev/null; then
			pid="$pid (stale)"
		fi
	fi

	if curl -sf "http://${LLAMA_HOST}:${port}/health" >/dev/null 2>&1; then
		printf '%-12s UP    port=%s pid=%s\n' "$name" "$port" "$pid"
	else
		printf '%-12s DOWN  port=%s pid=%s\n' "$name" "$port" "$pid"
		failures=$((failures + 1))
	fi
}

probe "llama-chat"  "$LLAMA_CHAT_PORT"  "$RUNTIME_DIR/llama-chat.pid"
probe "llama-embed" "$LLAMA_EMBED_PORT" "$RUNTIME_DIR/llama-embed.pid"

exit $(( failures > 0 ? 1 : 0 ))
