"""Reject old xUnit configuration and newly imposed diagnostic deadlines."""

import argparse
import hashlib
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET


def test_body_digest(source, names):
    # Preserve executable tokens, not comments/formatting or lifecycle declarations.
    pattern = re.compile(
        r'(?P<comment>//[^\r\n]*|/\*.*?\*/)|'
        r'(?P<string>(?P<raw>"{3,})[\s\S]*?(?P=raw)|(?:\$@|@\$|@)"(?:[^"]|"")*"|'
        r'\$?"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\')|(?P<space>\s+)|'
        r'(?P<token>[A-Za-z_]\w*|\d+(?:\.\d+)?|=>|==|!=|<=|>=|\+\+|--|&&|\|\||\?\?|\S)',
        re.S,
    )
    tokens = [
        match[0] for match in pattern.finditer(source)
        if match.lastgroup not in {"comment", "space"}
    ]
    for before, after in (
        (("global", ":", ":"), ()),
        (("Microsoft", ".", "VisualStudio", ".", "TestTools", ".", "UnitTesting", ".", "Assert"),
         ("Assert",)),
        (("System", ".", "Environment"), ("Environment",)),
        (("Assert", ".", "Equal"), ("Assert", ".", "AreEqual")),
        (("Assert", ".", "True"), ("Assert", ".", "IsTrue")),
    ):
        index = 0
        while index <= len(tokens) - len(before):
            if tuple(tokens[index:index + len(before)]) == before:
                tokens[index:index + len(before)] = after
                index += len(after)
            else:
                index += 1
    bodies = []
    for name in names:
        prefix = ("void", name, "(", ")")
        matches = [
            i + len(prefix) for i in range(len(tokens) - len(prefix))
            if tuple(tokens[i:i + len(prefix)]) == prefix
        ]
        assert len(matches) == 1, f"Missing or duplicate invariant test body: {name}"
        start = matches[0]
        if tokens[start] == "=>":
            end = tokens.index(";", start + 1)
            body = ["{", *tokens[start + 1:end + 1], "}"]
        else:
            assert tokens[start] == "{", f"Missing invariant test body: {name}"
            depth = 1
            end = start + 1
            while end < len(tokens) and depth:
                depth += (tokens[end] == "{") - (tokens[end] == "}")
                end += 1
            assert depth == 0, f"Unclosed invariant test body: {name}"
            body = tokens[start:end]
        bodies.append((name, body))
    return hashlib.sha256(json.dumps(bodies, separators=(",", ":")).encode("utf-8")).hexdigest()


def verify_test_bodies(root, contract):
    profile, expected, filename = contract.split(":", 2)
    profiles = {
        "collection": ("FirstCase", "SecondCase"),
        "diagnostics": ("CompletesNormally",),
    }
    assert profile in profiles, f"Unknown test-body contract: {profile}"
    path = root / filename
    assert path.is_file() and not path.is_symlink(), f"Missing invariant source: {filename}"
    actual = test_body_digest(path.read_text(encoding="utf-8"), profiles[profile])
    assert actual == expected, f"Original test-body assertions changed: {filename}"


def verify(root, forbid_timeouts=False):
    ignored = {".git", ".eval", ".agents", ".copilot", ".claude", "bin", "obj"}
    for path in root.rglob("*"):
        if not path.is_file() or any(part in ignored for part in path.relative_to(root).parts):
            continue
        assert path.name.lower() != "xunit.runner.json", f"Obsolete xUnit runner config: {path}"
        if path.suffix in {".csproj", ".props", ".targets", ".runsettings"}:
            for node in ET.parse(path).getroot().iter():
                tag = node.tag.rsplit("}", 1)[-1]
                if tag in {"PackageReference", "PackageVersion"}:
                    package = node.get("Include", node.get("Update", "")).lower()
                    assert package != "xunit" and not package.startswith("xunit."), (
                        f"Retained xUnit package: {package} in {path}"
                    )
                if tag == "Using":
                    namespace = node.get("Include", "").removeprefix("global::")
                    assert namespace != "Xunit" and not namespace.startswith("Xunit."), (
                        f"Retained xUnit import: {namespace} in {path}"
                    )
                if forbid_timeouts and tag in {"TestTimeout", "TestSessionTimeout"}:
                    assert int((node.text or "").strip()) == 0, (
                        f"Diagnostic warning became enforced {tag}: {path}"
                    )
        elif path.suffix == ".cs":
            text = path.read_text(encoding="utf-8")
            text = re.sub(
                r'(?P<raw>"{3,})[\s\S]*?(?P=raw)|@"(?:[^"]|"")*"|'
                r'"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|//[^\n]*|/\*[\s\S]*?\*/',
                lambda match: " " + "\n" * match[0].count("\n"),
                text,
            )
            assert not re.search(
                r"\b(?:global\s+)?using\s+(?:(?:static|unsafe)\s+)?"
                r"(?:\w+\s*=\s*)?(?:global\s*::\s*)?@?Xunit\b",
                text,
            ), f"Retained xUnit using directive: {path}"
            assert not re.search(
                r"\bnamespace\s+@?Xunit\b|\bXunit\s*(?:\.|::)", text,
            ), f"Retained xUnit namespace or qualified reference: {path}"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--forbid-timeouts", action="store_true")
    parser.add_argument("--test-bodies")
    args = parser.parse_args()
    verify(Path.cwd(), args.forbid_timeouts)
    if args.test_bodies:
        verify_test_bodies(Path.cwd(), args.test_bodies)


if __name__ == "__main__":
    main()
