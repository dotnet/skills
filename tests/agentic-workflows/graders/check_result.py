"""Generic proposal checks; all scenario expectations arrive in evaluator argv."""

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import sys


def require(condition, message):
    if not condition:
        raise ValueError(message)


def strict_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, f"Duplicate JSON key: {key}")
        result[key] = value
    return result


def read_json(path):
    return json.loads(
        path.read_text(encoding="utf-8-sig"),
        object_pairs_hook=strict_object,
        parse_constant=lambda value: (_ for _ in ()).throw(ValueError(f"Invalid JSON number: {value}")),
    )


def tree_digest(root):
    require(root.is_dir() and not root.is_symlink(), "Missing or symlinked input tree")
    digest = hashlib.sha256()
    for path in sorted(root.rglob("*"), key=lambda path: path.relative_to(root).as_posix()):
        require(not path.is_symlink(), f"Symlinked input: {path}")
        if path.is_file():
            relative = path.relative_to(root).as_posix().encode("utf-8")
            content = path.read_bytes().replace(b"\r\n", b"\n")
            digest.update(relative + b"\0" + content + b"\0")
    return digest.hexdigest()


def input_relative_path(value):
    require(isinstance(value, str) and value.strip(), "Evidence path must be a nonempty string")
    require("\\" not in value, "Evidence paths must be repository-relative JSON paths")
    require(":" not in value, "Evidence paths must not contain drive qualifiers or alternate streams")
    relative = PurePosixPath(value)
    require(not relative.is_absolute() and ".." not in relative.parts, "Evidence path escapes inputs")
    if relative.parts and relative.parts[0] == "inputs":
        relative = PurePosixPath(*relative.parts[1:])
    require(relative.parts, "Evidence path must identify a file under inputs")
    return relative.as_posix()


def input_file(root, value):
    relative = Path(input_relative_path(value))
    path = root / "inputs" / relative
    require(path.is_file() and not path.is_symlink(), f"Missing evidence/source: {value}")
    return path


def check_record(path, record):
    line = re.fullmatch(r"line\s+([1-9][0-9]*)", record, re.IGNORECASE)
    text = path.read_text(encoding="utf-8-sig")
    if line:
        require(int(line[1]) <= len(text.splitlines()), "Evidence line is outside the file")
        return
    if path.suffix.lower() not in (".json", ".jsonl"):
        raise ValueError("Text/source citations must identify a line")
    documents = [json.loads(item) for item in text.splitlines() if item.strip()] if path.suffix == ".jsonl" else [read_json(path)]
    tokens = set()

    def visit(value):
        if isinstance(value, dict):
            tokens.update(value.keys())
            for child in value.values():
                visit(child)
        elif isinstance(value, list):
            for child in value:
                visit(child)
        elif isinstance(value, str):
            tokens.add(value)

    for document in documents:
        visit(document)
    require(record in tokens, "Evidence record does not exist")


