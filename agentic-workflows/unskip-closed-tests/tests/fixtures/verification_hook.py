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

    outcome = "Passed" if behavior in ("pass", "mismatch") else "NotExecuted"
    result = ET.SubElement(results, "UnitTestResult")
    result.set("testId", test_id)
    result.set("testName", method_name)
    result.set("outcome", outcome)
    ET.ElementTree(test_run).write(path, encoding="utf-8", xml_declaration=True)


def main() -> int:
    if len(sys.argv) < 2:
        return 90

    request_path = Path(sys.argv[-1])
    if not request_path.is_absolute():
        return 91

    command_args = sys.argv[1:-1]
    if "--assert-no-token-environment" in command_args:
        command_args.remove("--assert-no-token-environment")
        if "GH_TOKEN" in os.environ or "GITHUB_TOKEN" in os.environ:
            return 97

    if command_args:
        if len(command_args) != 3 or command_args[0] != "--expire":
            return 96
        evidence_path = Path(command_args[1])
        evidence = json.loads(evidence_path.read_text(encoding="utf-8"))
        for canonical in command_args[2].split(","):
            reference = evidence["references"][canonical]
            reference["kind"] = "issue"
            reference["accessible"] = True
            reference["state"] = "open"
            reference["state_reason"] = "reopened"
            reference["merged_at"] = None
        evidence_path.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")

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

    method_name = request["tests"][0]["fqn"].rsplit(".", 1)[-1]
    if "Zero" in method_name:
        behavior = "zero"
    elif "Skipped" in method_name:
        behavior = "skipped"
    elif "Mismatch" in method_name:
        behavior = "mismatch"
    else:
        behavior = "pass"

    for test in request["tests"]:
        write_trx(Path(test["result_file"]), test["fqn"], behavior)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
