"""LP-04 Phase A: construction-fact oracle, not a package/license scanner or production resolver."""

import argparse
import copy
import hashlib
import io
import json
import os
import re
import tempfile
import zipfile
from pathlib import Path

ROOT = Path(__file__).parent / "fixtures" / "lp04"
PARENT = "d62478f05df2a520a6d81afca6c289342e6bad06"
FACTS = {}
CONTRACT = (
    "Qualified exact-byte notice coverage for the checked root and explicit TFM/all-root-entry scope. "
    "Admitted complete dependency and asset captures, prevalidated whole-subject obligation reviews, "
    "and exhaustive reviewed mappings are trust boundaries, not authenticated by hashes or references. "
    "No legal advice, LP-03 acceptability, build/source correspondence, ownership, vulnerability/support "
    "conclusion, or proof that the supplied obligation review is legally correct."
)
BRANCHES = {
    "missing-package": ("not tested", "The exact root package is missing.", "package", "package-acquisition"),
    "scope-missing": ("not tested", "An explicit exact-root and assessment scope is missing.", "assessment", "assessment-scope"),
    "evidence-missing": ("not tested", "The notice-coverage packet or an inventory/admission document is missing.", "notice-coverage-v1", "notice-acquisition"),
    "input-invalid": ("not tested", "Evidence is outside the bounded unambiguous supported profile.", "supported-evidence", "evidence-repair"),
    "binding-conflict": ("not tested", "Checked root, inventory, subject, review or packaged-entry bindings disagree.", "consistent-bindings", "evidence-repair"),
    "inventories-unresolved": ("not tested", "Complete resolved dependencies and all-root-entry asset inspection are not established.", "complete-inventories", "notice-acquisition"),
    "subject-bytes-missing": ("not tested", "An exact inventoried dependency or bundled asset is unavailable.", "subject-bytes", "notice-acquisition"),
    "reviews-unresolved": ("not tested", "Whole-subject applicability or supported retained obligation evidence is incomplete.", "obligation-reviews", "notice-investigation"),
    "mapping-unresolved": ("not tested", "Exhaustive reviewed mappings or supported exact representations are incomplete.", "notice-mapping", "notice-investigation"),
    "owner-evidence-required": ("owner evidence required", "An admitted review establishes a genuinely private owner fact necessary to resolve an obligation.", "private-owner-fact", "notice-owner"),
    "required-representation-missing": ("gap", None, None, None),
    "notice-coverage-satisfied": ("verified", None, None, None),
    "no-applicable-surface": ("not applicable", None, None, None),
}

# Normative results are specified independently of classify(), including each weakened counterexample.
# A None mutation is an existing-branch witness, not a claimed mutation execution.
CASES = (
    ("missing-package", "missing-package", "root-required", None, None, "Root bytes precede all row checks."),
    ("missing-scope", "scope-missing", "scope-required", None, None, "Never infer scope from successful captures."),
    ("missing-packet", "evidence-missing", "packet-required", None, None, "LICENSE and detected sidecars alone do not prove coverage."),
    ("all-sources-covered", "notice-coverage-satisfied", "exact-positive", None, None, "Two TFMs share exact direct/transitive versions; one entry covers multiple obligations; every part of a multipart obligation binds, including 16384-byte text."),
    ("no-applicable-surface", "no-applicable-surface", "captured-empty", None, None, "Empty complete dependency and asset inventories establish scoped non-applicability."),
    ("reviewed-no-obligations", "no-applicable-surface", "reviewed-none", None, None, "Nonempty inventories need admitted retained whole-subject no-obligation reviews, not required=false."),
    ("aggregate-shared-entry", "notice-coverage-satisfied", "aggregate-segments", None, None, "One explicitly admitted aggregate represents multiple subjects and parts at exact byte offsets."),
    ("direct-absent", "required-representation-missing", "direct-absence", "absence-is-unresolved", "mapping-unresolved", "A complete reviewed intended location is absent in exact root inspection; other obligations are resolved."),
    ("direct-partial", "required-representation-missing", "direct-prefix", "partial-is-unresolved", "mapping-unresolved", "A mapped full entry is a strict nonempty prefix of the required retained bytes, not an arbitrary modified text."),
    ("missing-dependency-inventory", "evidence-missing", "dependency-inventory-required", None, None, "An asset capture does not substitute for the resolved dependency inventory."),
    ("incomplete-dependency-inventory", "inventories-unresolved", "graph-counts", "graph-counts", "notice-coverage-satisfied", "Only the admitted graph node count differs; all bytes/reviews/mappings otherwise bind."),
    ("unresolved-graph", "inventories-unresolved", "graph-diagnostics", "graph-diagnostics", "notice-coverage-satisfied", "Capture diagnostics prevent a complete-closure conclusion."),
    ("missing-asset-inventory", "evidence-missing", "asset-inventory-required", None, None, "A dependency capture does not substitute for the bundled-asset inventory."),
    ("incomplete-asset-inventory", "inventories-unresolved", "all-root-entries", "all-root-entries", "notice-coverage-satisfied", "A passive root LICENSE entry alone is omitted; third-party assets and their mappings remain present."),
    ("unreviewed-asset-entry", "inventories-unresolved", "asset-classification", "asset-classification", "notice-coverage-satisfied", "An unresolved all-entry classification cannot be silently assumed first-party."),
    ("missing-dependency-bytes", "subject-bytes-missing", "dependency-bytes-required", None, None, "Root notice presence does not supply an unavailable dependency archive."),
    ("missing-asset-bytes", "subject-bytes-missing", "asset-bytes-required", None, None, "An inventoried bundled asset requires separately checked exact bytes, even when present in the root."),
    ("missing-reviews", "reviews-unresolved", "reviews-required", None, None, "Complete inventories and SPDX metadata alone are insufficient without applicability/obligation reviews."),
    ("unresolved-review", "reviews-unresolved", "review-disposition", "review-disposition", "notice-coverage-satisfied", "Unresolved dependency applicability and an unsupported generated-bundle review form use the same conservative review gate; all retained parts and mappings are intact."),
    ("missing-mapping", "mapping-unresolved", "mapping-required", None, None, "Obligation evidence cannot substitute for packaged notice coverage."),
    ("incomplete-mapping", "mapping-unresolved", "mapping-equality", "mapping-equality", "notice-coverage-satisfied", "One required part lacks a mapping, not a proven missing representation."),
    ("wrong-root-identity", "binding-conflict", "root-identity", "root-identity", "notice-coverage-satisfied", "All documents agree on the wrong root version; only observed archive identity refutes them."),
    ("wrong-root-digest", "binding-conflict", "root-digest", "root-digest", "notice-coverage-satisfied", "All document root digests agree with one another, not exact root bytes."),
    ("wrong-scope-binding", "binding-conflict", "scope-digest", "scope-digest", "notice-coverage-satisfied", "Only the packet assessment digest is wrong."),
    ("wrong-inventory-binding", "binding-conflict", "document-digests", "document-digests", "notice-coverage-satisfied", "Only the packet dependency-document digest differs; admission and loader bindings remain correct."),
    ("wrong-asset-binding", "binding-conflict", "asset-source-binding", "asset-source-binding", "notice-coverage-satisfied", "Retained asset bytes and inventory subject digest agree with each other, not the actual root entry."),
    ("wrong-review-binding", "binding-conflict", "review-subject-binding", "review-subject-binding", "notice-coverage-satisfied", "Only the review's exact subject digest is wrong."),
    ("wrong-entry-binding", "binding-conflict", "entry-source-binding", "entry-source-binding", "notice-coverage-satisfied", "Mapping digest and retained entry agree with each other, not the packaged notice entry."),
    ("unknown-field", "input-invalid", "exact-fields", "exact-fields", "notice-coverage-satisfied", "An extra complete=true assertion is never an admission protocol."),
    ("duplicate-json-field", "input-invalid", "unique-json-fields", "unique-json-fields", "notice-coverage-satisfied", "Duplicate identical protocol fields must not be last-wins."),
    ("unsafe-path", "input-invalid", "safe-paths", "safe-paths", "binding-conflict", "A parent traversal packaged mapping path is rejected before entry lookup."),
    ("resource-limit", "input-invalid", "retained-byte-limit", "retained-byte-limit", "notice-coverage-satisfied", "16385-byte reference text is outside the inclusive 16384-byte profile."),
    ("unaccepted-admission", "input-invalid", "admission-profiles", "admission-profiles", "notice-coverage-satisfied", "A caller-authored admission producer is unsupported even with matching hashes."),
    ("dependency-identity", "binding-conflict", "dependency-identity-binding", "dependency-identity-binding", "notice-coverage-satisfied", "All dependency facts name the wrong version but the archive identity does not."),
    ("modified-license", "mapping-unresolved", "modified-text", "modified-text", "notice-coverage-satisfied", "Arbitrary modified text is not exact coverage and is not automatically a legally incomplete grant."),
    ("acquisition-unavailable", "inventories-unresolved", "successful-captures", "capture-success", "notice-coverage-satisfied", "Retained partial documents and matching hashes do not override the admitted acquisition-unavailable state."),
    ("duplicate-package-path", "input-invalid", "unique-package-paths", "unique-package-paths", "notice-coverage-satisfied", "Even byte-identical duplicate archive entries are ambiguous."),
    ("alias-package-path", "input-invalid", "no-path-aliases", "no-path-aliases", "notice-coverage-satisfied", "Case-equivalent archive entries are not selected by runtime-dependent lookup."),
    ("multi-tfm-second-version-unresolved", "reviews-unresolved", "all-tfm-versions", "all-tfm-versions", "notice-coverage-satisfied", "Only the distinct version unique to TFM two has unresolved applicability."),
    ("owner-before-gap", "owner-evidence-required", "private-before-gap", "gap-before-owner", "required-representation-missing", "An admitted retained private-contract fact, not failed public acquisition, preempts another complete missing-notice counterexample."),
    ("unresolved-before-gap", "mapping-unresolved", "unresolved-before-gap", "gap-before-unresolved", "required-representation-missing", "One absent mapping preempts another fully observed missing representation."),
    ("later-binding-before-unresolved", "binding-conflict", "bindings-before-missing", "early-missing", "subject-bytes-missing", "A first-node missing archive cannot hide a later-node review digest conflict."),
    ("unaccepted-aggregate", "mapping-unresolved", "aggregate-admission", "aggregate-admission", "notice-coverage-satisfied", "Exact aggregate substrings without explicit aggregate acceptance are insufficient."),
)

