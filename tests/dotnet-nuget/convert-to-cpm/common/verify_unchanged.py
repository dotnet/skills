from __future__ import annotations

import hashlib
import sys
from pathlib import Path


IGNORED_DIRECTORIES = {"bin", "obj", ".nuget-packages", ".nuget-feed"}
EXPECTED = {
    "packages-config": {
        "LegacyApp/LegacyApp.csproj": "4aebb081aed8e2ee9ee9f37b45772f5399899225856ff4e0c9b727bfdd1ee4b4",
        "LegacyApp/packages.config": "4a1fc6c75c6aa0a4838fe4430e8552971f58600dda94a76790774f0d54b12d4b",
    },
    "already-cpm": {
        "App/App.csproj": "533f14612da98a1d2509d9217ffcd8f3825ee8e87320c806a117a725adb23692",
        "App/Program.cs": "22920472fba5b4401659b50ba48aef0c0b912a67e33332f3e23ca98b7cb5fb96",
        "Directory.Packages.props": "4eff35f15d02cdf4d27322d2a5cfa8b86a1dd815d2069d494569f6c87cda2b57",
    },
}


def source_files(root: Path) -> dict[str, str]:
    result: dict[str, str] = {}
    for path in root.rglob("*"):
        if not path.is_file() or any(part in IGNORED_DIRECTORIES for part in path.parts):
            continue
        content = path.read_bytes().replace(b"\r\n", b"\n")
        result[path.relative_to(root).as_posix()] = hashlib.sha256(content).hexdigest()
    return result


case = sys.argv[1]
expected = EXPECTED[case]
actual = source_files(Path(sys.argv[2]))

if expected != actual:
    missing = sorted(expected.keys() - actual.keys())
    added = sorted(actual.keys() - expected.keys())
    changed = sorted(path for path in expected.keys() & actual.keys() if expected[path] != actual[path])
    print(f"missing={missing} added={added} changed={changed}")
    raise SystemExit(1)

print("UNCHANGED")
