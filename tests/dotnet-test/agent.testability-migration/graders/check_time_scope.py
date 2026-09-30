"""A time-only migration must leave the unique export method unchanged."""

import json
from pathlib import Path
import re


SIGNATURE = ("public", "void", "ExportSubscription", "(", "Subscription", "sub", ")")
BASELINE = Path(".eval/baseline.json")
SOURCE = Path("FullPipeline/Services/SubscriptionManager.cs")
PROJECT = Path("FullPipeline/FullPipeline.csproj")


def tokens(source):
    # Tokenize strings before braces so interpolated strings do not affect nesting.
    return [
        token for token in re.findall(r'\$?@?"(?:""|\\.|[^"\\])*"|//[^\n]*|/\*.*?\*/|\w+|[^\s]', source, re.S)
        if not token.startswith(("//", "/*"))
    ]


def export_body(source):
    source_tokens = tokens(source)
    matches = [
        index for index in range(len(source_tokens) - len(SIGNATURE) + 1)
        if tuple(source_tokens[index:index + len(SIGNATURE)]) == SIGNATURE
    ]
    if len(matches) != 1:
        raise ValueError(
            f"Expected exactly one 'public void ExportSubscription(Subscription sub)' method, found {len(matches)}"
        )
    start = source_tokens.index("{", matches[0] + len(SIGNATURE))
    depth = 1
    end = start
    while depth:
        end += 1
        depth += (source_tokens[end] == "{") - (source_tokens[end] == "}")
    return source_tokens[start:end + 1]


def baseline_bytes(baseline, path):
    try:
        return bytes.fromhex(baseline[path.as_posix()])
    except (KeyError, ValueError) as error:
        raise ValueError(f"Invalid authenticated baseline entry: {path}") from error


def verify():
    baseline = json.loads(BASELINE.read_text(encoding="utf-8"))
    for path in (SOURCE, PROJECT):
        if path.is_symlink() or not path.is_file():
            raise ValueError(f"Missing or symlinked protected file: {path}")
    original_source = baseline_bytes(baseline, SOURCE).decode("utf-8-sig")
    if export_body(SOURCE.read_text(encoding="utf-8-sig")) != export_body(original_source):
        raise ValueError(
            "Time-only migration changed ExportSubscription "
            "(filesystem/environment/console behavior)"
        )
    if PROJECT.read_bytes() != baseline_bytes(baseline, PROJECT):
        raise ValueError("Time-only migration changed the production project file")
    print("ExportSubscription and the production project are unchanged.")


def main():
    verify()


if __name__ == "__main__":
    main()