# Independent finite inventory: removing a case/registration must not remove its obligation.
CRITICAL_GUARDS = (
    "root-required", "scope-required", "packet-required", "exact-positive", "captured-empty",
    "reviewed-none", "aggregate-segments", "direct-absence", "direct-prefix",
    "dependency-inventory-required", "graph-counts", "graph-diagnostics",
    "asset-inventory-required", "all-root-entries", "asset-classification",
    "dependency-bytes-required", "asset-bytes-required", "reviews-required",
    "review-disposition", "mapping-required", "mapping-equality", "root-identity",
    "root-digest", "scope-digest", "document-digests", "asset-source-binding",
    "review-subject-binding", "entry-source-binding", "exact-fields", "unique-json-fields",
    "safe-paths", "retained-byte-limit", "admission-profiles", "dependency-identity-binding",
    "modified-text", "successful-captures", "unique-package-paths", "no-path-aliases",
    "all-tfm-versions", "private-before-gap", "unresolved-before-gap",
    "bindings-before-missing", "aggregate-admission",
    "loader-duplicate-role", "loader-safe-path", "loader-file-presence",
    "loader-digest", "loader-exact-fields", "retained-syncfusion", "retained-infragistics",
)
DOC_ROLES = {
    "dependencies": "dependency-inventory", "assets": "asset-inventory",
    "reviews": "obligation-reviews", "mapping": "notice-mapping",
}
BASE_ROLES = {"package", "assessment", "notice-coverage", "admission", *DOC_ROLES.values()}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def encoded(value):
    return (json.dumps(value, indent=2, ensure_ascii=True) + "\n").encode("ascii")


def fields(value, names, weakened=None):
    require(type(value) is dict and (set(value) == set(names.split()) or
            weakened == "exact-fields" and set(names.split()) <= set(value)), "invalid-fields")


def text(value, maximum=256):
    require(type(value) is str and 0 < len(value) <= maximum and
            all(32 <= ord(char) <= 126 for char in value) and value.strip(), "invalid-text")


def sha(value):
    require(type(value) is str and re.fullmatch(r"[a-f0-9]{64}", value) is not None, "invalid-sha256")


def token(value):
    require(type(value) is str and re.fullmatch(r"[a-z0-9][a-z0-9.-]{0,63}", value) is not None, "invalid-token")


def safe_path(value):
    return (type(value) is str and 0 < len(value) <= 256 and
            re.fullmatch(r"[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)*", value) is not None and
            all(part not in (".", "..") and not part.endswith((".", " ")) and
                part.split(".")[0].lower() not in ("con", "prn", "aux", "nul",
                    *(f"com{i}" for i in range(1, 10)), *(f"lpt{i}" for i in range(1, 10)))
                for part in value.split("/")))


def path_field(value, weakened):
    require(weakened == "safe-paths" or safe_path(value), "unsafe-path")


def identity(value, weakened=None):
    fields(value, "id version sha256", weakened)
    token(value["id"])
    require(type(value["version"]) is str and re.fullmatch(
        r"(?:0|[1-9][0-9]{0,4})\.(?:0|[1-9][0-9]{0,4})\.(?:0|[1-9][0-9]{0,4})", value["version"]) is not None,
        "invalid-exact-version")
    sha(value["sha256"])


def array(value, maximum=64):
    require(type(value) is list and len(value) <= maximum, "array-limit")


def document(data, weakened=None):
    require(len(data) <= 65536 and not data.startswith(b"\xef\xbb\xbf"), "document-limit-or-bom")

    def pairs(items):
        result = {}
        for name, value in items:
            require(weakened == "unique-json-fields" or name not in result, "duplicate-field")
            result[name] = value
        return result

    try:
        value = json.loads(data.decode("utf-8"), object_pairs_hook=pairs,
                           parse_constant=lambda _: require(False, "non-json-number"))
    except (UnicodeError, json.JSONDecodeError, RecursionError) as error:
        raise ValueError("invalid-json") from error

    def bound(item, depth):
        require(depth <= 8, "document-depth")
        if type(item) is dict:
            for name, child in item.items():
                text(name)
                bound(child, depth + 1)
        elif type(item) is list:
            for child in item:
                bound(child, depth + 1)
        elif type(item) is str:
            text(item, 1024)
        else:
            require(item is None or type(item) is int, "unsupported-scalar")

    bound(value, 0)
    return value


def pinned_inputs(inputs, artifacts):
    checked = {}
    for item in inputs:
        fields(item, "role path sha256")
        text(item["role"], 64)
        require(item["role"] not in checked, "duplicate-role")
        require(safe_path(item["path"]), "invalid-relative-path")
        sha(item["sha256"])
        require(item["path"] in artifacts, "missing-file")
        data = artifacts[item["path"]]
        require(digest(data) == item["sha256"], "input-digest-mismatch")
        checked[item["role"]] = (item, data)
    return checked


def evidence_shape(value, weakened, kind):
    fields(value, "role path sha256", weakened)
    require(type(value["role"]) is str and re.fullmatch(kind + r"-[0-9]{3}", value["role"]) is not None,
            "evidence-role")
    path_field(value["path"], weakened)
    sha(value["sha256"])


