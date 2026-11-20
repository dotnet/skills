"""Regenerate source-bound simulated manifests; never run the native helper."""

import argparse
import hashlib
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parent / "unskip-closed-tests" / "fixtures"
COMMIT = "a" * 40
CONFIG_DIGEST = hashlib.sha256(b"offline fixture configuration v1").hexdigest()

# These are collector observations, not expected agent actions. The wrong-owner
# case intentionally simulates inconsistent trusted evidence for fail-closed
# planning. The legacy case is maintained separately because it is not v1.
OBSERVATIONS = {
    "completed": [("repo/Tests.cs", "Demo", "Tests", "Count", 101, "issue", "closed", "completed", True, [])],
    "merged": [("repo/Tests.cs", "Demo", "Tests", "Count", 102, "pull_request", "closed", "", True, [])],
    "unresolved": [
        ("repo/Tests.cs", "Demo", "Tests", "OpenIssue", 103, "issue", "open", "", False, ["tracking-item-open"]),
        ("repo/Tests.cs", "Demo", "Tests", "AbandonedIssue", 104, "issue", "closed", "not_planned", False, ["tracking-item-not-completed"]),
    ],
    "context": [("repo/Tests.cs", "Demo", "Tests", "Connect", 105, "issue", "closed", "completed", True, [])],
    "class": [("repo/Tests.cs", "Demo", "Tests", None, 106, "issue", "closed", "completed", True, [])],
    "partialclass": [("repo/Tests.cs", "Demo", "Tests", None, 107, "issue", "closed", "completed", False, ["partial-class", "nested-class", "incomplete-test-enumeration"])],
    "owner": [("repo/Tests.cs", "Demo", "Different", "Count", 108, "issue", "closed", "completed", True, [])],
    "stale": [("repo/Tests.cs", "Demo", "Tests", "Count", 109, "issue", "closed", "completed", True, [])],
    "modules": [
        ("repo/Core/Tests.cs", "Core", "Tests", "Count", 110, "issue", "closed", "completed", True, []),
        ("repo/Adapter/Tests.cs", "Adapter", "Tests", "Count", 110, "issue", "closed", "completed", True, []),
    ],
    "zero": [("repo/Tests.cs", "Demo", "Tests", "Count", 111, "issue", "closed", "completed", True, [])],
}


def sha256(value):
    return hashlib.sha256(value if isinstance(value, bytes) else value.encode("utf-8")).hexdigest()


def canonical_digest(value):
    return sha256(json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False))


def candidate(case, observation):
    path, namespace, typename, method, number, kind, state, state_reason, eligible, deferrals = observation
    source = (ROOT / case / Path(path)).read_bytes().replace(b"\r\n", b"\n")
    text = source.decode("utf-8")
    token = f"https://github.com/fixture/repo/{'pull' if kind == 'pull_request' else 'issues'}/{number}"
    anchor = text.index(token)
    start = text.rfind("Ignore(", 0, anchor)
    end = text.index(")", anchor) + 1
    attribute = text[start:end]
    line = text[:start].count("\n") + 1
    column = start - text.rfind("\n", 0, start)
    type_fqn = f"{namespace}.{typename}"
    declaration = f"M:{type_fqn}.{method}()" if method else f"T:{type_fqn}"
    owner_id = sha256(f"owner-v1\0fixture/repo\0{path}\0{declaration}\0{1}")
    blob_oid = hashlib.sha1(f"blob {len(source)}\0".encode("utf-8") + source).hexdigest()
    candidate_id = sha256(f"candidate-v1\0fixture/repo\0{path}\0{owner_id}\0{blob_oid}\0{start}:{end-start}\0{1}")
    test_fqns = [f"{type_fqn}.{method}"] if method else [f"{type_fqn}.First", f"{type_fqn}.Second"]
    if case == "partialclass":
        test_fqns = [f"{type_fqn}.First"]
    return {
        "candidate_id": candidate_id, "stable_owner_id": owner_id, "path": path,
        "blob_oid": blob_oid, "source_sha256": sha256(source),
        "attribute_span": {"start": start, "length": end-start, "start_line": line, "start_column": column, "end_line": line, "end_column": column+end-start},
        "attribute_text_sha256": sha256(attribute), "attribute_type": "Microsoft.VisualStudio.TestTools.UnitTesting.IgnoreAttribute",
        "owner": {"kind": "method" if method else "class", "namespace": namespace, "containing_types": [typename], "type_fqn": type_fqn, "declaration_id": declaration, "method_name": method or "", "method_signature": f"{method}()" if method else "", "test_fqns": test_fqns},
        "canonical_issue_references": [{"kind": kind, "owner": "fixture", "repo": "repo", "number": number, "canonical": f"fixture/repo#{number}", "url": token, "eligibility": eligible, "state": state, "state_reason": state_reason, "merged_at": "2026-09-30T12:00:00Z" if kind == "pull_request" else None}],
        "decision": {"eligible": eligible, "deferrals": deferrals},
    }


def documents():
    for case, observations in OBSERVATIONS.items():
        manifest = {
            "schema_version": "1", "repository": "fixture/repo", "source_commit": COMMIT,
            "git_object_format": "sha1", "config_digest": CONFIG_DIGEST,
            "candidate_count": len(observations), "candidates": [candidate(case, item) for item in observations],
        }
        manifest["manifest_digest"] = canonical_digest(manifest)
        context = {
            "environment": {
                "GH_AW_UNSKIP_MANIFEST": "manifest.json",
                "GH_AW_UNSKIP_SOURCE_COMMIT": "b" * 40 if case == "stale" else COMMIT,
                "GH_AW_UNSKIP_MANIFEST_DIGEST": manifest["manifest_digest"],
            },
            "required_schema_version": "1",
            "current_default_branch_commit": "b" * 40 if case == "stale" else COMMIT,
        }
        if case == "context":
            context["tracking_context"] = "tracking-context.json"
        if case == "zero":
            context["prior_verification_observation"] = "verification-observation.json"
        for name, document in (("manifest.json", manifest), ("context.json", context)):
            yield ROOT / case / name, json.dumps(document, indent=2, ensure_ascii=False) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Check committed generated evidence without writing it.")
    args = parser.parse_args()
    for path, content in documents():
        if args.check:
            actual = path.read_bytes().replace(b"\r\n", b"\n").decode("utf-8")
            if actual != content:
                raise ValueError(f"Generated fixture drift: {path.relative_to(ROOT)}")
        else:
            path.write_text(content, encoding="utf-8", newline="\n")
    print("Source-bound simulated fixture manifests are consistent.")


if __name__ == "__main__":
    main()
