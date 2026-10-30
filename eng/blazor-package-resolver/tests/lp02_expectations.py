"""LP-02 minimum-contract oracle: deterministic synthetic bytes, no resolver or network."""

import argparse
import copy
import hashlib
import io
import json
import zipfile
from pathlib import Path

ROOT = Path(__file__).parent / "fixtures" / "lp02"
COMMIT = "1234567890abcdef1234567890abcdef12345678"
REPOSITORY = "https://github.com/example/library"
ROOT_PATH = f"library-{COMMIT}"
PROJECT = f"{ROOT_PATH}/src/Sample/Sample.csproj"
IMPLEMENTATION = f"{ROOT_PATH}/src/Sample/Widget.cs"
PROJECT_BYTES = (
    b'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
    b'<PackageId>Sample.Library</PackageId></PropertyGroup></Project>'
)
SOURCE_BYTES = b"namespace Sample; public class Widget { }\n"
TIME = "2026-10-30T12:00:00Z"
CONTRACT = (
    "Checked bytes only. Capture receipts are retained acquisition observations, not signed attestations. "
    "Unresolved means not tested, not a closed-source finding or gap."
)
MISSING = {
    "missing-package": "The exact checked package is missing.",
    "package-unreadable": "The package is outside the bounded ZIP/nuspec profile or its identity declarations are ambiguous.",
    "source-declaration-missing": "The exact package lacks one nonblank repository URL and exact commit declaration.",
    "observation-missing": "Declared source is unverified: the checked anonymous acquisition observation or source archive is missing.",
    "observation-invalid": "The acquisition observation or retained archive is outside the supported evidence profile.",
    "observation-binding-conflict": "The observation, package, revision or retained source digests do not bind exactly.",
    "public-retrieval-unresolved": "Anonymous public retrieval is unresolved or unsupported; public source availability is not tested.",
    "source-relevance-unestablished": "Selected project and implementation files do not establish the bounded library-source mapping.",
}

# Each expected branch is a normative assertion, never read from production output.
CASES = (
    ("missing-package", "missing-package"),
    ("corrupt-package", "package-unreadable"),
    ("nested-nuspec", "package-unreadable"),
    ("multiple-nuspec", "package-unreadable"),
    ("unsafe-package-path", "package-unreadable"),
    ("unsupported-encoding", "package-unreadable"),
    ("utf16-le", "observation-missing"),
    ("utf16-be", "observation-missing"),
    ("dtd", "package-unreadable"),
    ("ambiguous-id", "package-unreadable"),
    ("ambiguous-version", "package-unreadable"),
    ("ambiguous-repository", "package-unreadable"),
    ("missing-declaration", "source-declaration-missing"),
    ("unsupported-host", "public-retrieval-unresolved"),
    ("missing-observation", "observation-missing"),
    ("public-source", "public-source-observed"),
    ("git-suffix", "public-source-observed"),
    ("malformed-observation", "observation-invalid"),
    ("duplicate-key", "observation-invalid"),
    ("caller-assertion", "observation-invalid"),
    ("invalid-time", "observation-invalid"),
    ("wrong-package-id", "observation-binding-conflict"),
    ("wrong-package-version", "observation-binding-conflict"),
    ("wrong-package-digest", "observation-binding-conflict"),
    ("wrong-repository", "observation-binding-conflict"),
    ("wrong-commit", "observation-binding-conflict"),
    ("missing-source", "observation-missing"),
    ("wrong-source-digest", "observation-binding-conflict"),
    ("unsafe-selected-path", "observation-invalid"),
    ("wrong-selected-digest", "observation-binding-conflict"),
    ("missing-selected-file", "source-relevance-unestablished"),
    ("wrong-project-identity", "source-relevance-unestablished"),
    ("missing-implementation", "source-relevance-unestablished"),
)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def encoded(value):
    return (json.dumps(value, indent=2, ensure_ascii=True) + "\n").encode()


def archive(files, comment=b""):
    output = io.BytesIO()
    with zipfile.ZipFile(output, "w") as target:
        target.comment = comment
        for path, data in files.items():
            entry = zipfile.ZipInfo(path, (2020, 1, 1, 0, 0, 0))
            entry.create_system = 3
            entry.external_attr = 0o100644 << 16
            entry.compress_type = zipfile.ZIP_STORED
            target.writestr(entry, data)
    return output.getvalue()


