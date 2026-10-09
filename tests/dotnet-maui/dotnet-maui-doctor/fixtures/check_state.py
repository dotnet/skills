import hashlib
import json
import sys
from pathlib import Path


def check(allowed=()):
    baseline = json.loads(Path(".scope-snapshot.json").read_text())
    for name, digest in baseline.items():
        path = Path(name)
        assert path.is_file(), f"Deleted input: {name}"
        assert hashlib.sha256(path.read_bytes()).hexdigest() == digest, f"Changed input: {name}"
    extra = {
        path.as_posix() for path in Path(".").rglob("*")
        if path.is_file()
        and ".git" not in path.parts
        and "__pycache__" not in path.parts
        and path.name != ".scope-snapshot.json"
    } - baseline.keys() - set(allowed)
    assert not extra, f"Unexpected files: {sorted(extra)}"


if __name__ == "__main__":
    check(sys.argv[1:])
    print("Complete input state preserved")
