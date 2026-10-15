"""Correlate the executed fixture's scheduling policy with the final report."""

import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys


def verified_bytes(path, expected):
    data = path.read_bytes()
    assert hashlib.sha256(data.replace(b"\r\n", b"\n")).hexdigest() == expected, (
        f"Evaluator resource changed: {path}"
    )
    return data


def validate_report(policy, response):
    assert policy in {"assembly-serial", "deferred-members", "shared-resource"}, policy
    assert isinstance(response, str) and response.strip(), "Missing final report"
    normalized = re.sub(r"[`*_]", "", response).replace("\u2019", "'")
    normalized = re.sub(r"\s+", " ", normalized)
    normalized = re.sub(r"\bunlike\s+xunit(?:\.net)?\s*,\s*", "", normalized, flags=re.I)
    unrelated = r"\b(?:unrelated|other)\s+(?:(?:test\s+)?(?:classes|tests)|collections)\b"
    members = (
        r"\b(?:(?:these|those|the)\s+(?:(?:deferred|serialized|shared|collection|two)\s+)?"
        r"(?:members|classes|tests)|(?:deferred|serialized)\s+members|FirstMember|SecondMember)\b"
    )
    peers = r"\b(?:each other|one another|themselves)\b"
    parallel = r"\b(?:parallel|concurrent(?:ly)?|overlaps?|overlapping)\b"
    serial = (
        r"\b(?:serial(?:izes?|izing|ized|ly)?|sequential(?:ly)?|nonparallel|blocks?|blocked|"
        rf"prevent(?:s|ed|ing)?(?=\s+from\s+(?:(?:running|executing)\s+(?:in\s+)?)?{parallel}))\b"
    )
    negation = r"\b(?:cannot|can't|don't|doesn't|won't|isn't|aren't|not|never|no longer)\b"
    continuation = (
        r"(?:can|cannot|can't|may|must|do|does|don't|doesn't|will|won't|are|aren't|"
        r"remain|continue|not|never|no longer|also)\b"
    )
    claims = set()
    previous_subject = None
    previous_historical = False
    for sentence in re.split(r"[.!?;\n]+|\b(?:whereas|unlike)\b", normalized, flags=re.I):
        subject = None
        historical = False
        previous_predicate = None
        # Keep coordinated overlap targets together, but separate their predicates.
        separator = (
            r",(?=\s*(?:MSTest|xUnit)\b)|\b(?:but|yet|however|while)\b|"
            rf"\band(?=\s+(?:{continuation}|they\s+{continuation}|{unrelated}|{members}\s+(?:can|cannot|may|are)\b))"
        )
        for clause in re.split(separator, sentence, flags=re.I):
            clause = re.sub(r"\s+", " ", clause).strip(" ,")
            if previous_predicate and not re.search(
                parallel + "|" + serial, re.sub(members, "", clause, flags=re.I), re.I
            ):
                clause = re.sub(
                    rf"^((?:they\s+)?(?:{continuation})\s+)(?=(?:with\s+)?(?:{members}|{unrelated}|{peers}))",
                    lambda match: match[0] + previous_predicate + " ", clause, flags=re.I,
                )
            if re.match(r"^(?:in\s+)?xunit\b", clause, re.I):
                historical = True
            elif re.match(r"^mstest\b", clause, re.I):
                historical = False
            pronoun = re.match(r"^they\s+", clause, re.I)
            if pronoun and re.search(parallel + "|" + serial, clause, re.I):
                assert previous_subject, "Scheduling disclosure has an unresolved pronoun"
                clause = previous_subject + " " + clause[pronoun.end():]
                historical = previous_historical
            if subject and re.match(continuation, clause, re.I):
                clause = subject + " " + clause
            actor = re.search(unrelated + "|" + members, clause, re.I)
            if actor:
                subject = actor[0]
                previous_subject = subject
                previous_historical = historical
            if historical:
                continue
            if not re.search(unrelated, clause, re.I):
                continue
            # Member adjectives (for example, "serialized members") are not predicates.
            predicate_text = re.sub(members, "", clause, flags=re.I)
            predicate = re.search(parallel + "|" + serial, predicate_text, re.I)
            if not predicate:
                continue
            previous_predicate = predicate[0]
            negated = bool(re.search(negation, predicate_text[:predicate.start()], re.I))
            permitted = bool(re.fullmatch(parallel, predicate[0], re.I)) != negated
            targets = []
            if re.search(members, clause, re.I):
                targets.append("members")
            if re.search(peers, clause, re.I):
                targets.append("peers")
            claims.update((target, permitted) for target in targets or ["unspecified"])

    if policy == "shared-resource":
        assert claims & {("members", True), ("unspecified", True)} and all(
            permitted for _, permitted in claims
        ), (
            "Scheduling disclosure contradicts shared-resource policy: "
            "unrelated classes remain eligible for parallel execution"
        )
    elif policy == "deferred-members":
        assert ("members", False) in claims and claims <= {
            ("members", False), ("peers", True),
        }, (
            "Scheduling disclosure contradicts deferred-members: unrelated classes "
            "may overlap each other, but cannot overlap these deferred members"
        )
    else:
        assert claims & {("peers", False), ("unspecified", False)} and not any(
            permitted for _, permitted in claims
        ), (
            f"Scheduling disclosure contradicts {policy}: "
            "unrelated classes must be disclosed as serialized with each other too"
        )


def main():
    workspace = Path(os.environ["EVALUATE_WORKSPACE"]).resolve()
    grading = json.loads(Path(os.environ["EVALUATE_GRADER_INPUT"]).read_text(encoding="utf-8"))
    guard = verified_bytes(
        workspace / ".eval" / "CollectionSchedulingContract.txt",
        "0a47547396ad65fc8373f5b8a559e171ba20ad5696362c2a5fae4f01ade543d3",
    )
    verified_bytes(
        workspace / ".eval" / "migration_evidence.py",
        "9ded8edba1cdcdad78385a79b58919cf394758a1768d79662a9260595f8f5ed2",
    )
    (workspace / "CollectionSchedulingContract.cs").write_bytes(guard)
    result = subprocess.run(
        [
            sys.executable, ".eval/migration_evidence.py", "run",
            "--project", "TestProject.csproj", "--runner", "vstest",
            "--mstest-major", "4", "--lifecycle",
            "--expect", "FirstCase=Passed", "--expect", "SecondCase=Passed",
            "--unchanged-file",
            "bbadd2d5430f817f830d9a34899bc1bc9d99aae2dcd20815a4d1615b28c2f7b5:LifecycleProbe.cs",
        ],
        cwd=workspace, capture_output=True, text=True, encoding="utf-8",
        errors="replace", timeout=210,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    evidence = json.loads(result.stdout)
    reports = list(evidence["reports"])
    assert len(reports) == 1, reports
    policy_path = Path(reports[0]).parent / "lifecycle.scheduling.json"
    policy = policy_path.read_text(encoding="utf-8")
    validate_report(policy, grading["trajectory"]["output"])
    print(json.dumps({
        "name": "collection-policy-report", "kind": "code",
        "passed": True, "score": 1,
        "evidence": f"Runtime lifecycle and report match scheduling policy: {policy}",
    }))


if __name__ == "__main__":
    main()