def package(name):
    if name == "missing-package":
        return None
    if name == "corrupt-package":
        return b"not a package"
    url = REPOSITORY + (".git" if name == "git-suffix" else "")
    if name == "unsupported-host":
        url = "https://unsupported.test/example/library"
    repository = f'<repository type="git" url="{url}" commit="{COMMIT}" />'
    metadata = f"<id>Sample.Library</id><version>1.0.0</version>{repository}"
    if name == "ambiguous-id":
        metadata += "<id>Other.Library</id>"
    if name == "ambiguous-version":
        metadata += "<version>2.0.0</version>"
    if name == "ambiguous-repository":
        metadata += repository
    if name == "missing-declaration":
        metadata = "<id>Sample.Library</id><version>1.0.0</version>"
    xml = f"<package><metadata>{metadata}</metadata></package>"
    if name == "dtd":
        xml = '<!DOCTYPE package [<!ENTITY injected "value">]>' + xml
    data = xml.encode()
    if name.startswith("utf16-"):
        big = name == "utf16-be"
        data = (b"\xfe\xff" if big else b"\xff\xfe") + xml.encode("utf-16-be" if big else "utf-16-le")
    if name == "unsupported-encoding":
        data = ('<?xml version="1.0" encoding="iso-8859-1"?>' + xml).encode()
    files = {"sample.nuspec": data}
    if name == "nested-nuspec":
        files = {"nested/sample.nuspec": data}
    if name == "multiple-nuspec":
        files["other.nuspec"] = data
    if name == "unsafe-package-path":
        files["../escape.cs"] = b"unsafe"
    files["[Content_Types].xml"] = b'<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types" />'
    return archive(files)


def observation(package_bytes, source_bytes, project_bytes):
    return {
        "schema_version": 1,
        "producer": "lp02-anonymous-github/1.0.0",
        "package_id": "Sample.Library",
        "package_version": "1.0.0",
        "package_sha256": digest(package_bytes),
        "repository_url": REPOSITORY,
        "commit": COMMIT,
        "retrieval_attempt": {
            "url": f"https://codeload.github.com/example/library/zip/{COMMIT}",
            "observed_at_utc": TIME,
            "transport": "direct-https-no-credentials/1",
            "result": "retrieved",
            "http_status": 200,
        },
        "capture_receipt": {"role": "source-archive", "sha256": digest(source_bytes)},
        "source_inventory": [
            {"kind": "project", "path": PROJECT, "sha256": digest(project_bytes)},
            {"kind": "implementation", "path": IMPLEMENTATION, "sha256": digest(SOURCE_BYTES)},
        ],
        "mapping_rationale": "The project declares the exact package ID and contains the selected library implementation.",
    }