def shapes(docs, checked, weakened):
    for role, (_, data) in checked.items():
        require(role in BASE_ROLES or re.fullmatch(r"(?:dependency|asset|reference|entry)-[0-9]{3}", role) is not None,
                "unknown-role")
        if role == "package" or role.startswith("dependency-") and role != "dependency-inventory":
            require(0 < len(data) <= 8 * 1024 * 1024, "archive-limit")
        elif role.startswith(("reference-", "entry-", "asset-")) and role != "asset-inventory":
            limit = 1024 * 1024 if role.startswith("asset-") else 16384
            require(0 < len(data) and (len(data) <= limit or weakened == "retained-byte-limit"), "retained-byte-limit")
            if not role.startswith("asset-"):
                require(not data.startswith(b"\xef\xbb\xbf") and data.decode("utf-8").strip(), "retained-text")
    require(sum(len(data) for _, data in checked.values()) <= 64 * 1024 * 1024, "total-byte-limit")
    for name, doc in docs.items():
        if doc is None:
            continue
        require(type(doc) is dict and type(doc.get("schema_version")) is int and doc["schema_version"] == 1, "schema-version")
        if name != "obligation-reviews" and name != "notice-mapping":
            identity(doc["root"], weakened)
    scope = docs["assessment"]
    fields(scope, "schema_version root target_frameworks asset_scope", weakened)
    array(scope["target_frameworks"], 4)
    require(scope["target_frameworks"] and len(set(scope["target_frameworks"])) == len(scope["target_frameworks"])
            and scope["asset_scope"] == "all-root-package-entries", "assessment-scope")
    for tfm in scope["target_frameworks"]:
        token(tfm)
    packet = docs["notice-coverage"]
    if packet is not None:
        fields(packet, "schema_version protocol root assessment_sha256 dependencies assets reviews mapping admission_sha256", weakened)
        require(packet["protocol"] == "notice-coverage-v1", "packet-protocol")
        sha(packet["assessment_sha256"])
        sha(packet["admission_sha256"])
        for field in DOC_ROLES:
            if packet[field] is not None:
                sha(packet[field])
    admission = docs["admission"]
    if admission is not None:
        fields(admission, "schema_version profile root assessment_sha256 dependencies assets reviews mapping", weakened)
        require(weakened == "admission-profiles" or admission["profile"] == "notice-coverage-admission/1", "admission-profile")
        sha(admission["assessment_sha256"])
        for name in DOC_ROLES:
            item = admission[name]
            fields(item, "profile sha256 reference authority_reference state aggregates", weakened)
            require(item["profile"] == {
                "dependencies": "resolved-target-capture/1", "assets": "all-root-entry-capture/1",
                "reviews": "whole-subject-obligations/1", "mapping": "exhaustive-notice-mapping/1",
            }[name], "capture-profile")
            if item["sha256"] is not None:
                sha(item["sha256"])
            text(item["reference"])
            text(item["authority_reference"])
            require(item["state"] in ("captured", "unavailable"), "capture-state")
            array(item["aggregates"])
            require(name == "mapping" or not item["aggregates"], "aggregate-scope")
            for aggregate in item["aggregates"]:
                fields(aggregate, "path sha256", weakened)
                path_field(aggregate["path"], weakened)
                sha(aggregate["sha256"])
    deps = docs["dependency-inventory"]
    if deps is not None:
        fields(deps, "schema_version profile root targets packages unresolved", weakened)
        require(deps["profile"] == "resolved-target-graph/1", "dependency-profile")
        array(deps["targets"], 4)
        array(deps["packages"])
        array(deps["unresolved"])
        for diagnostic in deps["unresolved"]:
            text(diagnostic)
        for target in deps["targets"]:
            fields(target, "tfm direct nodes node_count edge_count", weakened)
            token(target["tfm"])
            array(target["direct"])
            array(target["nodes"])
            require(type(target["node_count"]) is int and 0 <= target["node_count"] <= 64 and
                    type(target["edge_count"]) is int and 0 <= target["edge_count"] <= 128, "graph-count-limit")
            for node in target["nodes"]:
                fields(node, "key dependencies", weakened)
                text(node["key"], 128)
                array(node["dependencies"])
                for edge in node["dependencies"]:
                    text(edge, 128)
            for edge in target["direct"]:
                text(edge, 128)
        for package in deps["packages"]:
            fields(package, "key identity role", weakened)
            text(package["key"], 128)
            identity(package["identity"], weakened)
            require(type(package["role"]) is str and re.fullmatch(r"dependency-[0-9]{3}", package["role"]) is not None, "dependency-role")
    assets = docs["asset-inventory"]
    if assets is not None:
        fields(assets, "schema_version root entries assets", weakened)
        array(assets["entries"], 128)
        array(assets["assets"])
        for entry in assets["entries"]:
            fields(entry, "path sha256 kind subjects", weakened)
            path_field(entry["path"], weakened)
            sha(entry["sha256"])
            require(entry["kind"] in ("third-party", "first-party", "packaging", "unresolved"), "entry-kind")
            array(entry["subjects"])
            for subject in entry["subjects"]:
                text(subject, 128)
        for asset in assets["assets"]:
            fields(asset, "key role package_entry sha256", weakened)
            text(asset["key"], 128)
            path_field(asset["package_entry"], weakened)
            sha(asset["sha256"])
            require(type(asset["role"]) is str and re.fullmatch(r"asset-[0-9]{3}", asset["role"]) is not None, "asset-role")
    reviews = docs["obligation-reviews"]
    if reviews is not None:
        fields(reviews, "schema_version reviews", weakened)
        array(reviews["reviews"], 128)
        for review in reviews["reviews"]:
            fields(review, "subject sha256 disposition basis obligations owner_fact", weakened)
            text(review["subject"], 128)
            sha(review["sha256"])
            require(review["disposition"] in ("obligations", "no-obligations", "unresolved", "unsupported", "owner-private"), "review-disposition")
            evidence_shape(review["basis"], weakened, "reference")
            if review["owner_fact"] is not None:
                text(review["owner_fact"])
            array(review["obligations"], 8)
            for obligation in review["obligations"]:
                fields(obligation, "id parts", weakened)
                token(obligation["id"])
                array(obligation["parts"], 8)
                require(obligation["parts"], "empty-obligation")
                for part in obligation["parts"]:
                    fields(part, "id kind evidence", weakened)
                    token(part["id"])
                    require(part["kind"] in ("license-text", "notice", "attribution"), "obligation-kind")
                    evidence_shape(part["evidence"], weakened, "reference")
    mapping = docs["notice-mapping"]
    if mapping is not None:
        fields(mapping, "schema_version mappings", weakened)
        array(mapping["mappings"], 128)
        for item in mapping["mappings"]:
            fields(item, "subject obligation part mode package_entry entry offset length", weakened)
            for field in ("subject", "obligation", "part"):
                text(item[field], 128)
            require(item["mode"] in ("entry", "aggregate", "absent", "partial"), "mapping-mode")
            path_field(item["package_entry"], weakened)
            if item["entry"] is not None:
                evidence_shape(item["entry"], weakened, "entry")
            require(type(item["offset"]) is int and type(item["length"]) is int and
                    0 <= item["offset"] <= 16384 and (0 <= item["length"] <= 16384 or
                    weakened == "retained-byte-limit" and item["length"] >= 0), "mapping-range")


