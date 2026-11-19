#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ ${1:-} == --help ]]; then
    echo "Usage: $0 [Meitou-0.22.1-release-directory]"
    exit 0
fi
if [[ $# -gt 1 ]]; then
    echo "Usage: $0 [Meitou-0.22.1-release-directory]" >&2
    exit 2
fi

build_args=()
if [[ $# -eq 1 ]]; then
    build_args+=("-p:MeitouDirectory=$(realpath -e -- "$1")")
fi

cd -- "$repo_dir"
"${DOTNET:-dotnet}" build CharTweak3-Meitou.csproj -c Release -t:Rebuild "${build_args[@]}"

mkdir -p artifacts
stage_dir="$(mktemp -d "$repo_dir/artifacts/package.XXXXXX")"
trap 'rm -rf -- "$stage_dir"' EXIT
mod_dir="$stage_dir/CharTweak3-Meitou"
mkdir -- "$mod_dir"
install -m 644 bin/Release/net11.0/CharTweak3-Meitou.dll \
    bin/Release/net11.0/CharTweak3-Meitou.mod bin/Release/net11.0/meitou.json \
    LICENSE README.md "$mod_dir/"
tar -czf artifacts/CharTweak3-Meitou.tar.gz -C "$stage_dir" CharTweak3-Meitou
echo "Package: $repo_dir/artifacts/CharTweak3-Meitou.tar.gz"
