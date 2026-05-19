#!/usr/bin/env bash
set -euo pipefail
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LINK="$REPO_ROOT/.obsidian/plugins/darbee-memory"

if [[ -L "$LINK" ]]; then
	rm "$LINK"
	echo "removed symlink $LINK"
elif [[ ! -e "$LINK" ]]; then
	echo "no symlink at $LINK"
else
	echo "$LINK is not a symlink — leaving it alone" >&2
	exit 1
fi