def classify(checked, weakened=None):
    """Only construction-time archive observations are used; arbitrary archives are not classified."""
    require(weakened is None or weakened in {case[3] for case in CASES if case[3]}, "unknown-mutation")
    if "package" not in checked:
        return "missing-package"
    if "assessment" not in checked:
        return "scope-missing"
    try:
        docs = {role: document(checked[role][1], weakened) if role in checked else None
                for role in BASE_ROLES - {"package"}}
        shapes(docs, checked, weakened)
    except (ValueError, KeyError, TypeError, UnicodeError):
        return "input-invalid"
    scope = docs["assessment"]
    root = FACTS.get(digest(checked["package"][1]))
    if root is None:
        return "input-invalid"
    if ((weakened != "unique-package-paths" and root["duplicates"]) or
            (weakened != "no-path-aliases" and root["aliases"]) or not root["safe"]):
        return "input-invalid"
    if (weakened != "root-digest" and scope["root"]["sha256"] != checked["package"][0]["sha256"] or
            weakened != "root-identity" and any(scope["root"][key] != root["identity"][key] for key in ("id", "version"))):
        return "binding-conflict"
    packet, admission = docs["notice-coverage"], docs["admission"]
    if packet is None or admission is None or any(docs[DOC_ROLES[key]] is None for key in ("dependencies", "assets")):
        return "evidence-missing"
    for doc in (packet, admission, docs["dependency-inventory"], docs["asset-inventory"]):
        if doc["root"] != scope["root"]:
            return "binding-conflict"
    if weakened != "scope-digest" and packet["assessment_sha256"] != checked["assessment"][0]["sha256"]:
        return "binding-conflict"
    if admission["assessment_sha256"] != checked["assessment"][0]["sha256"] or packet["admission_sha256"] != checked["admission"][0]["sha256"]:
        return "binding-conflict"
    for name, role in DOC_ROLES.items():
        actual = checked[role][0]["sha256"] if role in checked else None
        if ((weakened != "document-digests" and packet[name] != actual) or admission[name]["sha256"] != actual):
            return "binding-conflict"
    deps, assets = docs["dependency-inventory"], docs["asset-inventory"]
    if weakened != "capture-success" and any(admission[name]["state"] != "captured" for name in ("dependencies", "assets")):
        return "inventories-unresolved"
    if (sorted(target["tfm"] for target in deps["targets"]) != sorted(scope["target_frameworks"]) or
            weakened != "graph-diagnostics" and deps["unresolved"]):
        return "inventories-unresolved"
    union = set()
    for target in deps["targets"]:
        nodes = {item["key"]: item["dependencies"] for item in target["nodes"]}
        edges = target["direct"] + [edge for outgoing in nodes.values() for edge in outgoing]
        if (len(nodes) != len(target["nodes"]) or len(set(target["direct"])) != len(target["direct"]) or
                any(len(set(outgoing)) != len(outgoing) for outgoing in nodes.values()) or
                len(edges) > 128 or any(edge not in nodes for edge in edges) or
                len({key.split("/")[0] for key in nodes}) != len(nodes)):
            return "inventories-unresolved"
        reached, pending = set(), list(target["direct"])
        while pending:
            key = pending.pop()
            if key not in reached:
                reached.add(key)
                pending.extend(nodes[key])
        if reached != set(nodes) or weakened != "graph-counts" and (
                target["node_count"] != len(nodes) or target["edge_count"] != len(edges)):
            return "inventories-unresolved"
        union.update(nodes)
    packages = {item["key"]: item for item in deps["packages"]}
    if len(union) > 64 or set(packages) != union or len(packages) != len(deps["packages"]):
        return "inventories-unresolved"
    observed_entries = root["entries"]
    entry_map = {item["path"]: item for item in assets["entries"]}
    if len(entry_map) != len(assets["entries"]):
        return "inventories-unresolved"
    if weakened != "all-root-entries" and set(entry_map) != set(observed_entries):
        return "inventories-unresolved"
    if any(path not in observed_entries or item["sha256"] != digest(observed_entries[path]) for path, item in entry_map.items()):
        return "binding-conflict"
    if weakened != "asset-classification" and any(item["kind"] == "unresolved" for item in entry_map.values()):
        return "inventories-unresolved"
    asset_map = {item["key"]: item for item in assets["assets"]}
    if len(asset_map) != len(assets["assets"]):
        return "inventories-unresolved"
    classified = {}
    for item in entry_map.values():
        if len(set(item["subjects"])) != len(item["subjects"]) or (
                item["kind"] == "third-party") != bool(item["subjects"]):
            return "inventories-unresolved"
        for subject in item["subjects"]:
            if subject in classified:
                return "inventories-unresolved"
            classified[subject] = item["path"]
    if set(classified) != set(asset_map):
        return "inventories-unresolved"
    subjects, used, missing = {}, set(BASE_ROLES) & set(checked), False
    for index, key in enumerate(sorted(packages)):
        item = packages[key]
        ident, role = item["identity"], item["role"]
        if key != f"{ident['id']}/{ident['version']}" or role != f"dependency-{index:03d}":
            return "binding-conflict"
        subjects["dependency:" + key] = ident["sha256"]
        used.add(role)
        if role not in checked:
            missing = True
            if weakened == "early-missing":
                return "subject-bytes-missing"
            continue
        fact = FACTS.get(digest(checked[role][1]))
        if fact is None or fact["duplicates"] or fact["aliases"] or not fact["safe"]:
            return "input-invalid"
        if (checked[role][0]["sha256"] != ident["sha256"] or weakened != "dependency-identity-binding" and
                any(ident[field] != fact["identity"][field] for field in ("id", "version"))):
            return "binding-conflict"
    for index, key in enumerate(sorted(asset_map)):
        item = asset_map[key]
        if (not key.startswith("asset:") or item["role"] != f"asset-{index:03d}" or
                classified[key] != item["package_entry"]):
            return "binding-conflict"
        if weakened != "asset-source-binding" and item["sha256"] != digest(observed_entries[item["package_entry"]]):
            return "binding-conflict"
        subjects[key] = item["sha256"]
        used.add(item["role"])
        if item["role"] not in checked:
            missing = True
        elif checked[item["role"]][0]["sha256"] != item["sha256"]:
            return "binding-conflict"
    reviews_doc = docs["obligation-reviews"]
    reviews = {item["subject"]: item for item in reviews_doc["reviews"]} if reviews_doc else {}
    if len(reviews) != (len(reviews_doc["reviews"]) if reviews_doc else 0) or not set(reviews) <= set(subjects):
        return "binding-conflict"
    review_unresolved = (set(reviews) != set(subjects) or admission["reviews"]["state"] != "captured")
    owner = False
    parts = {}

    def evidence_bytes(evidence):
        used.add(evidence["role"])
        if evidence["role"] not in checked:
            return None
        pin, data = checked[evidence["role"]]
        require(pin == evidence, "binding-conflict")
        return data

    try:
        for subject, review in sorted(reviews.items()):
            if weakened != "review-subject-binding" and review["sha256"] != subjects[subject]:
                return "binding-conflict"
            basis = evidence_bytes(review["basis"])
            disposition = review["disposition"]
            if weakened == "all-tfm-versions" and subject == "dependency:sample.a/2.0.0":
                disposition = "obligations"
            if basis is None or disposition in ("unresolved", "unsupported") and weakened != "review-disposition":
                review_unresolved = True
            if disposition == "owner-private":
                # The retained basis and admission prevalidation establish necessity, not owner access.
                if basis is None or not review["owner_fact"] or review["obligations"]:
                    review_unresolved = True
                else:
                    owner = True
            elif review["owner_fact"] is not None:
                return "binding-conflict"
            if (disposition == "no-obligations" and review["obligations"] or
                    disposition == "obligations" and not review["obligations"]):
                return "binding-conflict"
            obligation_ids = set()
            for obligation in review["obligations"]:
                if obligation["id"] in obligation_ids:
                    return "binding-conflict"
                obligation_ids.add(obligation["id"])
                for part in obligation["parts"]:
                    key = (subject, obligation["id"], part["id"])
                    if key in parts:
                        return "binding-conflict"
                    parts[key] = evidence_bytes(part["evidence"])
                    if parts[key] is None:
                        review_unresolved = True
        mapping_doc = docs["notice-mapping"]
        mappings = {(item["subject"], item["obligation"], item["part"]): item
                    for item in mapping_doc["mappings"]} if mapping_doc else {}
        unknown_parts = {key for key in mappings if key not in parts and key[0] in reviews}
        if (len(mappings) != (len(mapping_doc["mappings"]) if mapping_doc else 0) or unknown_parts or
                any(key[0] not in subjects for key in mappings)):
            return "binding-conflict"
        mapping_unresolved = (mapping_doc is None or admission["mapping"]["state"] != "captured" or
                              weakened != "mapping-equality" and set(mappings) != set(parts))
        accepted = {item["path"]: item["sha256"] for item in admission["mapping"]["aggregates"]}
        if len(accepted) != len(admission["mapping"]["aggregates"]):
            return "binding-conflict"
        for path, expected_sha in accepted.items():
            if path not in observed_entries or digest(observed_entries[path]) != expected_sha:
                return "binding-conflict"
        gap = False
        for key, item in mappings.items():
            packaged = observed_entries.get(item["package_entry"])
            mode = item["mode"]
            if mode == "absent":
                if item["entry"] is not None or item["offset"] != 0 or item["length"] != 0 or packaged is not None:
                    return "binding-conflict"
                if weakened == "absence-is-unresolved":
                    mapping_unresolved = True
                else:
                    gap = True
                continue
            if item["entry"] is None:
                mapping_unresolved = True
                continue
            retained = evidence_bytes(item["entry"])
            if packaged is None or weakened != "entry-source-binding" and (
                    digest(packaged) != item["entry"]["sha256"]):
                return "binding-conflict"
            if retained is None:
                mapping_unresolved = True
                continue
            if mode != "aggregate" and (item["offset"] != 0 or item["length"] != len(retained)):
                return "binding-conflict"
            expected = parts.get(key)
            if expected is None:
                continue
            if mode == "aggregate":
                if weakened != "aggregate-admission" and accepted.get(item["package_entry"]) != item["entry"]["sha256"]:
                    mapping_unresolved = True
                if retained[item["offset"]:item["offset"] + item["length"]] != expected or item["length"] != len(expected):
                    mapping_unresolved = True
            elif mode == "partial":
                if 0 < len(retained) < len(expected) and expected.startswith(retained):
                    if weakened == "partial-is-unresolved":
                        mapping_unresolved = True
                    else:
                        gap = True
                else:
                    mapping_unresolved = True
            elif retained != expected and weakened != "modified-text":
                mapping_unresolved = True
    except ValueError as error:
        require(str(error) == "binding-conflict", "unexpected-evidence-error")
        return "binding-conflict"
    if set(checked) - used:
        return "binding-conflict"
    if missing:
        return "subject-bytes-missing"
    if review_unresolved:
        return "reviews-unresolved"
    if mapping_unresolved and not (gap and weakened == "gap-before-unresolved"):
        return "mapping-unresolved"
    if owner and not (gap and weakened == "gap-before-owner"):
        return "owner-evidence-required"
    if gap:
        return "required-representation-missing"
    return "notice-coverage-satisfied" if parts else "no-applicable-surface"


