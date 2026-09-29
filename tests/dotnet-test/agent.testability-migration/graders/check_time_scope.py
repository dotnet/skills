"""A time-only migration must leave the unique export method unchanged."""

import json
from pathlib import Path
import re


SIGNATURE = ("public", "void", "ExportSubscription", "(", "Subscription", "sub", ")")


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


def main():
    baseline = json.loads(Path(".eval/baseline.json").read_text(encoding="utf-8"))
    path = "FullPipeline/Services/SubscriptionManager.cs"
    if export_body(Path(path).read_text(encoding="utf-8-sig")) != export_body(baseline[path]):
        raise ValueError(
            "Time-only migration changed ExportSubscription "
            "(filesystem/environment/console behavior)"
        )
    print("ExportSubscription unchanged (ignoring formatting/comments).")


if __name__ == "__main__":
    main()
