from __future__ import annotations

import hashlib
import json
import shutil
import subprocess
import sys
from pathlib import Path


REPAIR_INPUT_HASHES = {
    "diagnosis-contract.json": "ea59db3b011aa280f695ff752c23e103dc7a57d4bafc10883d55f7ea955e3eae",
    "snapshots/WorkloadDependencies.json": "f6ff45f0366a55af7550f0b8cea248429824193c3a7ebd7172143ee2b9e960d6",
    "snapshots/android-packages.txt": "7b143bbc6ccfca5b7da1c5b00027b1b1417aaba55de66e89b67188e7d99c1b68",
    "snapshots/dotnet-info.txt": "8a3e684103455d82f0a73adebbbac0834983188295c57014739d7e57cf5c4e8a",
    "snapshots/host.txt": "dbe8050d95ac832f9a7b6f4d844bcd9a841b91822ca4ae3553eaa3009ff68cd3",
    "snapshots/java-version.txt": "42db1154ebffddcaa3b27a19f8b42a942d0d0db161c6f683acd55d280545b52b",
    "snapshots/microsoft-openjdk-policy.json": "f306db3099033322059886eb15e853beeabf1497a70dd6b0abdecf1f0d4de21e",
    "snapshots/workload-list.txt": "04a558ecc89133f69b823efe4440254da8b7f26575dfc8f2ede3b11c787d354e",
}
REPAIR_ALLOWED_FILES = sorted(
    [*REPAIR_INPUT_HASHES, "diagnosis.json", "diagnosis.md"]
)
NOOP_HASHES = {
    "diagnosis.json": "f1b8b5ceea5b0ff2bb4489127097519360368d70bdae6e7a70dfd04b62902f7d",
    "diagnosis.md": "3fbf2ebeab8de039143cf5ce7e0c2a26619e6c407f83481f870f7c477ef3fc54",
    "snapshot.json": "074be68bc9e4bd38ae160cef5e7d5dc237b6f96da4c140a029f37eb8f18ec30f",
}


def fail(message: str) -> None:
    raise SystemExit(message)


def normalized_hash(path: Path) -> str:
    return hashlib.sha256(path.read_bytes().replace(b"\r\n", b"\n")).hexdigest()


def file_set(root: Path) -> list[str]:
    return sorted(
        path.relative_to(root).as_posix()
        for path in root.rglob("*")
        if path.is_file()
    )


