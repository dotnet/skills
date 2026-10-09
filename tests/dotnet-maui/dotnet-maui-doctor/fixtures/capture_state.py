import hashlib
import json
from pathlib import Path


def state():
    result = {}
    for path in Path(".").rglob("*"):
        if not path.is_file() or ".git" in path.parts or "__pycache__" in path.parts:
            continue
        if path.name == ".scope-snapshot.json":
            continue
        result[path.as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    return result


if __name__ == "__main__":
    Path(".scope-snapshot.json").write_text(json.dumps(state(), sort_keys=True))
