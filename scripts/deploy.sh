#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ ${1:-} == --help ]]; then
    echo "Usage: $0 [Kenshi-install-directory] (or set KENSHI_PATH)"
    echo "Close MeitouClient first. Build a fresh package with scripts/package.sh."
    exit 0
fi
if [[ $# -gt 1 ]]; then
    echo "Usage: $0 [Kenshi-install-directory] (or set KENSHI_PATH)" >&2
    exit 2
fi

kenshi_dir="${1:-${KENSHI_PATH:-}}"
if [[ -z $kenshi_dir || ! -f "$kenshi_dir/data/gamedata.base" ]]; then
    echo "Set KENSHI_PATH or pass a Kenshi install containing data/gamedata.base." >&2
    exit 1
fi
archive="$repo_dir/artifacts/CharTweak3-Meitou.tar.gz"
if [[ ! -f $archive ]]; then
    echo "Run scripts/package.sh first." >&2
    exit 1
fi

mkdir -p -- "$kenshi_dir/mods"
# Extract only the known mod files, preserving any user content in the mod folder.
tar -xzf "$archive" -C "$kenshi_dir/mods" \
    CharTweak3-Meitou/CharTweak3-Meitou.dll CharTweak3-Meitou/CharTweak3-Meitou.mod \
    CharTweak3-Meitou/meitou.json CharTweak3-Meitou/LICENSE CharTweak3-Meitou/README.md
echo "Installed in $kenshi_dir/mods/CharTweak3-Meitou"
echo "Enable CharTweak3-Meitou in the Meitou launcher and restart the client."
