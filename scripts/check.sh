#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ ${1:-} == --help ]]; then
    echo "Usage: $0 <standard-Linux-Meitou-release-directory>"
    exit 0
fi
if [[ $# -ne 1 ]]; then
    echo "Usage: $0 <standard-Linux-Meitou-release-directory>"
    echo "Build the mod first. Checks run with the release's bundled .NET runtime."
    exit 2
fi
runtime_dir="$(realpath -e -- "$1")"
"${DOTNET:-dotnet}" build "$repo_dir/checks/CharTweak3.Checks/CharTweak3.Checks.csproj" \
    -c Release "-p:MeitouRuntimeDirectory=$runtime_dir"

# The Fedora SDK may still use an RC runtime. Harmony must run on the game's own
# supported runtime, so create an isolated test host from the release's apphost.
python3 - "$repo_dir" "$runtime_dir" <<'PY'
from pathlib import Path
import json
import os
import shutil
import subprocess
import sys
import tempfile

repo, runtime = map(Path, sys.argv[1:])
with tempfile.TemporaryDirectory(prefix="chartweak3-check-") as temporary:
    run = Path(temporary)
    for source in runtime.iterdir():
        if source.is_file() and source.name not in {"Check", "Check.dll", "Check.runtimeconfig.json", "Check.deps.json"}:
            (run / source.name).symlink_to(source)
    apphost = (runtime / "MeitouClient").read_bytes()
    entry = b"MeitouClient.dll"
    if apphost.count(entry) != 1:
        raise RuntimeError("Unsupported Meitou Linux apphost layout")
    (run / "Check").write_bytes(apphost.replace(entry, b"Check.dll".ljust(len(entry), b"\0")))
    (run / "Check").chmod(0o755)
    shutil.copy2(repo / "checks/CharTweak3.Checks/bin/Release/net11.0/Check.dll", run / "Check.dll")
    shutil.copy2(repo / "checks/CharTweak3.Checks/bin/Release/net11.0/Check.pdb", run / "Check.pdb")
    shutil.copy2(runtime / "MeitouClient.runtimeconfig.json", run / "Check.runtimeconfig.json")
    deps = json.loads((runtime / "MeitouClient.deps.json").read_text())
    deps["targets"][deps["runtimeTarget"]["name"]]["Check/1.0.0"] = {"runtime": {"Check.dll": {}}}
    deps["libraries"]["Check/1.0.0"] = {"type": "project", "serviceable": False, "sha512": ""}
    (run / "Check.deps.json").write_text(json.dumps(deps))
    environment = dict(os.environ, CHARTWEAK3_CONFIG_HOME=str(run / "config"))
    subprocess.run([str(run / "Check"), str(repo / "bin/Release/net11.0")], env=environment, check=True)
PY