def result(branch, inputs):
    status, missing, required, category = BRANCHES[branch]
    deterministic = status in ("verified", "gap", "not applicable")
    finding = {
        "verified": "Every applicable reviewed preservation part is represented by exact checked root entry bytes or explicitly admitted aggregate segments within the assessed scope.",
        "gap": "Complete admitted inventories, supported obligation reviews and exhaustive mappings establish at least one required representation absent or a strict nonempty prefix in the exact root package.",
        "not applicable": "Complete admitted inventories and whole-subject reviews establish no applicable third-party license/notice preservation obligation within the assessed scope.",
    }.get(status)
    return {
        "row_id": "LP-04", "resolver_id": "lp04-notice-coverage", "resolver_version": "1.0.0",
        "branch_id": branch, "decision": "deterministic" if deterministic else "escalated",
        "applicability": "not-applicable" if status == "not applicable" else
                         "applicable" if deterministic or status == "owner evidence required" else "unknown",
        "inputs": inputs, "evidence_references": [f"input:{item['role']}:{item['sha256']}" for item in inputs],
        "evidence_contract": CONTRACT, "finding": finding, "missing_fact": missing,
        "required_input": required, "next_category": category,
    }


def package(package_id, version, entries):
    """Record known byte-construction facts; do not parse ZIP/XML to rediscover those facts."""
    nuspec = (f'<?xml version="1.0" encoding="utf-8"?><package><metadata><id>{package_id}</id>'
              f'<version>{version}</version><authors>Fictional</authors><description>Synthetic only</description>'
              '<license type="expression">MIT</license></metadata></package>').encode("ascii")
    all_entries = [(package_id + ".nuspec", nuspec), *entries]
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w", compression=zipfile.ZIP_STORED) as archive:
        # Duplicate-entry fixtures deliberately exercise observations, not Python reader selection.
        for index, (path, data) in enumerate(all_entries):
            physical_name = path if path not in [name for name, _ in all_entries[:index]] else f"temporary-{index}"
            info = zipfile.ZipInfo(physical_name, (2026, 1, 1, 0, 0, 0))
            info.external_attr = 0o100644 << 16
            archive.writestr(info, data)
    raw = buffer.getvalue()
    # Equal-length name replacement creates one bounded duplicate without custom ZIP parsing.
    for index, (path, _) in enumerate(all_entries):
        if path in [name for name, _ in all_entries[:index]]:
            temporary = f"temporary-{index}"
            require(len(temporary) == len(path), "duplicate-fixture-name-length")
            raw = raw.replace(temporary.encode("ascii"), path.encode("ascii"))
    names = [name for name, _ in all_entries]
    fact = {
        "identity": {"id": package_id, "version": version, "sha256": digest(raw)},
        "entries": dict(all_entries), "duplicates": len(names) != len(set(names)),
        "aliases": len({name.lower() for name in names}) != len(set(names)),
        "safe": all(safe_path(name) for name in names),
    }
    FACTS[digest(raw)] = fact
    return raw, fact