def reject_duplicate_keys(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def load_json(path: Path):
    try:
        return json.loads(
            path.read_text(encoding="utf-8"),
            object_pairs_hook=reject_duplicate_keys,
        )
    except (OSError, UnicodeError, json.JSONDecodeError, ValueError) as exc:
        fail(f"{path.name} is not valid unambiguous JSON: {exc}")


def verify_repair(root: Path) -> None:
    actual_files = file_set(root)
    if actual_files != REPAIR_ALLOWED_FILES:
        fail(
            "Repair fixture file set changed: "
            f"expected {REPAIR_ALLOWED_FILES}, got {actual_files}"
        )
    for relative, expected_hash in REPAIR_INPUT_HASHES.items():
        if normalized_hash(root / relative) != expected_hash:
            fail(f"Immutable repair evidence changed: {relative}")

    manifest = load_json(root / "snapshots" / "WorkloadDependencies.json")
    if set(manifest) != {"microsoft.net.sdk.android"}:
        fail("Manifest root must contain only microsoft.net.sdk.android")
    android_workload = manifest["microsoft.net.sdk.android"]
    if not isinstance(android_workload, dict) or set(android_workload) != {
        "workload",
        "jdk",
        "androidsdk",
    }:
        fail("microsoft.net.sdk.android has an unexpected schema")

    workload = android_workload.get("workload")
    jdk = android_workload.get("jdk")
    android_sdk = android_workload.get("androidsdk")
    if (
        not isinstance(workload, dict)
        or set(workload) != {"alias", "version"}
        or workload.get("alias") != ["android"]
        or not isinstance(workload.get("version"), str)
    ):
        fail("Manifest workload metadata is invalid")
    if not isinstance(jdk, dict) or set(jdk) != {"version", "recommendedVersion"}:
        fail("Manifest JDK data must contain version and recommendedVersion only")
    if "vendor" in jdk:
        fail("JDK vendor must not be inferred from WorkloadDependencies.json")
    recommended_version = jdk.get("recommendedVersion")
    if not isinstance(recommended_version, str) or not recommended_version:
        fail("Manifest JDK recommendedVersion is missing")

    if not isinstance(android_sdk, dict) or set(android_sdk) != {"packages"}:
        fail("Manifest androidsdk object has an unexpected schema")
    packages = android_sdk.get("packages")
    if not isinstance(packages, list) or not packages:
        fail("Manifest Android package list must be non-empty")
    required_packages: list[str] = []
    for package in packages:
        if not isinstance(package, dict) or set(package) != {
            "desc",
            "sdkPackage",
            "optional",
        }:
            fail("Manifest package entry has an unexpected schema")
        if not isinstance(package.get("desc"), str) or not package["desc"]:
            fail("Manifest package description is missing")
        if package.get("optional") not in {"true", "false"}:
            fail("Manifest package optional flag must be a string boolean")
        sdk_package = package.get("sdkPackage")
        if not isinstance(sdk_package, dict) or "id" not in sdk_package:
            fail("Manifest package sdkPackage.id is missing")
        package_id = sdk_package["id"]
        if not (
            isinstance(package_id, str)
            or (
                isinstance(package_id, dict)
                and package_id
                and all(
                    isinstance(key, str)
                    and isinstance(value, str)
                    and value
                    for key, value in package_id.items()
                )
            )
        ):
            fail("Manifest package id must be a string or platform map")
        if package["optional"] == "false" and isinstance(package_id, str):
            required_packages.append(package_id)
    if len(required_packages) != len(set(required_packages)):
        fail("Required Android package identifiers must be unique")

    policy = load_json(root / "snapshots" / "microsoft-openjdk-policy.json")
    if set(policy) != {"policy", "jdk"} or policy.get("policy") != "captured-android-toolchain-policy":
        fail("Captured JDK policy has an unexpected schema")
    policy_jdk = policy.get("jdk")
    if not isinstance(policy_jdk, dict) or set(policy_jdk) != {
        "supportedVendor",
        "distribution",
    }:
        fail("Captured JDK policy is incomplete")
    recommended_vendor = policy_jdk.get("supportedVendor")
    if recommended_vendor != "Microsoft" or policy_jdk.get("distribution") != "Microsoft OpenJDK":
        fail("Captured JDK vendor policy is not Microsoft OpenJDK")

    installed_packages = {
        line.strip()
        for line in (root / "snapshots" / "android-packages.txt")
        .read_text(encoding="utf-8")
        .splitlines()
        if line.strip()
    }
    missing_packages = sorted(set(required_packages) - installed_packages)
    java_version = (root / "snapshots" / "java-version.txt").read_text(encoding="utf-8")
    if "Temurin" not in java_version:
        fail("Captured Java evidence no longer identifies Temurin")

    contract = load_json(root / "diagnosis-contract.json")
    diagnosis = load_json(root / "diagnosis.json")
    report = (root / "diagnosis.md").read_text(encoding="utf-8")
    if diagnosis.get("status") != contract.get("status"):
        fail("Diagnosis status does not match diagnosis-contract.json")
    if diagnosis.get("no_actions_executed") is not contract.get("no_actions_executed"):
        fail("Diagnosis must record that no installation actions were executed")
    issue_list = diagnosis.get("issues")
    if not isinstance(issue_list, list):
        fail("Diagnosis issues must be a list")
    issues = {issue.get("id"): issue for issue in issue_list if isinstance(issue, dict)}
    issue_contract = contract.get("issues")
    if not isinstance(issue_contract, dict):
        fail("Diagnosis contract issues must be an object")
    if len(issues) != len(issue_list) or set(issues) != set(issue_contract):
        fail("Diagnosis issue ids do not match diagnosis-contract.json")
    for issue_id, definition in issue_contract.items():
        required_fields = definition.get("fields") if isinstance(definition, dict) else None
        if not isinstance(required_fields, list) or not all(
            isinstance(field, str) for field in required_fields
        ):
            fail(f"Diagnosis contract fields are invalid for {issue_id}")
        if not set(required_fields).issubset(issues[issue_id]):
            fail(f"Diagnosis issue is missing contract fields: {issue_id}")

    jdk_issue = issues["jdk-vendor"]
    if jdk_issue.get("detected_vendor") != "Temurin":
        fail("Diagnosis must report the captured Temurin vendor")
    if jdk_issue.get("recommended_vendor") != recommended_vendor:
        fail("JDK vendor recommendation does not match the captured policy")
    if jdk_issue.get("recommended_version") != recommended_version:
        fail("JDK version recommendation does not match the nested manifest")
    if issues["android-packages"].get("missing") != missing_packages:
        fail("Android package diagnosis does not match manifest minus installed inventory")

    required_report = (
        f"Microsoft OpenJDK {recommended_version}",
        *missing_packages,
    )
    missing_report = [value for value in required_report if value not in report]
    if missing_report:
        fail("Diagnosis report is incomplete: " + ", ".join(missing_report))
    report_lower = report.lower()
    if "degraded" not in report_lower:
        fail("Diagnosis report must state the degraded result")
    if not (
        ("no installation" in report_lower or "no install" in report_lower)
        and ("run" in report_lower or "execut" in report_lower)
    ):
        fail("Diagnosis report must state that no installation action was executed")
    if "workload update" in report.lower() or "workload repair" in report.lower():
        fail("Diagnosis report recommends an unsafe workload command")


def verify_noop(root: Path) -> None:
    actual = file_set(root)
    expected = sorted(NOOP_HASHES)
    if actual != expected:
        fail(f"Healthy fixture file set changed: expected {expected}, got {actual}")
    for relative, expected_hash in NOOP_HASHES.items():
        if normalized_hash(root / relative) != expected_hash:
            fail(f"Healthy fixture changed: {relative}")


def expect_failure(action, label: str) -> None:
    try:
        action()
    except (SystemExit, ValueError):
        return
    fail(f"Mutation unexpectedly passed: {label}")


def run_probes() -> None:
    suite = Path(__file__).resolve().parents[1]
    repo = suite.parents[2]
    probe = suite / "validator-probe"
    shutil.rmtree(probe, ignore_errors=True)
    try:
        repair = probe / "repair-environment"
        shutil.copytree(suite / "fixtures" / "repair-environment", repair)
        expect_failure(lambda: verify_repair(repair), "broken fixture")
        subprocess.run(
            [
                "git",
                "apply",
                f"--directory={(probe.relative_to(repo)).as_posix()}",
                str((suite / "references" / "repair-diagnosis.patch").relative_to(repo)),
            ],
            check=True,
            cwd=repo,
        )
        verify_repair(repair)
        immutable = {
            relative: (repair / relative).read_bytes()
            for relative in REPAIR_INPUT_HASHES
        }

        evidence = repair / "snapshots" / "java-version.txt"
        evidence.write_text("Microsoft OpenJDK", encoding="utf-8")
        expect_failure(lambda: verify_repair(repair), "evidence tampering")
        evidence.write_bytes(immutable["snapshots/java-version.txt"])

        manifest_path = repair / "snapshots" / "WorkloadDependencies.json"
        manifest_path.write_text("{", encoding="utf-8")
        expect_failure(lambda: verify_repair(repair), "malformed manifest")
        manifest_path.write_bytes(immutable["snapshots/WorkloadDependencies.json"])

        manifest = load_json(manifest_path)
        package_list = manifest["microsoft.net.sdk.android"]["androidsdk"]["packages"]
        package_list.append(package_list[0])
        manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
        expect_failure(lambda: verify_repair(repair), "duplicate package")
        manifest_path.write_bytes(immutable["snapshots/WorkloadDependencies.json"])

        (repair / "snapshots" / "extra.txt").write_text("unexpected", encoding="utf-8")
        expect_failure(lambda: verify_repair(repair), "extra evidence file")
        verify_noop(suite / "fixtures" / "healthy-environment")
    finally:
        shutil.rmtree(probe, ignore_errors=True)


def main() -> None:
    if len(sys.argv) == 2 and sys.argv[1] == "probe":
        run_probes()
        return
    if len(sys.argv) != 3:
        fail("usage: validate_fixture.py <repair|noop> <fixture-root> | probe")
    mode, root_arg = sys.argv[1:]
    root = Path(root_arg)
    if mode == "repair":
        verify_repair(root)
    elif mode == "noop":
        verify_noop(root)
    else:
        fail(f"unknown mode: {mode}")


if __name__ == "__main__":
    main()
