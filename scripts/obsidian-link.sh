#!/usr/bin/env bash
# Symlink obsidian-plugin/dist/ into .obsidian/plugins/darbee-memory/.
# Idempotent: replaces an existing symlink, refuses to clobber a real directory.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC="$REPO_ROOT/obsidian-plugin/dist"
TARGET_DIR="$REPO_ROOT/.obsidian/plugins"
LINK="$TARGET_DIR/darbee-memory"

[[ -d "$SRC" ]] || { echo "build output missing at $SRC — run 'npm run obsidian:build' first" >&2; exit 1; }
mkdir -p "$TARGET_DIR"

if [[ -L "$LINK" ]]; then
	rm "$LINK"
elif [[ -e "$LINK" ]]; then
	echo "$LINK exists and is not a symlink — refusing to replace" >&2
	exit 1
fi

ln -s "$SRC" "$LINK"
echo "linked $LINK -> $SRC"
echo "Enable 'Darbee Memory' in Obsidian -> Community Plugins."
