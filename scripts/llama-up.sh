#!/usr/bin/env bash
# Bring up the two llama.cpp llama-server instances the DAIS bridge needs:
#   :8080  chat model       (Llama-4-Scout by default, alias llama-4-scout)
#   :8081  embedding model  (Qwen3-Embedding-8B, alias qwen3-embedding-8b)
#
# Idempotent: skips a server if its PID file is alive and its /health is OK.
# Override paths via env vars; see DEFAULTS section below.
#
# Logs:  .runtime/llama-chat.log  / .runtime/llama-embed.log
# PIDs:  .runtime/llama-chat.pid  / .runtime/llama-embed.pid
set -euo pipefail

# ---------- DEFAULTS (override via env) -------------------------------------
LLAMA_SERVER_BIN="${LLAMA_SERVER_BIN:-$HOME/llms/llama.cpp/build/bin/llama-server}"

LLAMA_CHAT_MODEL="${LLAMA_CHAT_MODEL:-$HOME/models/Q4_K_M/Llama-4-Scout-17B-16E-Instruct-Q4_K_M-00001-of-00002.gguf}"
LLAMA_CHAT_ALIAS="${LLAMA_CHAT_ALIAS:-llama-4-scout}"
LLAMA_CHAT_PORT="${LLAMA_CHAT_PORT:-8080}"
LLAMA_CHAT_CTX="${LLAMA_CHAT_CTX:-8192}"

LLAMA_EMBED_MODEL="${LLAMA_EMBED_MODEL:-$HOME/.lmstudio/models/Qwen/Qwen3-Embedding-8B-GGUF/Qwen3-Embedding-8B-Q8_0.gguf}"
LLAMA_EMBED_ALIAS="${LLAMA_EMBED_ALIAS:-qwen3-embedding-8b}"
LLAMA_EMBED_PORT="${LLAMA_EMBED_PORT:-8081}"
LLAMA_EMBED_CTX="${LLAMA_EMBED_CTX:-4096}"

LLAMA_NGL="${LLAMA_NGL:-99}"
LLAMA_HOST="${LLAMA_HOST:-127.0.0.1}"

# Wait up to N seconds for each /health to report ok before declaring success.
LLAMA_READY_TIMEOUT="${LLAMA_READY_TIMEOUT:-180}"

# ---------- Paths -----------------------------------------------------------
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RUNTIME_DIR="$REPO_ROOT/.runtime"
mkdir -p "$RUNTIME_DIR"

CHAT_PID_FILE="$RUNTIME_DIR/llama-chat.pid"
CHAT_LOG_FILE="$RUNTIME_DIR/llama-chat.log"
EMBED_PID_FILE="$RUNTIME_DIR/llama-embed.pid"
EMBED_LOG_FILE="$RUNTIME_DIR/llama-embed.log"

# ---------- Helpers ---------------------------------------------------------
die() { echo "llama-up: $*" >&2; exit 1; }
log() { echo "llama-up: $*"; }

pid_alive() {
	local pid="$1"
	[[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null
}

health_ok() {
	local port="$1"
	curl -sf "http://${LLAMA_HOST}:${port}/health" >/dev/null 2>&1
}

read_pid() {
	local f="$1"
	[[ -f "$f" ]] && cat "$f" 2>/dev/null || true
}

wait_for_health() {
	local name="$1"
	local port="$2"
	local pid="$3"
	local deadline=$(( $(date +%s) + LLAMA_READY_TIMEOUT ))
	while (( $(date +%s) < deadline )); do
		if ! pid_alive "$pid"; then
			die "$name died during startup; tail the log to see why"
		fi
		if health_ok "$port"; then
			return 0
		fi
		sleep 2
	done
	die "$name did not become healthy on :$port within ${LLAMA_READY_TIMEOUT}s"
}

start_server() {
	local name="$1"
	local port="$2"
	local pid_file="$3"
	local log_file="$4"
	shift 4
	local args=("$@")

	local existing_pid
	existing_pid="$(read_pid "$pid_file")"
	if pid_alive "$existing_pid" && health_ok "$port"; then
		log "$name already running (pid=$existing_pid, port=$port)"
		return 0
	fi

	# Stale PID file: remove before relaunching.
	rm -f "$pid_file"

	# Port already serving /health with no tracked PID (likely a manual
	# llama-server the user started outside this script). Adopt it: do not
	# launch a duplicate, and do not write a PID we don't own.
	if health_ok "$port"; then
		log "$name port $port already serving /health (adopted, not tracked)"
		return 0
	fi

	log "starting $name on :$port..."
	nohup "$LLAMA_SERVER_BIN" "${args[@]}" >"$log_file" 2>&1 &
	local new_pid=$!
	echo "$new_pid" >"$pid_file"
	log "$name pid=$new_pid, waiting for /health..."
	wait_for_health "$name" "$port" "$new_pid"
	log "$name ready"
}

# ---------- Pre-flight -----------------------------------------------------
[[ -x "$LLAMA_SERVER_BIN" ]] || die "llama-server not found at $LLAMA_SERVER_BIN (set LLAMA_SERVER_BIN)"
[[ -f "$LLAMA_CHAT_MODEL" ]] || die "chat model not found at $LLAMA_CHAT_MODEL (set LLAMA_CHAT_MODEL)"
[[ -f "$LLAMA_EMBED_MODEL" ]] || die "embedding model not found at $LLAMA_EMBED_MODEL (set LLAMA_EMBED_MODEL)"

# ---------- Start ----------------------------------------------------------
start_server "llama-chat" "$LLAMA_CHAT_PORT" "$CHAT_PID_FILE" "$CHAT_LOG_FILE" \
	--host "$LLAMA_HOST" \
	--port "$LLAMA_CHAT_PORT" \
	--model "$LLAMA_CHAT_MODEL" \
	--alias "$LLAMA_CHAT_ALIAS" \
	--ctx-size "$LLAMA_CHAT_CTX" \
	--n-gpu-layers "$LLAMA_NGL"

start_server "llama-embed" "$LLAMA_EMBED_PORT" "$EMBED_PID_FILE" "$EMBED_LOG_FILE" \
	--host "$LLAMA_HOST" \
	--port "$LLAMA_EMBED_PORT" \
	--model "$LLAMA_EMBED_MODEL" \
	--alias "$LLAMA_EMBED_ALIAS" \
	--ctx-size "$LLAMA_EMBED_CTX" \
	--n-gpu-layers "$LLAMA_NGL" \
	--embeddings \
	--pooling last

log "both servers up:"
log "  chat   http://${LLAMA_HOST}:${LLAMA_CHAT_PORT}   alias=$LLAMA_CHAT_ALIAS"
log "  embed  http://${LLAMA_HOST}:${LLAMA_EMBED_PORT}   alias=$LLAMA_EMBED_ALIAS"