def build():
    artifacts, cases = {}, []

    def retain(path, data, role):
        require(path not in artifacts or artifacts[path] == data, "artifact-collision")
        artifacts[path] = data
        return {"role": role, "path": path, "sha256": digest(data)}

    for name, branch, _, _, _, rationale in CASES:
        local = {}

        def add(role, data, folder="references", extension="txt"):
            pin = retain(f"{folder}/{digest(data)}.{extension}", data, role)
            local[role] = pin
            return pin

        def replace(role, data):
            old = local[role]
            local[role] = retain(f"references/{digest(data)}.txt", data, role)
            return old

        size = 16385 if name == "resource-limit" else 16384
        aggregate = name in ("aggregate-shared-entry", "unaccepted-aggregate")
        license_bytes = (b"Fictional complete short license; preserve these bytes.\n" if aggregate else
                         b"Fictional exact license text. Preserve this complete text.\n".ljust(size, b" "))
        a_notice, asset_notice = b"Fictional A attribution must be retained.\n", b"Fictional bundled widget attribution must be retained.\n"
        references = {}
        for data in (license_bytes, a_notice, asset_notice):
            references[data] = add(f"reference-{len(references):03d}", data)
        keys = ["sample.a/1.0.0", "sample.b/1.0.0"]
        if name == "multi-tfm-second-version-unresolved":
            keys.append("sample.a/2.0.0")
        empty = name == "no-applicable-surface"
        if empty:
            keys = []
        packages = []
        for index, key in enumerate(sorted(keys)):
            package_id, version = key.split("/")
            archive, fact = package(package_id, version, [("LICENSE.txt", license_bytes), ("NOTICE.txt", a_notice)])
            role = f"dependency-{index:03d}"
            add(role, archive, "packages", "nupkg")
            packages.append({"key": key, "identity": copy.deepcopy(fact["identity"]), "role": role})
        asset_data = b"/* fictional generated/minified asset: upstream review retained separately */function w(){return 1;}\n"
        asset_key = "asset:widget"
        if not empty:
            add("asset-000", asset_data, "assets", "bin")
        sources = {"notices/license.txt": license_bytes, "notices/a.txt": a_notice, "notices/widget.txt": asset_notice}
        aggregate_data = b"Aggregate header\n" + license_bytes + a_notice + asset_notice
        absent = name in ("direct-absent", "owner-before-gap", "unresolved-before-gap")
        if absent:
            del sources["notices/a.txt"]
        if name == "direct-partial":
            sources["notices/a.txt"] = a_notice[:12]
        if name == "modified-license":
            sources["notices/license.txt"] = b"Changed arbitrary grant text, not an established incomplete representation.\n"
        if name == "wrong-entry-binding":
            sources["notices/a.txt"] = b"Packaged entry differs from the internally consistent claimed entry bytes.\n"
        root_entries = [("LICENSE.txt", b"First-party LICENSE is not third-party coverage evidence.\n")]
        if not empty:
            root_entries += [("wwwroot/widget.min.js", asset_data)]
            root_entries += [("NOTICE.txt", aggregate_data)] if aggregate else list(sources.items())
        if name == "duplicate-package-path":
            # The temporary name and LICENSE.txt are both eleven bytes.
            root_entries.append(("LICENSE.txt", root_entries[0][1]))
        if name == "alias-package-path":
            root_entries.append(("license.txt", root_entries[0][1]))
        root_bytes, root_fact = package("sample.root", "1.0.0", root_entries)
        add("package", root_bytes, "packages", "nupkg")
        root = copy.deepcopy(root_fact["identity"])
        if name == "wrong-root-identity":
            root["version"] = "9.0.0"
        if name == "wrong-root-digest":
            root["sha256"] = "0" * 64
        scope = {"schema_version": 1, "root": root, "target_frameworks": ["net8.0", "net9.0"],
                 "asset_scope": "all-root-package-entries"}
        targets = []
        for tfm in scope["target_frameworks"]:
            selected_a = "sample.a/2.0.0" if tfm == "net9.0" and "sample.a/2.0.0" in keys else "sample.a/1.0.0"
            nodes = [] if empty else [{"key": selected_a, "dependencies": ["sample.b/1.0.0"]},
                                      {"key": "sample.b/1.0.0", "dependencies": []}]
            targets.append({"tfm": tfm, "direct": [] if empty else [selected_a], "nodes": nodes,
                            "node_count": len(nodes), "edge_count": 0 if empty else 2})
        if name == "dependency-identity":
            packages[0]["key"] = "sample.a/9.0.0"
            packages[0]["identity"]["version"] = "9.0.0"
            for target in targets:
                target["direct"] = ["sample.a/9.0.0"]
                target["nodes"][0]["key"] = "sample.a/9.0.0"
        deps = {"schema_version": 1, "profile": "resolved-target-graph/1", "root": root,
                "targets": targets, "packages": packages, "unresolved": []}
        if name == "incomplete-dependency-inventory":
            targets[0]["node_count"] += 1
        if name == "unresolved-graph":
            deps["unresolved"] = ["Capture could not resolve one target condition."]
        assets = {"schema_version": 1, "root": root, "entries": [], "assets": []}
        for path, data in sorted(root_fact["entries"].items()):
            third_party = path == "wwwroot/widget.min.js"
            assets["entries"].append({"path": path, "sha256": digest(data),
                                     "kind": "third-party" if third_party else "packaging",
                                     "subjects": [asset_key] if third_party else []})
        if not empty:
            assets["assets"] = [{"key": asset_key, "role": "asset-000",
                                  "package_entry": "wwwroot/widget.min.js", "sha256": digest(asset_data)}]
        if name == "incomplete-asset-inventory":
            assets["entries"] = [item for item in assets["entries"] if item["path"] != "LICENSE.txt"]
        if name == "unreviewed-asset-entry":
            next(item for item in assets["entries"] if item["path"] == "LICENSE.txt")["kind"] = "unresolved"
        if name == "wrong-asset-binding":
            changed = b"Different internally consistent retained asset bytes.\n"
            replace("asset-000", changed)
            assets["assets"][0]["sha256"] = digest(changed)
        reviews, mappings = [], []
        subjects = [("dependency:" + item["key"], item["identity"]["sha256"]) for item in packages]
        if not empty:
            subjects.append((asset_key, assets["assets"][0]["sha256"]))
        entry_pins = {}

        def mapped(subject, part_id, source, data):
            if aggregate:
                path = "NOTICE.txt"
                offset, length, mode = aggregate_data.index(data), len(data), "aggregate"
            else:
                path = source
                offset, length, mode = 0, len(root_fact["entries"].get(path, b"")), "entry"
            if path not in root_fact["entries"]:
                mode = "absent"
            if name == "direct-partial" and path == "notices/a.txt":
                mode = "partial"
            if mode != "absent" and path not in entry_pins:
                entry_pins[path] = add(f"entry-{len(entry_pins):03d}", root_fact["entries"][path])
            return {"subject": subject, "obligation": "preservation", "part": part_id, "mode": mode,
                    "package_entry": path, "entry": entry_pins.get(path), "offset": offset, "length": length}

        for subject, subject_sha in subjects:
            selected = [("license", "license-text", "notices/license.txt", license_bytes)]
            if subject.startswith("dependency:sample.a/"):
                selected.append(("notice", "notice", "notices/a.txt", a_notice))
            if subject == asset_key:
                selected.append(("attribution", "attribution", "notices/widget.txt", asset_notice))
            disposition, owner_fact = "obligations", None
            if name == "reviewed-no-obligations":
                disposition, selected = "no-obligations", []
            if name == "owner-before-gap" and subject == asset_key:
                disposition, selected, owner_fact = "owner-private", [], "Only the owner retains the exact private bundled-asset preservation contract."
            basis_bytes = (f"Fictional retained upstream terms and whole-subject review for {subject}: "
                           f"{disposition}. Exact checked license/notice parts must be preserved; "
                           "no-obligations means the reviewed complete terms impose none. "
                           "owner-private means a documented private contract is necessary, not failed public inspection.\n").encode("ascii")
            basis = add(f"reference-{3 + len(reviews):03d}", basis_bytes)
            parts = [{"id": part_id, "kind": kind, "evidence": references[data]} for part_id, kind, _, data in selected]
            reviews.append({"subject": subject, "sha256": subject_sha, "disposition": disposition, "basis": basis,
                            "obligations": [{"id": "preservation", "parts": parts}] if parts else [], "owner_fact": owner_fact})
            mappings.extend(mapped(subject, part_id, source, data) for part_id, _, source, data in selected)
        if name == "unresolved-review":
            reviews[0]["disposition"] = "unresolved"
            reviews[-1]["disposition"] = "unsupported"
        if name == "multi-tfm-second-version-unresolved":
            next(item for item in reviews if item["subject"] == "dependency:sample.a/2.0.0")["disposition"] = "unresolved"
            # This version otherwise has complete parts/mappings, isolating the all-TFM review decision.
        if name == "wrong-review-binding":
            reviews[-1]["sha256"] = "0" * 64
        if name == "later-binding-before-unresolved":
            del local["dependency-000"]
            reviews[1]["sha256"] = "0" * 64
        if name == "wrong-entry-binding":
            path = "notices/a.txt"
            pin = replace(entry_pins[path]["role"], a_notice)
            for item in mappings:
                if item["package_entry"] == path:
                    item["entry"] = local[pin["role"]]
                    item["length"] = len(artifacts[item["entry"]["path"]])
        if name in ("incomplete-mapping", "unresolved-before-gap"):
            mappings.pop()
        if name == "unsafe-path":
            mappings[0]["package_entry"] = "../notice.txt"
        reviews_doc = {"schema_version": 1, "reviews": reviews}
        mapping_doc = {"schema_version": 1, "mappings": mappings}
        docs = {"assessment": scope, "dependency-inventory": deps, "asset-inventory": assets,
                "obligation-reviews": reviews_doc, "notice-mapping": mapping_doc}
        removed = {
            "missing-dependency-inventory": "dependency-inventory", "missing-asset-inventory": "asset-inventory",
            "missing-reviews": "obligation-reviews", "missing-mapping": "notice-mapping",
        }.get(name)
        for role, doc in docs.items():
            if role != removed:
                local[role] = retain(f"inputs/{name}.{role}.json", encoded(doc), role)
        admission = {"schema_version": 1, "profile": "notice-coverage-admission/1", "root": root,
                     "assessment_sha256": local["assessment"]["sha256"]}
        for label, role in DOC_ROLES.items():
            admission[label] = {
                "profile": {"dependencies": "resolved-target-capture/1", "assets": "all-root-entry-capture/1",
                            "reviews": "whole-subject-obligations/1", "mapping": "exhaustive-notice-mapping/1"}[label],
                "sha256": local[role]["sha256"] if role in local else None,
                "reference": "synthetic-only full capture or exhaustive reviewed evidence reference",
                "authority_reference": "synthetic-only caller prevalidation of producer or reviewer authority and completeness",
                "state": "captured" if role in local else "unavailable",
                "aggregates": [{"path": "NOTICE.txt", "sha256": digest(aggregate_data)}]
                              if label == "mapping" and name == "aggregate-shared-entry" else [],
            }
        if name == "unaccepted-admission":
            admission["profile"] = "caller-asserted/1"
        if name == "acquisition-unavailable":
            admission["dependencies"]["state"] = "unavailable"
        local["admission"] = retain(f"inputs/{name}.admission.json", encoded(admission), "admission")
        packet = {"schema_version": 1, "protocol": "notice-coverage-v1", "root": root,
                  "assessment_sha256": local["assessment"]["sha256"],
                  **{label: local[role]["sha256"] if role in local else None for label, role in DOC_ROLES.items()},
                  "admission_sha256": local["admission"]["sha256"]}
        if name == "wrong-scope-binding":
            packet["assessment_sha256"] = "0" * 64
        if name == "wrong-inventory-binding":
            packet["dependencies"] = "0" * 64
        if name == "unknown-field":
            packet["complete"] = "caller-asserted"
        packet_bytes = encoded(packet)
        if name == "duplicate-json-field":
            packet_bytes = packet_bytes.replace(b'"schema_version": 1,', b'"schema_version": 1, "schema_version": 1,', 1)
        local["notice-coverage"] = retain(f"inputs/{name}.notice-coverage.json", packet_bytes, "notice-coverage")
        for case_name, role in (("missing-package", "package"), ("missing-scope", "assessment"),
                                ("missing-packet", "notice-coverage"), ("missing-dependency-bytes", "dependency-000"),
                                ("missing-asset-bytes", "asset-000")):
            if name == case_name:
                del local[role]
        # Retain exactly referenced evidence roles; deliberate missing roles remain omitted.
        referenced = {part["evidence"]["role"] for review in reviews for obligation in review["obligations"] for part in obligation["parts"]}
        referenced |= {review["basis"]["role"] for review in reviews}
        referenced |= {item["entry"]["role"] for item in mappings if item["entry"] is not None}
        if removed == "obligation-reviews":
            # Mappings cannot stand in for absent reviews; do not leave unrelated review reference roles.
            referenced = {item["entry"]["role"] for item in mappings if item["entry"] is not None}
        if removed == "notice-mapping":
            referenced = {part["evidence"]["role"] for review in reviews for obligation in review["obligations"] for part in obligation["parts"]}
            referenced |= {review["basis"]["role"] for review in reviews}
        local = {role: pin for role, pin in local.items() if not role.startswith(("reference-", "entry-")) or role in referenced}
        inputs = [pin for role, pin in sorted(local.items())]
        expected = result(branch, inputs)
        cases.append({"case_id": "lp04-" + name, "row_id": "LP-04", "inputs": inputs, "expected": expected,
                      "rationale": rationale, "evidence_pointers": expected["evidence_references"]})
    artifacts["cases.json"] = encoded({"schema_version": 1, "cases": cases})
    baseline = next(case for case in cases if case["case_id"] == "lp04-all-sources-covered")["inputs"]
    controls = []
    for suffix, rejection, guard in (
        ("duplicate-role", "duplicate-role", "loader-duplicate-role"),
        ("parent-path", "invalid-relative-path", "loader-safe-path"),
        ("missing-file", "missing-file", "loader-file-presence"),
        ("changed-bytes", "input-digest-mismatch", "loader-digest"),
        ("unknown-input-field", "invalid-fields", "loader-exact-fields"),
    ):
        inputs = copy.deepcopy(baseline)
        if suffix == "duplicate-role":
            inputs.append(copy.deepcopy(inputs[0]))
        elif suffix == "parent-path":
            inputs[0]["path"] = "../outside.json"
        elif suffix == "missing-file":
            inputs[0]["path"] = "inputs/not-retained.json"
        elif suffix == "changed-bytes":
            inputs[0]["sha256"] = "0" * 64
        else:
            inputs[0]["complete"] = True
        controls.append({"case_id": "lp04-reject-" + suffix, "stage": "shared-input-loader",
                         "guard_id": guard, "expected_rejection": rejection, "inputs": inputs})
    artifacts["rejected-inputs.json"] = encoded({"schema_version": 1, "controls": controls})
    retained, vendors = [], []
    for vendor in json.loads((ROOT.parent / "lp03" / "retained-inputs.json").read_bytes())["vendors"]:
        vendor = {**vendor, "binding": vendor["binding"].replace("LP03_", "LP04_")}
        vendors.append(vendor)
        inputs = [{"role": "package", "path": f"packages/{vendor['package_id']}.{vendor['version']}.nupkg", "sha256": vendor["sha256"]}]
        expected = result("scope-missing", inputs)
        retained.append({"case_id": "lp04-retained-" + vendor["vendor"], "row_id": "LP-04", "inputs": inputs,
                         "expected": expected, "rationale": "Exact root bytes without checked notice-coverage evidence cannot establish notice compliance.",
                         "evidence_pointers": expected["evidence_references"]})
    artifacts["acceptance.json"] = encoded({"schema_version": 1, "cases": retained})
    artifacts["retained-inputs.json"] = encoded({"schema_version": 1, "vendors": vendors})
    witnesses = [{"guard_id": guard, "case_id": "lp04-" + name, "kind": "weakened-rule" if mutation else "existing-branch",
                  "weakened_rule": mutation, "normal_branch": branch, "wrong_branch": wrong, "rule": rationale}
                 for name, branch, guard, mutation, wrong, rationale in CASES]
    witnesses.extend({"guard_id": item["guard_id"], "case_id": item["case_id"], "kind": "loader",
                      "expected_rejection": item["expected_rejection"]} for item in controls)
    witnesses.extend({"guard_id": "retained-" + vendor["vendor"], "case_id": case["case_id"],
                      "kind": "optional-retained", "normal_branch": "scope-missing"} for vendor, case in zip(vendors, retained))
    artifacts["discrimination.json"] = encoded({"schema_version": 1, "guard_inventory": list(CRITICAL_GUARDS), "witnesses": witnesses})
    # Discard intermediate content-addressed artifacts no final expectation actually references.
    needed = {pin["path"] for case in cases for pin in case["inputs"]}
    artifacts = {path: data for path, data in artifacts.items() if not path.startswith(
        ("inputs/", "packages/", "references/", "assets/")) or path in needed}
    index = [{"path": path, "size_bytes": len(data), "sha256": digest(data)} for path, data in sorted(artifacts.items())
             if path.startswith(("inputs/", "packages/", "references/", "assets/"))]
    artifacts["input-manifest.json"] = encoded({"schema_version": 1, "inputs": index})
    artifacts["acceptance-manifest.json"] = encoded({
        "schema_version": 1, "phase": "A", "row_id": "LP-04", "scope": "repository-wide",
        "area": "Licensing and provenance", "clause": "2.12", "classification": "decomposition evidence check",
        "obligation": "Required third-party license and notice files are preserved.",
        "resolver_id": "lp04-notice-coverage", "resolver_version": "1.0.0", "protocol": "notice-coverage-v1",
        "accepted_parent": PARENT, "production_implementation": "absent; LP-04 remains tbd",
        "synthetic_cases": len(cases), "rejected_inputs": len(controls), "retained_expectations": len(retained),
        "total_named_expectations": len(cases) + len(controls) + len(retained),
        "guard_count": len(CRITICAL_GUARDS), "witness_count": len(witnesses),
        "weakened_rule_variants": sum(case[3] is not None for case in CASES),
        "guard_omission_checks": 2 * len(CRITICAL_GUARDS),
        "input_count": len(index), "input_bytes": sum(item["size_bytes"] for item in index),
        "branch_status_mapping": {branch: details[0] for branch, details in BRANCHES.items()},
        "manifest_sha256": {path: digest(artifacts[path]) for path in
                            ("cases.json", "acceptance.json", "rejected-inputs.json", "retained-inputs.json",
                             "input-manifest.json", "discrimination.json")},
        "contract_sha256": digest((ROOT / "README.md").read_bytes()),
        "oracle_sha256": digest(Path(__file__).read_bytes()),
    })
    return artifacts