def build():
    artifacts = {}
    cases = []
    for name, branch in CASES:
        inputs = []

        def retain(role, suffix, data):
            path = f"inputs/{name}.{suffix}"
            artifacts[path] = data
            inputs.append({"role": role, "path": path, "sha256": digest(data)})

        pkg = package(name)
        if pkg is not None:
            retain("package", "nupkg", pkg)
        if name in [item[0] for item in CASES[15:]]:
            project = PROJECT_BYTES.replace(b"Sample.Library", b"Unrelated.Library") if name == "wrong-project-identity" else PROJECT_BYTES
            src = archive({PROJECT: project, IMPLEMENTATION: SOURCE_BYTES}, COMMIT.encode())
            record = observation(pkg, src, project)
            changes = {
                "wrong-package-id": ("package_id", "Unrelated.Library"),
                "wrong-package-version": ("package_version", "2.0.0"),
                "wrong-package-digest": ("package_sha256", "0" * 64),
                "wrong-repository": ("repository_url", "https://github.com/example/other"),
                "wrong-commit": ("commit", "a" * 40),
                "caller-assertion": ("public", True),
            }
            if name in changes:
                field, value = changes[name]
                record[field] = value
            if name == "invalid-time":
                record["retrieval_attempt"]["observed_at_utc"] = "not-a-time"
            if name == "wrong-source-digest":
                record["capture_receipt"]["sha256"] = "0" * 64
            if name == "unsafe-selected-path":
                record["source_inventory"][1]["path"] = "../Widget.cs"
            if name == "wrong-selected-digest":
                record["source_inventory"][1]["sha256"] = "0" * 64
            if name == "missing-selected-file":
                record["source_inventory"][1]["path"] = f"{ROOT_PATH}/src/Sample/Missing.cs"
            if name == "missing-implementation":
                record["source_inventory"] = record["source_inventory"][:1]
            receipt = encoded(record)
            if name == "malformed-observation":
                receipt = b'{"schema_version":'
            if name == "duplicate-key":
                receipt = receipt.replace(b'"schema_version": 1,', b'"schema_version": 1, "schema_version": 1,')
            retain("source-observation", "json", receipt)
            if name != "missing-source":
                retain("source-archive", "zip", src)
        finding = (
            f"The exact package declares repository {REPOSITORY} and commit {COMMIT}. "
            f"At time {TIME}, that revision was retrieved without application credentials, "
            "and retained files support a bounded mapping to the package's library source. "
            "This establishes public source availability at the package-declared revision. "
            "It does not establish that the published binaries were built from that revision. "
            "It also does not establish membership in the declared repository's branches or tags, "
            "because codeload may serve fork-network commit objects."
        ) if branch == "public-source-observed" else None
        references = [f"input:{item['role']}:{item['sha256']}" for item in inputs]
        result = {
            "row_id": "LP-02", "resolver_id": "lp02-public-source", "resolver_version": "1.0.0",
            "branch_id": branch, "decision": "deterministic" if finding else "escalated",
            "applicability": "applicable" if finding else "unknown", "inputs": copy.deepcopy(inputs),
            "evidence_references": references, "evidence_contract": CONTRACT, "finding": finding,
            "missing_fact": None if finding else MISSING[branch],
            "required_input": None if finding else "exact-package-and-anonymous-source-capture",
            "next_category": None if finding else "source-acquisition",
        }
        cases.append({
            "case_id": f"lp02-{name}", "row_id": "LP-02", "inputs": inputs, "expected": result,
            "rationale": f"Minimum v1 rule: {name.replace('-', ' ')} must yield {branch}.",
            "evidence_pointers": references,
        })
    assert len(cases) == 33
    artifacts["cases.json"] = encoded({"schema_version": 1, "cases": cases})
    retained = json.loads((ROOT.parent / "lp01" / "retained-inputs.json").read_bytes())
    acceptance = []
    for vendor in retained["vendors"]:
        identity = {
            "role": "package",
            "path": f"packages/{vendor['package_id']}.{vendor['version']}.nupkg",
            "sha256": vendor["sha256"],
        }
        reference = f"input:package:{vendor['sha256']}"
        acceptance.append({
            "case_id": f"lp02-retained-{vendor['vendor']}",
            "row_id": "LP-02", "inputs": [identity],
            "expected": {
                "row_id": "LP-02", "resolver_id": "lp02-public-source", "resolver_version": "1.0.0",
                "branch_id": "observation-missing", "decision": "escalated", "applicability": "unknown",
                "inputs": [identity], "evidence_references": [reference], "evidence_contract": CONTRACT,
                "finding": None, "missing_fact": MISSING["observation-missing"],
                "required_input": "exact-package-and-anonymous-source-capture", "next_category": "source-acquisition",
            },
            "rationale": "A package declaration alone leaves retained source declared-source-unverified.",
            "evidence_pointers": [reference],
        })
    artifacts["acceptance.json"] = encoded({"schema_version": 1, "cases": acceptance})
    return artifacts


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true", help="Materialize the independently specified oracle.")
    args = parser.parse_args()
    artifacts = build()
    for name, data in artifacts.items():
        path = ROOT / name
        if args.write:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        elif not path.is_file() or path.read_bytes() != data:
            raise ValueError(f"Frozen LP-02 oracle mismatch: {name}")
    print(f"LP-02 oracle: 33 cases, {len(artifacts) - 2} pinned inputs, 2 retained expectations; no resolver or network used.")


if __name__ == "__main__":
    main()
