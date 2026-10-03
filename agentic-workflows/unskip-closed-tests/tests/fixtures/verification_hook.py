import json
import os
import sys
from pathlib import Path
from xml.etree import ElementTree as ET


def write_trx(path: Path, fqn: str, behavior: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    test_run = ET.Element("TestRun")
    definitions = ET.SubElement(test_run, "TestDefinitions")
    results = ET.SubElement(test_run, "Results")

    if behavior == "zero":
        ET.ElementTree(test_run).write(path, encoding="utf-8", xml_declaration=True)
        return

    class_name, method_name = fqn.rsplit(".", 1)
    if behavior == "mismatch":
        class_name = "Fabricated.Type"

    test_id = "11111111-1111-1111-1111-111111111111"
    unit_test = ET.SubElement(definitions, "UnitTest", id=test_id, name=method_name)
    execution = ET.SubElement(unit_test, "Execution")
    execution.set("id", "22222222-2222-2222-2222-222222222222")
    test_method = ET.SubElement(unit_test, "TestMethod")
    test_method.set("className", class_name)
    test_method.set("name", method_name)

    if behavior == "partial":
        second_test = ET.SubElement(
            definitions,
            "UnitTest",
            id="33333333-3333-3333-3333-333333333333",
            name=method_name,
        )
        second_method = ET.SubElement(second_test, "TestMethod")
        second_method.set("className", class_name)
        second_method.set("name", method_name)

    outcome = "Passed" if behavior in ("pass", "mismatch") else "NotExecuted"
    if behavior in ("partial", "duplicate_result"):
        outcome = "Passed"
    result = ET.SubElement(results, "UnitTestResult")
    result.set("testId", test_id)
    result.set("testName", method_name)
    result.set("outcome", outcome)
    if behavior == "duplicate_result":
        duplicate = ET.SubElement(results, "UnitTestResult")
        duplicate.set("testId", test_id)
        duplicate.set("testName", method_name)
        duplicate.set("outcome", "Passed")
    ET.ElementTree(test_run).write(path, encoding="utf-8", xml_declaration=True)


def main() -> int:
    if len(sys.argv) < 2:
        return 90

    request_path = Path(sys.argv[-1])
    if not request_path.is_absolute():
        return 91

    command_args = sys.argv[1:-1]
    fail_when_token_missing = None
    write_extra_evidence = False
    if "--assert-no-token-environment" in command_args:
        command_args.remove("--assert-no-token-environment")
        if "GH_TOKEN" in os.environ or "GITHUB_TOKEN" in os.environ:
            return 97
    if "--write-extra-evidence" in command_args:
        command_args.remove("--write-extra-evidence")
        write_extra_evidence = True

    if command_args:
        if len(command_args) == 3 and command_args[0] == "--expire":
            evidence_path = Path(command_args[1])
            evidence = json.loads(evidence_path.read_text(encoding="utf-8"))
            for canonical in command_args[2].split(","):
                reference = evidence["references"][canonical]
                reference["kind"] = "issue"
                reference["accessible"] = True
                reference["state"] = "open"
                reference["state_reason"] = "reopened"
                reference["merged_at"] = None
            evidence_path.write_text(
                json.dumps(evidence, indent=2) + "\n", encoding="utf-8"
            )
        elif len(command_args) == 3 and command_args[0] == "--fail-when-token-missing":
            fail_when_token_missing = (command_args[1], command_args[2])
        else:
            return 96

    request = json.loads(request_path.read_text(encoding="utf-8"))
    if set(request) != {
        "schema_version",
        "candidate",
        "repository",
        "source_commit",
        "tests",
    }:
        return 92
    if (
        request["schema_version"] != "1"
        or set(request["candidate"]) != {"candidate_id"}
        or not request["candidate"]["candidate_id"]
    ):
        return 93

    for test in request["tests"]:
        if set(test) != {"fqn", "source_path", "result_file"}:
            return 94
        if not Path(test["result_file"]).is_absolute():
            return 95

    for test in request["tests"]:
        method_name = test["fqn"].rsplit(".", 1)[-1]
        if "Zero" in method_name:
            behavior = "zero"
        elif "Skipped" in method_name:
            behavior = "skipped"
        elif "Mismatch" in method_name:
            behavior = "mismatch"
        elif "PartialDefinitions" in method_name:
            behavior = "partial"
        elif "DuplicateResult" in method_name:
            behavior = "duplicate_result"
        else:
            behavior = "pass"
        if (
            fail_when_token_missing is not None
            and method_name == fail_when_token_missing[0]
            and fail_when_token_missing[1]
            not in Path(test["source_path"]).read_text(encoding="utf-8")
        ):
            behavior = "skipped"
        write_trx(Path(test["result_file"]), test["fqn"], behavior)
    if write_extra_evidence:
        request_path.with_name("unexpected.txt").write_text(
            "untrusted evidence\n", encoding="utf-8"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