def validate_guards(spec):
    require(spec["guard_inventory"] == list(CRITICAL_GUARDS), "guard-inventory-missing-or-extra")
    witnesses = spec["witnesses"]
    require(len({item["case_id"] for item in witnesses}) == len(witnesses), "duplicate-witness")
    require({item["guard_id"] for item in witnesses} == set(CRITICAL_GUARDS), "guard-without-witness")
    require(len(witnesses) == len(CRITICAL_GUARDS), "guard-witness-count")


def check(artifacts, mode):
    cases = json.loads(artifacts["cases.json"])["cases"]
    retained = json.loads(artifacts["acceptance.json"])["cases"]
    controls = json.loads(artifacts["rejected-inputs.json"])["controls"]
    require(len(CASES) == len(cases) == 43 and len(controls) == 5 and len(retained) == 2, "expectation-counts")
    ids = [case["case_id"] for case in cases + retained + controls]
    require(len(ids) == len(set(ids)) == 50, "named-expectation-limit")
    if mode in ("all", "oracle"):
        for case in cases:
            actual = classify(pinned_inputs(case["inputs"], artifacts))
            require(result(actual, case["inputs"]) == case["expected"],
                    f"{case['case_id']}: expected {case['expected']['branch_id']}, got {actual}")
        require({case["expected"]["branch_id"] for case in cases + retained} == set(BRANCHES), "branch-coverage")
    if mode in ("all", "loaders", "rejected-inputs"):
        for control in controls:
            try:
                pinned_inputs(control["inputs"], artifacts)
            except ValueError as error:
                require(str(error) == control["expected_rejection"], f"{control['case_id']}: wrong rejection: {error}")
            else:
                raise ValueError(f"{control['case_id']}: accepted rejected input")
    spec = json.loads(artifacts["discrimination.json"])
    validate_guards(spec)
    if mode in ("all", "discrimination"):
        by_id = {case["case_id"]: case for case in cases}
        for witness in spec["witnesses"]:
            if witness["kind"] not in ("weakened-rule", "existing-branch"):
                continue
            case = by_id[witness["case_id"]]
            checked = pinned_inputs(case["inputs"], artifacts)
            actual = classify(checked)
            require(actual == witness["normal_branch"], "wrong-normal-witness: " + witness["case_id"])
            if witness["kind"] == "weakened-rule":
                wrong = classify(checked, witness["weakened_rule"])
                require(wrong == witness["wrong_branch"] and wrong != actual,
                        f"non-discriminating: {witness['guard_id']}: got {wrong}, expected {witness['wrong_branch']}")
        for guard in CRITICAL_GUARDS:
            for kind, expected in (("registration", "guard-inventory-missing-or-extra"), ("witness", "guard-without-witness")):
                broken = copy.deepcopy(spec)
                if kind == "registration":
                    broken["guard_inventory"].remove(guard)
                else:
                    broken["witnesses"] = [item for item in broken["witnesses"] if item["guard_id"] != guard]
                try:
                    validate_guards(broken)
                except ValueError as error:
                    require(str(error) == expected, "wrong-guard-omission-rejection")
                else:
                    raise ValueError("accepted-guard-omission: " + guard)