def parse_options(arguments=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--expected-action", required=True, choices=("comment", "review", "noop", "patch"))
    parser.add_argument("--input-digest", required=True)
    parser.add_argument("--min-findings", type=int, default=0)
    parser.add_argument("--max-findings", type=int, default=10)
    parser.add_argument("--max-body-chars", type=int)
    parser.add_argument("--require-pattern", action="append", default=[])
    parser.add_argument("--require-summary-pattern", action="append", default=[])
    parser.add_argument("--forbid-pattern", action="append", default=[])
    parser.add_argument("--allowed-classification", action="append", default=[])
    parser.add_argument("--allowed-finding-path", action="append", default=[])
    parser.add_argument("--expected-candidate", action="append", default=[])
    parser.add_argument("--require-limitations", action="store_true")
    parser.add_argument("--forbid-high-confidence", action="store_true")
    options = parser.parse_args(arguments)
    require(re.fullmatch(r"[0-9a-f]{64}", options.input_digest), "Invalid evaluator input digest")
    require(0 <= options.min_findings <= options.max_findings, "Invalid evaluator finding limits")
    if options.max_body_chars is not None:
        require(options.max_body_chars > 0, "Invalid evaluator body bound")
    require(len(options.expected_candidate) == len(set(options.expected_candidate)), "Duplicate evaluator candidate expectation")
    require(bool(options.expected_candidate) == (options.expected_action == "patch"), "Selection expectations must match output type")
    for pattern in options.require_pattern + options.require_summary_pattern + options.forbid_pattern:
        re.compile(pattern)
    return options


def validate_result(root, options):
    root = Path(root)
    require(tree_digest(root / "inputs") == options.input_digest, "Input source/evidence was edited, added, or deleted")
    result_path = root / "result.json"
    require(result_path.is_file() and not result_path.is_symlink(), "Missing or symlinked result.json")
    require(result_path.stat().st_size <= 100_000, "Unbounded result.json")
    result = read_json(result_path)
    require(isinstance(result, dict), "result.json must be an object")
    allowed = {"action", "reason", "proposed_only", "findings", "limitations", "body", "review_event", "candidate_ids", "manifest_digest", "source_commit", "publication_authorized"}
    require(result.keys() <= allowed, "Unsupported action/result fields")
    action = options.expected_action
    require(result.get("action") == action, f"Expected {action}, received {result.get('action')!r}")
    require(result.get("proposed_only") is True, "Actions must be offline proposals")
    reason = result.get("reason")
    require(isinstance(reason, str) and reason.strip(), "Missing decision reason")
    findings = result.get("findings")
    require(isinstance(findings, list) and options.min_findings <= len(findings) <= options.max_findings, "Wrong number of supported findings")
    limitations = result.get("limitations")
    require(isinstance(limitations, list) and all(isinstance(item, str) and item.strip() for item in limitations), "Invalid limitations")
    for finding in findings:
        require(isinstance(finding, dict), "Finding must be an object")
        for key in ("summary", "classification", "next_step"):
            require(isinstance(finding.get(key), str) and finding[key].strip(), f"Missing finding {key}")
        for pattern in options.require_summary_pattern:
            require(re.search(pattern, finding["summary"], re.IGNORECASE), f"Finding summary lacks supported outcome: {pattern}")
        require(finding.get("confidence") in ("high", "medium", "low"), "Invalid confidence")
        citations = finding.get("evidence")
        require(isinstance(citations, list) and citations, "Finding needs evidence")
        for citation in citations:
            require(isinstance(citation, dict), "Invalid evidence citation")
            evidence = input_file(root, citation.get("path"))
            require(isinstance(citation.get("record"), str) and citation["record"].strip(), "Missing record citation")
            check_record(evidence, citation["record"])
        if action == "review" or "suggestion" in finding:
            source = finding.get("path")
            input_file(root, source)
            source = input_relative_path(source)
            context = read_json(root / "inputs" / "context.json")
            changed = context.get("changed_files", {}).get(source, {}).get("lines", [])
            require(type(finding.get("line")) is int and finding["line"] in changed, "Finding/suggestion is not on a changed line")
            if "suggestion" in finding:
                require(isinstance(finding["suggestion"], str), "Suggestion must be exact replacement text")
        if options.allowed_classification:
            require(finding["classification"].lower() in options.allowed_classification, "Wrong classification")
        if options.forbid_high_confidence:
            require(finding["confidence"] != "high", "Unsupported confidence")
        if options.allowed_finding_path:
            require(input_relative_path(finding.get("path")) in options.allowed_finding_path, "Finding exceeds allowed scope")
    if options.require_limitations:
        require(limitations, "Evidence gap must remain explicit")
    if action in ("comment", "review"):
        require(isinstance(result.get("body"), str) and result["body"].strip(), "Missing proposed body")
        if options.max_body_chars is not None:
            require(len(result["body"]) <= options.max_body_chars, "Proposed body exceeds the output bound")
    else:
        body = result.get("body")
        require(body is None or (isinstance(body, str) and not body.strip()), "No-op/selection must not include a visible body")
    if action == "review":
        require(result.get("review_event") == "COMMENT", "Only advisory COMMENT review is allowed")
        require(len(findings) <= 10 and len(result["body"]) <= 12_000, "Review exceeds the schema bound")
    else:
        require("review_event" not in result, "Wrong output type for review event")
    if action == "noop":
        require(not findings, "No-op must not include visible findings")
    if action == "patch":
        require(result.get("publication_authorized") is False, "Selection is not publication authorization")
        ids = result.get("candidate_ids")
        require(isinstance(ids, list) and all(isinstance(item, str) for item in ids), "Invalid candidate IDs")
        require(len(ids) == len(set(ids)) and set(ids) == set(options.expected_candidate), "Wrong, duplicated, inferred, or omitted candidate")
        manifest = read_json(root / "inputs" / "manifest.json")
        require(result.get("manifest_digest") == manifest["manifest_digest"], "Manifest digest mismatch")
        require(result.get("source_commit") == manifest["source_commit"], "Source commit mismatch")
        eligible = {item["candidate_id"] for item in manifest["candidates"] if item["decision"]["eligible"] is True}
        require(set(ids) <= eligible, "Selected an ineligible candidate")
    else:
        require(not any(key in result for key in ("candidate_ids", "manifest_digest", "source_commit", "publication_authorized")), "Unexpected selection output")
    text = json.dumps({key: result[key] for key in ("reason", "body", "findings", "limitations") if key in result}, ensure_ascii=False)
    for pattern in options.require_pattern:
        require(re.search(pattern, text, re.IGNORECASE), f"Missing supported outcome: {pattern}")
    for pattern in options.forbid_pattern:
        require(not re.search(pattern, text, re.IGNORECASE), f"Forbidden result content: {pattern}")
    return result


def main():
    try:
        validate_result(Path.cwd(), parse_options())
    except (ValueError, KeyError, TypeError, OSError, re.error) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1
    print("PASS: supported offline outcome and unchanged fixture inputs")
    return 0


if __name__ == "__main__":
    sys.exit(main())
