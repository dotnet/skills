"""A time-only migration must leave the unrelated export method unchanged."""

import json
from pathlib import Path
import re


def export_body(source):
    # Tokenize strings before braces so interpolated strings do not affect nesting.
    tokens = [
        token for token in re.findall(r'\$?@?"(?:""|\\.|[^"\\])*"|//[^\n]*|/\*.*?\*/|\w+|[^\s]', source, re.S)
        if not token.startswith(("//", "/*"))
    ]
    start = tokens.index("{", tokens.index("ExportSubscription"))
    depth = 1
    end = start
    while depth:
        end += 1
        depth += (tokens[end] == "{") - (tokens[end] == "}")
    return tokens[start:end + 1]


baseline = json.loads(Path(".eval/baseline.json").read_text(encoding="utf-8"))
path = "FullPipeline/Services/SubscriptionManager.cs"
assert export_body(Path(path).read_text(encoding="utf-8-sig")) == export_body(baseline[path]), (
    "Time-only migration changed ExportSubscription (filesystem/environment/console behavior)"
)
print("ExportSubscription unchanged (ignoring formatting/comments).")