def retained_bindings(vendors, supplied):
    allowed = {vendor["binding"] for vendor in vendors}
    require(not set(supplied) - allowed, "unknown-retained-binding")
    require(set(supplied) == allowed, "both-retained-bindings-required")
    result_bytes = {}
    for vendor in vendors:
        path = Path(supplied[vendor["binding"]])
        require(path.is_file(), "retained-file-missing")
        require(path.stat().st_size == vendor["size_bytes"], "retained-size-mismatch")
        data = path.read_bytes()
        require(len(data) == vendor["size_bytes"], "retained-size-mismatch")
        require(digest(data) == vendor["sha256"], "retained-digest-mismatch")
        result_bytes[vendor["binding"]] = data
    return result_bytes


def check_retained(artifacts, requested):
    vendors = json.loads(artifacts["retained-inputs.json"])["vendors"]
    supplied = {key: value for key, value in os.environ.items() if key.startswith("LP04_")}
    for values, expected in (
        ({}, "both-retained-bindings-required"),
        ({vendors[0]["binding"]: "not-opened"}, "both-retained-bindings-required"),
        ({"LP04_MISSPELLED_NUPKG": "not-opened"}, "unknown-retained-binding"),
    ):
        try:
            retained_bindings(vendors, values)
        except ValueError as error:
            require(str(error) == expected, "wrong-retained-rejection")
        else:
            raise ValueError("accepted-invalid-retained-binding")
    with tempfile.TemporaryDirectory(prefix="lp04-bindings-") as directory:
        wrong = Path(directory) / "wrong.nupkg"
        wrong.write_bytes(b"x")
        for name, expected in (("missing", "retained-file-missing"), ("size", "retained-size-mismatch"), ("digest", "retained-digest-mismatch")):
            miniature = copy.deepcopy(vendors)
            if name == "digest":
                for vendor in miniature:
                    vendor["size_bytes"] = 1
            values = {vendor["binding"]: str(wrong if name != "missing" else wrong.with_name("absent.nupkg")) for vendor in miniature}
            try:
                retained_bindings(miniature, values)
            except ValueError as error:
                require(str(error) == expected, "wrong-retained-byte-rejection")
            else:
                raise ValueError("accepted-invalid-retained-bytes")
    if not supplied and not requested:
        return "NOT RUN: both optional local bindings absent; six binding rejection classes passed; no download"
    checked = retained_bindings(vendors, supplied)
    for vendor, case in zip(vendors, json.loads(artifacts["acceptance.json"])["cases"]):
        inputs = case["inputs"]
        require(result(classify({"package": (inputs[0], checked[vendor["binding"]])}), inputs) == case["expected"],
                "retained-result-mismatch")
    return "2 exact retained roots: scope-missing / not tested; no positive or gap"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true")
    parser.add_argument("--mode", choices=("all", "oracle", "loaders", "rejected-inputs", "bindings", "discrimination"), default="all")
    parser.add_argument("--retained", action="store_true", help="Require both LP04_*_NUPKG optional exact local bindings.")
    args = parser.parse_args()
    artifacts = build()
    check(artifacts, args.mode)
    if args.write and (ROOT / "input-manifest.json").is_file():
        old_inputs = json.loads((ROOT / "input-manifest.json").read_bytes())["inputs"]
        for item in old_inputs:
            path = item["path"]
            require(safe_path(path) and path.startswith(("inputs/", "packages/", "references/", "assets/")),
                    "unsafe-old-generated-path")
            if path not in artifacts:
                require(digest((ROOT / path).read_bytes()) == item["sha256"], "modified-old-generated-input")
                (ROOT / path).unlink()
    for name, data in artifacts.items():
        path = ROOT / name
        if args.write:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        else:
            require(path.is_file() and path.read_bytes() == data, "frozen-oracle-mismatch: " + name)
    actual = {path.relative_to(ROOT).as_posix() for folder in ("inputs", "packages", "references", "assets")
              for path in (ROOT / folder).rglob("*") if path.is_file()}
    expected = {name for name in artifacts if name.startswith(("inputs/", "packages/", "references/", "assets/"))}
    require(actual == expected, "unindexed-or-missing-input")
    manifest = json.loads(artifacts["acceptance-manifest.json"])
    print(f"LP-04 Phase A [{args.mode}]: 43 replays + 5 loader rejections + 2 optional retained = 50 named expectations; 13 branches.")
    print(f"Guards: {manifest['guard_count']} / {manifest['witness_count']} witnesses; "
          f"{manifest['weakened_rule_variants']} fixed weakened variants; {manifest['guard_omission_checks']} omission checks.")
    print(f"Inputs: {manifest['input_count']} / {manifest['input_bytes']} bytes; input manifest SHA-256 {digest(artifacts['input-manifest.json'])}")
    print("Retained: " + check_retained(artifacts, args.retained))
    print("No production handler, registry change, scanner, package-manager execution or network used.")


if __name__ == "__main__":
    main()
