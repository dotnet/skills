"""LP-03 Phase A only: independent checked-receipt oracle and synthetic fixture generator."""

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

ROOT = Path(__file__).parent / "fixtures" / "lp03"
LP01 = ROOT.parent / "lp01"
POLICY_SHA = "b395256c51e7c0ae68bf3a213aab90bee0479f6115c3507e456269d0cdd41f47"
CORPUS_SHA = "157789ba91984aecba5c67b84168dbbd41a235b910a7569aa64bf42a88b24290"
PRODUCER = "lp03-resolved-target-capture/1.0.0"
PACKAGE_FACTS = {}
CONTRACT = (
    "Exact root bytes and explicit target-framework union only. Accepted acquisition observations "
    "supply resolved closure and package inspection; caller-prevalidated reviews supply complete "
    "grant applicability to each entire dependency version. Hashes prove bindings, not those "
    "observations' truth or reviewer authority. No legal approval or overall readiness conclusion."
)
BRANCHES = {
    "missing-package": ("not tested", "The exact root package is missing.", "package", "package-acquisition"),
    "scope-missing": ("not tested", "The exact root and target-framework assessment scope is missing.", "assessment", "assessment-scope"),
    "evidence-missing": ("not tested", "A checked resolved graph or acquisition receipt is missing.", "dependency-closure", "dependency-acquisition"),
    "input-invalid": ("not tested", "The checked evidence is outside the bounded unambiguous receipt profile.", "supported-evidence", "evidence-repair"),
    "policy-unavailable": ("not tested", "The pinned policy and SPDX catalog are missing or inconsistent.", "pinned-policy-and-corpus", "policy-acquisition"),
    "binding-conflict": ("not tested", "Root, graph, dependency, grant or review bindings disagree.", "consistent-checked-bindings", "evidence-repair"),
    "closure-unresolved": ("not tested", "Acquisition or the exact resolved target union and edge closure is incomplete or conflicting.", "complete-resolved-target-capture", "dependency-acquisition"),
    "dependency-bytes-missing": ("not tested", "An exact inventoried dependency archive is unavailable.", "exact-dependency-bytes", "dependency-acquisition"),
    "license-evidence-unresolved": ("not tested", "Applicable retained grants and supported single-license reviews are incomplete or ambiguous.", "applicable-license-evidence", "license-investigation"),
    "owner-evidence-required": ("owner evidence required", "A checked applicability review identifies an unpublished owner-issued grant as necessary.", "owner-issued-grant", "license-owner"),
    "incompatible-dependency-license": ("gap", None, None, None),
    "dependencies-policy-satisfied": ("verified", None, None, None),
}

# Deliberate normative expectations, not branches obtained from production output.
CASES = (
    ("missing-package", "missing-package", "Root bytes precede every other assessment input."),
    ("missing-scope", "scope-missing", "A capture cannot choose the caller's assessment TFMs."),
    ("missing-closure", "evidence-missing", "A receipt alone is not a resolved closure."),
    ("missing-capture", "evidence-missing", "An unobserved graph is not an acquisition completeness basis."),
    ("acquisition-failed", "closure-unresolved", "Failed acquisition cannot manufacture a complete inventory."),
    ("single-mit-at-grant-limit", "dependencies-policy-satisfied", "A complete prevalidated grant at 16384 bytes qualifies."),
    ("direct-transitive", "dependencies-policy-satisfied", "Both direct MIT and transitive BSD evidence are necessary."),
    ("zero-dependencies", "dependencies-policy-satisfied", "A checked empty graph in every requested target permits a scoped vacuous finding."),
    ("multi-tfm-shared", "dependencies-policy-satisfied", "Shared exact versions deduplicate evidence but retain each target's edges."),
    ("multi-tfm-versions", "dependencies-policy-satisfied", "Different versions in different targets both remain in the assessed union."),
    ("direct-gpl", "incompatible-dependency-license", "A fully evidenced direct strong-copyleft license violates the pinned policy."),
    ("transitive-weak-copyleft", "incompatible-dependency-license", "A fully evidenced transitive weak-copyleft license also violates policy."),
    ("non-osi", "incompatible-dependency-license", "A current catalog ID explicitly not OSI-approved is incompatible."),
    ("metadata-only", "license-evidence-unresolved", "Neither an inventory nor PackageLicenseExpression establishes applicable grants."),
    ("owner-grant", "owner-evidence-required", "Only a reviewed genuinely unpublished owner grant routes to an owner."),
    ("dual-expression", "license-evidence-unresolved", "No license election is invented for OR, even when both terms might qualify."),
    ("conjunctive-expression", "license-evidence-unresolved", "AND is outside this single-license profile, not implicitly interpreted."),
    ("custom-expression", "license-evidence-unresolved", "Unknown custom terms do not create a non-OSI gap."),
    ("missing-dependency", "dependency-bytes-missing", "An unavailable exact dependency version is unresolved."),
    ("wrong-root-id", "binding-conflict", "Root ID binds independently of root version and digest."),
    ("wrong-root-version", "binding-conflict", "Root version binds independently of root ID and digest."),
    ("wrong-root-digest", "binding-conflict", "A receipt for different root bytes is unusable."),
    ("missing-tfm", "closure-unresolved", "Every requested target must be captured, not just the first."),
    ("dangling-edge", "closure-unresolved", "Every direct and transitive edge must resolve to an exact node."),
    ("unreachable-node", "closure-unresolved", "Unreachable inventory cannot silently be counted as closure membership."),
    ("incomplete-basis", "closure-unresolved", "Acquisition target counts must agree with the retained full graph."),
    ("same-tfm-version-conflict", "closure-unresolved", "One ID with competing versions in a single target is not a solver decision."),
    ("duplicate-key", "input-invalid", "Duplicate JSON fields are rejected, not last-wins."),
    ("unknown-field", "input-invalid", "A caller-authored complete=true is an unsupported field, not evidence."),
    ("unsafe-grant-path", "input-invalid", "Retained and in-package grant paths must be safe canonical relative paths."),
    ("grant-over-limit", "input-invalid", "16385 grant bytes exceed the inclusive 16384-byte profile."),
    ("wrong-grant-binding", "binding-conflict", "Retained grant bytes must match capture and review digests."),
    ("wrong-package-binding", "binding-conflict", "Dependency archive identities and roles bind exactly."),
    ("wrong-graph-binding", "binding-conflict", "The acquisition receipt must bind the entire retained graph digest."),
    ("wrong-review-binding", "binding-conflict", "A review for different grant bytes cannot establish applicability."),
    ("missing-policy", "policy-unavailable", "No positive or gap can substitute an unpinned policy."),
    ("incomplete-before-gap", "closure-unresolved", "Incomplete graph takes precedence over a recorded GPL dependency."),
    ("missing-evidence-before-gap", "license-evidence-unresolved", "This conservative profile defers gap when another grant is unavailable."),
    ("binding-before-gap", "binding-conflict", "An invalid evidence binding preempts a would-be policy gap."),
    ("changed-policy", "policy-unavailable", "Even internally plausible replacement policy bytes cannot override the pin."),
)


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def encoded(value):
    return (json.dumps(value, indent=2, ensure_ascii=True) + "\n").encode("ascii")


def safe_path(value):
    return (
        isinstance(value, str) and 0 < len(value) <= 256
        and re.fullmatch(r"[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)*", value) is not None
        and all(part not in (".", "..") for part in value.split("/"))
    )


def fields(value, names):
    require(type(value) is dict and set(value) == set(names.split()), "invalid-fields")


def text(value, maximum=256):
    require(type(value) is str and 0 < len(value) <= maximum
            and bool(value.strip(" "))
            and all(32 <= ord(char) <= 126 for char in value), "invalid-string")


def sha(value):
    require(type(value) is str and re.fullmatch(r"[a-f0-9]{64}", value) is not None, "invalid-sha256")


def identity(value):
    fields(value, "id version sha256")
    require(type(value["id"]) is str and
            re.fullmatch(r"[a-z0-9][a-z0-9.-]{0,63}", value["id"]) is not None, "invalid-id")
    require(type(value["version"]) is str and
            re.fullmatch(r"(?:0|[1-9][0-9]{0,4})(?:\.(?:0|[1-9][0-9]{0,4})){2}", value["version"]) is not None,
            "unsupported-exact-version")
    sha(value["sha256"])


def target(value):
    require(type(value) is str and re.fullmatch(r"[a-z0-9][a-z0-9.-]{0,63}", value) is not None,
            "invalid-target")


def key(value):
    text(value, 128)
    parts = value.split("/")
    require(len(parts) == 2, "invalid-package-key")
    identity({"id": parts[0], "version": parts[1], "sha256": "0" * 64})


def array(value, maximum):
    require(type(value) is list and len(value) <= maximum, "invalid-array-or-limit")


def document(data):
    require(len(data) <= 65536, "document-limit")

    def pairs(items):
        result = {}
        for name, value in items:
            require(name not in result, "duplicate-field")
            result[name] = value
        return result

    try:
        result = json.loads(data.decode("utf-8"), object_pairs_hook=pairs,
                            parse_constant=lambda value: require(False, "non-json-number"))
    except (UnicodeError, json.JSONDecodeError, RecursionError) as error:
        raise ValueError("invalid-json") from error

    def bound(value, depth):
        require(depth <= 8, "document-depth")
        if isinstance(value, dict):
            for name, child in value.items():
                text(name)
                bound(child, depth + 1)
        elif isinstance(value, list):
            for child in value:
                bound(child, depth + 1)
        elif isinstance(value, str):
            text(value, 1024)
        else:
            require(value is None or type(value) is int, "invalid-scalar")

    bound(result, 0)
    return result


def pinned_inputs(inputs, artifacts):
    result = {}
    for item in inputs:
        fields(item, "role path sha256")
        text(item["role"], 64)
        require(item["role"] not in result, "duplicate-role")
        require(safe_path(item["path"]), "invalid-relative-path")
        sha(item["sha256"])
        require(item["path"] in artifacts, "missing-file")
        data = artifacts[item["path"]]
        require(digest(data) == item["sha256"], "input-digest-mismatch")
        result[item["role"]] = (item, data)
    return result


def shape(scope, capture, graph, reviews, checked):
    fields(scope, "schema_version root target_frameworks")
    require(type(scope["schema_version"]) is int and scope["schema_version"] == 1, "schema-version")
    identity(scope["root"])
    array(scope["target_frameworks"], 4)
    require(scope["target_frameworks"], "empty-target-scope")
    for tfm in scope["target_frameworks"]:
        target(tfm)
    require(len(set(scope["target_frameworks"])) == len(scope["target_frameworks"]), "duplicate-target")
    if capture is not None:
        fields(capture, "schema_version producer root graph_sha256 target_frameworks result completeness_basis packages capture_reference")
        require(type(capture["schema_version"]) is int and capture["schema_version"] == 1
                and capture["producer"] == PRODUCER, "capture-profile")
        identity(capture["root"])
        array(capture["target_frameworks"], 4)
        for tfm in capture["target_frameworks"]:
            target(tfm)
        text(capture["capture_reference"])
        require(capture["result"] in ("captured", "unavailable"), "capture-result")
        if capture["graph_sha256"] is not None:
            sha(capture["graph_sha256"])
        fields(capture["completeness_basis"], "profile targets")
        require(capture["completeness_basis"]["profile"] == "resolved-target-graph/1", "basis-profile")
        array(capture["completeness_basis"]["targets"], 4)
        for item in capture["completeness_basis"]["targets"]:
            fields(item, "tfm node_count edge_count")
            target(item["tfm"])
            require(type(item["node_count"]) is int and 0 <= item["node_count"] <= 64
                    and type(item["edge_count"]) is int and 0 <= item["edge_count"] <= 128, "basis-count")
        array(capture["packages"], 64)
        if capture["result"] == "unavailable":
            require(capture["graph_sha256"] is None and not capture["packages"]
                    and not capture["completeness_basis"]["targets"], "failed-capture-shape")
        for item in capture["packages"]:
            fields(item, "key identity role declared_expression grant")
            key(item["key"])
            identity(item["identity"])
            require(re.fullmatch(r"dependency-[0-9]{3}", item["role"] or "") is not None, "dependency-role")
            if item["declared_expression"] is not None:
                text(item["declared_expression"])
            grant = item["grant"]
            if grant is not None:
                fields(grant, "role path sha256 package_entry")
                require(re.fullmatch(r"grant-[0-9]{3}", grant["role"] or "") is not None, "grant-role")
                require(safe_path(grant["path"]) and safe_path(grant["package_entry"]), "invalid-grant-path")
                sha(grant["sha256"])
    if graph is not None:
        fields(graph, "schema_version root targets unresolved")
        require(type(graph["schema_version"]) is int and graph["schema_version"] == 1, "graph-schema")
        identity(graph["root"])
        array(graph["targets"], 4)
        array(graph["unresolved"], 64)
        for item in graph["unresolved"]:
            text(item)
        for item in graph["targets"]:
            fields(item, "tfm direct nodes")
            target(item["tfm"])
            array(item["direct"], 64)
            array(item["nodes"], 64)
            for dependency in item["direct"]:
                key(dependency)
            for node in item["nodes"]:
                fields(node, "key dependencies")
                key(node["key"])
                array(node["dependencies"], 64)
                for dependency in node["dependencies"]:
                    key(dependency)
    if reviews is not None:
        fields(reviews, "schema_version reviews")
        require(type(reviews["schema_version"]) is int and reviews["schema_version"] == 1, "review-schema")
        array(reviews["reviews"], 64)
        for item in reviews["reviews"]:
            fields(item, "key package_sha256 grant_sha256 license_id scope basis disposition reviewer_reference authority_reference review_reference")
            key(item["key"])
            sha(item["package_sha256"])
            sha(item["grant_sha256"])
            for name in ("license_id", "reviewer_reference", "authority_reference", "review_reference"):
                text(item[name])
            require(item["scope"] == "entire-dependency-version"
                    and item["basis"] in ("published-package-grant", "unpublished-owner-grant")
                    and item["disposition"] in ("applicable-single-license", "owner-grant-required", "unresolved"),
                    "review-profile")
    for role, (_, data) in checked.items():
        require(role in ("package", "assessment", "dependency-graph", "acquisition-receipt", "license-reviews", "policy", "corpus")
                or re.fullmatch(r"(?:dependency|grant)-[0-9]{3}", role) is not None, "unknown-role")
        if role.startswith("grant-"):
            require(0 < len(data) <= 16384, "grant-limit")
            require(data.decode("utf-8").strip() and not data.startswith(b"\xef\xbb\xbf"), "invalid-grant")
        if role == "package" or role.startswith("dependency-") and role != "dependency-graph":
            require(0 < len(data) <= 8 * 1024 * 1024, "package-limit")
    require(sum(len(data) for _, data in checked.values()) <= 64 * 1024 * 1024, "aggregate-limit")


def classify(checked):
    """Classify normalized observations only; never parse packages, solve or interpret grant text."""
    if "package" not in checked:
        return "missing-package"
    if "assessment" not in checked:
        return "scope-missing"
    try:
        scope = document(checked["assessment"][1])
        capture = document(checked["acquisition-receipt"][1]) if "acquisition-receipt" in checked else None
        graph = document(checked["dependency-graph"][1]) if "dependency-graph" in checked else None
        reviews = document(checked["license-reviews"][1]) if "license-reviews" in checked else None
        shape(scope, capture, graph, reviews, checked)
    except (ValueError, UnicodeError, TypeError):
        return "input-invalid"
    if not all(role in checked for role in ("policy", "corpus")):
        return "policy-unavailable"
    if digest(checked["policy"][1]) != POLICY_SHA or digest(checked["corpus"][1]) != CORPUS_SHA:
        return "policy-unavailable"
    if scope["root"]["sha256"] != checked["package"][0]["sha256"]:
        return "binding-conflict"
    root_fact = PACKAGE_FACTS.get(checked["package"][0]["sha256"])
    if root_fact is None:
        return "input-invalid"
    if scope["root"] != root_fact["identity"]:
        return "binding-conflict"
    if capture is None:
        return "evidence-missing"
    if capture["root"] != scope["root"]:
        return "binding-conflict"
    if capture["result"] != "captured":
        return "closure-unresolved"
    if graph is None:
        return "evidence-missing"
    if graph["root"] != scope["root"] or capture["graph_sha256"] != checked["dependency-graph"][0]["sha256"]:
        return "binding-conflict"
    requested = sorted(scope["target_frameworks"])
    if (sorted(capture["target_frameworks"]) != requested
            or sorted(item["tfm"] for item in graph["targets"]) != requested
            or graph["unresolved"]):
        return "closure-unresolved"
    union = set()
    counts = []
    for item in graph["targets"]:
        nodes = {node["key"]: node["dependencies"] for node in item["nodes"]}
        all_edges = item["direct"] + [edge for deps in nodes.values() for edge in deps]
        if (len(nodes) != len(item["nodes"]) or len(set(item["direct"])) != len(item["direct"])
                or any(len(set(edges)) != len(edges) for edges in nodes.values())
                or len(all_edges) > 128 or any(edge not in nodes for edge in all_edges)
                or len({name.split("/")[0] for name in nodes}) != len(nodes)):
            return "closure-unresolved"
        reached = set()
        pending = list(item["direct"])
        while pending:
            name = pending.pop()
            if name not in reached:
                reached.add(name)
                pending.extend(nodes[name])
        if reached != set(nodes):
            return "closure-unresolved"
        union.update(nodes)
        counts.append({"tfm": item["tfm"], "node_count": len(nodes), "edge_count": len(all_edges)})
    if len(union) > 64 or sorted(counts, key=lambda item: item["tfm"]) != sorted(
            capture["completeness_basis"]["targets"], key=lambda item: item["tfm"]):
        return "closure-unresolved"
    inventory = {item["key"]: item for item in capture["packages"]}
    if len(inventory) != len(capture["packages"]) or set(inventory) != union:
        return "closure-unresolved"
    review_map = {item["key"]: item for item in reviews["reviews"]} if reviews else {}
    if reviews and (len(review_map) != len(reviews["reviews"]) or not set(review_map) <= union):
        return "binding-conflict"
    used = {"package", "assessment", "dependency-graph", "acquisition-receipt", "policy", "corpus"}
    if reviews is not None:
        used.add("license-reviews")
    missing_package = False
    unresolved = False
    owner = False
    incompatible = False
    policy = json.loads(checked["policy"][1])
    catalog = {item["licenseId"]: item for item in json.loads(checked["corpus"][1])["licenses"]}
    for index, name in enumerate(sorted(union)):
        item = inventory[name]
        package_identity = item["identity"]
        if (name != f"{package_identity['id']}/{package_identity['version']}"
                or item["role"] != f"dependency-{index:03d}"):
            return "binding-conflict"
        role = item["role"]
        used.add(role)
        if role not in checked:
            missing_package = True
        elif checked[role][0]["sha256"] != package_identity["sha256"]:
            return "binding-conflict"
        elif PACKAGE_FACTS.get(package_identity["sha256"], {}).get("identity") != package_identity:
            return "binding-conflict"
        elif PACKAGE_FACTS[package_identity["sha256"]]["expression"] != item["declared_expression"]:
            return "binding-conflict"
        grant = item["grant"]
        review = review_map.get(name)
        if grant is not None:
            if grant["role"] != f"grant-{index:03d}":
                return "binding-conflict"
            used.add(grant["role"])
            if grant["role"] in checked:
                grant_input = checked[grant["role"]][0]
                if (grant_input["path"] != grant["path"] or grant_input["sha256"] != grant["sha256"]):
                    return "binding-conflict"
            if role in checked and (
                    grant["package_entry"] != "LICENSE.txt"
                    or PACKAGE_FACTS[package_identity["sha256"]]["grant_sha256"] != grant["sha256"]):
                return "binding-conflict"
        if review and (review["package_sha256"] != package_identity["sha256"]
                       or grant is not None and review["grant_sha256"] != grant["sha256"]):
            return "binding-conflict"
        if grant is None or grant["role"] not in checked or review is None:
            unresolved = True
            continue
        expression = item["declared_expression"]
        if expression != review["license_id"]:
            return "binding-conflict"
        if review["disposition"] == "owner-grant-required":
            if expression == "LicenseRef-owner-grant" and review["basis"] == "unpublished-owner-grant":
                owner = True
            else:
                unresolved = True
            continue
        if (review["disposition"] != "applicable-single-license" or review["basis"] != "published-package-grant"
                or expression not in catalog or catalog[expression]["isDeprecatedLicenseId"]):
            unresolved = True
            continue
        if expression in policy["non_copyleft_allowlist"]:
            continue
        if (expression in policy["copyleft"] or expression in policy["weak_copyleft"]
                or not catalog[expression]["isOsiApproved"]):
            incompatible = True
        else:
            unresolved = True
    if set(checked) - used:
        return "binding-conflict"
    if missing_package:
        return "dependency-bytes-missing"
    if unresolved:
        return "license-evidence-unresolved"
    if owner:
        return "owner-evidence-required"
    return "incompatible-dependency-license" if incompatible else "dependencies-policy-satisfied"


def result(branch, inputs):
    status, missing, required, category = BRANCHES[branch]
    deterministic = status in ("verified", "gap")
    finding = None
    if status == "verified":
        finding = (
            "Within the exact root and requested target-framework union, every distinct dependency "
            "version has checked bytes and caller-prevalidated applicable grant evidence for a pinned "
            "OSI-approved, non-copyleft single license; an empty checked closure is vacuously satisfied. "
            "Acquisition completeness and review authority are accepted evidence boundaries, not independently attested."
        )
    elif status == "gap":
        finding = (
            "At least one exact dependency version in the checked target-framework union has "
            "caller-prevalidated applicable grant evidence for a license incompatible with the pinned "
            "OSI-approved, non-copyleft policy. This is a scoped LP-03 gap, not a legal opinion."
        )
    return {
        "row_id": "LP-03", "resolver_id": "lp03-dependency-licenses", "resolver_version": "1.0.0",
        "branch_id": branch, "decision": "deterministic" if deterministic else "escalated",
        "applicability": "applicable" if deterministic or status == "owner evidence required" else "unknown",
        "inputs": copy.deepcopy(inputs),
        "evidence_references": [f"input:{item['role']}:{item['sha256']}" for item in inputs],
        "evidence_contract": CONTRACT, "finding": finding, "missing_fact": missing,
        "required_input": required, "next_category": category,
    }


def package(package_id, version, expression, grant, dependency_groups=None):
    """Generate bounded synthetic archives; deliberately no ZIP/XML reader in the oracle."""
    output = io.BytesIO()
    groups = "".join(
        f'<group targetFramework="{tfm}">'
        + "".join(f'<dependency id="{name.split("/")[0]}" version="[{name.split("/")[1]}]" />'
                  for name in dependencies)
        + "</group>"
        for tfm, dependencies in sorted((dependency_groups or {}).items())
    )
    with zipfile.ZipFile(output, "w") as archive:
        for path, data in {
            "fixture.nuspec": (
                f"<package><metadata><id>{package_id}</id><version>{version}</version>"
                f'<license type="expression">{expression}</license><dependencies>{groups}</dependencies>'
                "</metadata></package>"
            ).encode("ascii"),
            "LICENSE.txt": grant,
        }.items():
            entry = zipfile.ZipInfo(path, (2020, 1, 1, 0, 0, 0))
            entry.create_system = 3
            entry.external_attr = 0o100644 << 16
            entry.compress_type = zipfile.ZIP_STORED
            archive.writestr(entry, data)
    data = output.getvalue()
    PACKAGE_FACTS[digest(data)] = {
        "identity": {"id": package_id, "version": version, "sha256": digest(data)},
        "expression": expression, "grant_sha256": digest(grant),
    }
    return data


def build():
    artifacts = {}
    PACKAGE_FACTS.clear()

    def retain(path, data):
        require(path not in artifacts or artifacts[path] == data, "artifact-collision")
        artifacts[path] = data
        return {"role": "", "path": path, "sha256": digest(data)}

    policy = (LP01 / "policy" / "lp01-policy-1.0.0.json").read_bytes()
    corpus = (LP01 / "policy" / "spdx-licenses-3.27.0.json").read_bytes()
    require(digest(policy) == POLICY_SHA and digest(corpus) == CORPUS_SHA, "accepted-policy-pin-changed")
    retain("policy/lp01-policy-1.0.0.json", policy)
    retain("policy/spdx-licenses-3.27.0.json", corpus)
    mit = (LP01 / "grants" / "mit-full.txt").read_bytes()
    bsd = (LP01 / "grants" / "bsd-full.txt").read_bytes()
    cases = []
    for name, branch, rationale in CASES:
        local = {"policy": ("policy/lp01-policy-1.0.0.json", policy),
                 "corpus": ("policy/spdx-licenses-3.27.0.json", corpus)}
        scope = {"schema_version": 1, "root": None, "target_frameworks": ["net8.0"]}
        nodes = [{"key": "sample.a/1.0.0", "dependencies": ["sample.b/1.0.0"]},
                 {"key": "sample.b/1.0.0", "dependencies": []}]
        graph = {"schema_version": 1, "root": None,
                 "targets": [{"tfm": "net8.0", "direct": ["sample.a/1.0.0"], "nodes": nodes}],
                 "unresolved": []}
        licenses = {"sample.a/1.0.0": "MIT", "sample.b/1.0.0": "BSD-2-Clause"}
        if name == "single-mit-at-grant-limit":
            nodes[0]["dependencies"] = []
            nodes.pop()
            licenses.pop("sample.b/1.0.0")
        if name == "zero-dependencies":
            graph["targets"][0]["direct"] = []
            nodes.clear()
            licenses.clear()
        if name.startswith("multi-tfm") or name == "missing-tfm":
            scope["target_frameworks"].append("net9.0")
            second = copy.deepcopy(graph["targets"][0])
            second["tfm"] = "net9.0"
            if name == "multi-tfm-versions":
                second["direct"] = ["sample.a/2.0.0"]
                second["nodes"][0]["key"] = "sample.a/2.0.0"
                licenses["sample.a/2.0.0"] = "MIT"
            graph["targets"].append(second)
        if name in ("direct-gpl", "incomplete-before-gap", "missing-evidence-before-gap", "binding-before-gap"):
            licenses["sample.a/1.0.0"] = "GPL-3.0-only"
        if name == "transitive-weak-copyleft":
            licenses["sample.b/1.0.0"] = "LGPL-2.1-only"
        if name == "non-osi":
            licenses["sample.a/1.0.0"] = "CC-BY-NC-4.0"
        if name in ("dual-expression", "conjunctive-expression", "custom-expression", "owner-grant"):
            licenses["sample.a/1.0.0"] = {
                "dual-expression": "MIT OR BSD-2-Clause", "conjunctive-expression": "MIT AND BSD-2-Clause",
                "custom-expression": "LicenseRef-unclassified", "owner-grant": "LicenseRef-owner-grant",
            }[name]
        root_bytes = package("sample.root", "1.0.0", "MIT", mit,
                             {item["tfm"]: item["direct"] for item in graph["targets"]})
        root = {"id": "sample.root", "version": "1.0.0", "sha256": digest(root_bytes)}
        local["package"] = (f"packages/root.{digest(root_bytes)[:12]}.nupkg", root_bytes)
        scope["root"] = copy.deepcopy(root)
        graph["root"] = copy.deepcopy(root)
        capture = {"schema_version": 1, "producer": PRODUCER, "root": copy.deepcopy(root),
                   "graph_sha256": None, "target_frameworks": copy.deepcopy(scope["target_frameworks"]),
                   "result": "captured", "completeness_basis": {"profile": "resolved-target-graph/1", "targets": []},
                   "packages": [], "capture_reference": "synthetic-capture/1"}
        reviews = {"schema_version": 1, "reviews": []}
        for index, (node_key, expression) in enumerate(sorted(licenses.items())):
            package_id, version = node_key.split("/")
            grant = mit if expression == "MIT" else bsd if expression == "BSD-2-Clause" else (
                f"Synthetic fixture grant for {package_id} {version}.\n"
                f"This entire synthetic package is offered solely under {expression}.\n"
                "This original fixture statement is not a copy of the referenced license text.\n"
            ).encode("ascii")
            if name == "owner-grant" and index == 0:
                grant = b"This synthetic package requires an individual unpublished grant from Example Owner.\n"
            if name in ("single-mit-at-grant-limit", "grant-over-limit") and index == 0:
                grant += b" " * ((16384 if name == "single-mit-at-grant-limit" else 16385) - len(grant))
            groups = {
                item["tfm"]: node["dependencies"] for item in graph["targets"]
                for node in item["nodes"] if node["key"] == node_key
            }
            pkg = package(package_id, version, expression, grant, groups)
            pkg_path = f"packages/{package_id}.{version}.{digest(pkg)[:12]}.nupkg"
            grant_path = f"grants/{digest(grant)}.txt"
            local[f"dependency-{index:03d}"] = (pkg_path, pkg)
            local[f"grant-{index:03d}"] = (grant_path, grant)
            capture["packages"].append({
                "key": node_key, "identity": {"id": package_id, "version": version, "sha256": digest(pkg)},
                "role": f"dependency-{index:03d}", "declared_expression": expression,
                "grant": {"role": f"grant-{index:03d}", "path": grant_path,
                          "sha256": digest(grant), "package_entry": "LICENSE.txt"},
            })
            reviews["reviews"].append({
                "key": node_key, "package_sha256": digest(pkg), "grant_sha256": digest(grant),
                "license_id": expression, "scope": "entire-dependency-version",
                "basis": "unpublished-owner-grant" if name == "owner-grant" and index == 0 else "published-package-grant",
                "disposition": "owner-grant-required" if name == "owner-grant" and index == 0 else "applicable-single-license",
                "reviewer_reference": "synthetic-reviewer/1", "authority_reference": "synthetic-grant-authority/1",
                "review_reference": "synthetic-applicability-review/1",
            })
        if name == "dangling-edge":
            nodes[0]["dependencies"] = ["sample.missing/1.0.0"]
        if name == "unreachable-node":
            graph["targets"][0]["direct"] = []
        if name == "same-tfm-version-conflict":
            nodes.append({"key": "sample.a/2.0.0", "dependencies": []})
            nodes[0]["dependencies"].append("sample.a/2.0.0")
        if name == "incomplete-before-gap":
            graph["unresolved"] = ["sample.missing: exact version unavailable"]
        if name == "missing-tfm":
            graph["targets"].pop()
        for item in graph["targets"]:
            capture["completeness_basis"]["targets"].append({
                "tfm": item["tfm"], "node_count": len(item["nodes"]),
                "edge_count": len(item["direct"]) + sum(len(node["dependencies"]) for node in item["nodes"]),
            })
        if name == "incomplete-basis":
            capture["completeness_basis"]["targets"][0]["node_count"] = 1
        if name in ("wrong-root-id", "wrong-root-version", "wrong-root-digest"):
            field = {"wrong-root-id": "id", "wrong-root-version": "version", "wrong-root-digest": "sha256"}[name]
            capture["root"][field] = {"id": "other.root", "version": "2.0.0", "sha256": "0" * 64}[field]
            if name == "wrong-root-id":
                scope["root"][field] = capture["root"][field]
                graph["root"][field] = capture["root"][field]
        if name in ("wrong-package-binding", "binding-before-gap"):
            capture["packages"][0]["identity"]["sha256"] = "0" * 64
        if name == "wrong-grant-binding":
            capture["packages"][0]["grant"]["sha256"] = "0" * 64
        if name == "wrong-review-binding":
            reviews["reviews"][0]["grant_sha256"] = "0" * 64
        if name == "unsafe-grant-path":
            capture["packages"][0]["grant"]["package_entry"] = "../LICENSE.txt"
        if name == "unknown-field":
            capture["complete"] = True
        if name == "metadata-only":
            for item in capture["packages"]:
                item["grant"] = None
            local = {role: value for role, value in local.items() if not role.startswith("grant-")}
            reviews = None
        if name == "missing-evidence-before-gap":
            local.pop("grant-001")
        if name == "missing-dependency":
            local.pop("dependency-001")
        graph_bytes = encoded(graph)
        capture["graph_sha256"] = digest(graph_bytes)
        if name == "wrong-graph-binding":
            capture["graph_sha256"] = "0" * 64
        if name == "acquisition-failed":
            capture.update(result="unavailable", graph_sha256=None, packages=[])
            capture["completeness_basis"]["targets"] = []
            local = {role: value for role, value in local.items()
                     if not role.startswith(("dependency-", "grant-"))}
            reviews = None
        capture_bytes = encoded(capture)
        if name == "duplicate-key":
            capture_bytes = capture_bytes.replace(b'"schema_version": 1,', b'"schema_version": 1, "schema_version": 1,', 1)
        local["assessment"] = (f"inputs/{name}.assessment.json", encoded(scope))
        local["dependency-graph"] = (f"inputs/{name}.graph.json", graph_bytes)
        local["acquisition-receipt"] = (f"inputs/{name}.capture.json", capture_bytes)
        if reviews is not None:
            local["license-reviews"] = (f"inputs/{name}.reviews.json", encoded(reviews))
        for case_name, role in (("missing-package", "package"), ("missing-scope", "assessment"),
                                ("missing-closure", "dependency-graph"), ("missing-capture", "acquisition-receipt"),
                                ("missing-policy", "policy")):
            if name == case_name:
                local.pop(role)
        if name == "changed-policy":
            changed = json.loads(policy)
            changed["policy_version"] = "1.0.1"
            local["policy"] = ("policy/unsupported-policy.json", encoded(changed))
        inputs = []
        for role, (path, data) in sorted(local.items()):
            item = retain(path, data)
            item["role"] = role
            inputs.append(item)
        expected = result(branch, inputs)
        cases.append({
            "case_id": f"lp03-{name}", "row_id": "LP-03", "inputs": inputs, "expected": expected,
            "rationale": rationale, "evidence_pointers": expected["evidence_references"],
        })
    require(len(cases) == 40, "case-count")
    artifacts["cases.json"] = encoded({"schema_version": 1, "cases": cases})
    baseline = next(item for item in cases if item["case_id"] == "lp03-direct-transitive")["inputs"]
    controls = []
    for name, rejection in (
        ("duplicate-role", "duplicate-role"), ("parent-path", "invalid-relative-path"),
        ("missing-file", "missing-file"), ("changed-bytes", "input-digest-mismatch"),
        ("unknown-input-field", "invalid-fields"),
    ):
        inputs = copy.deepcopy(baseline)
        if name == "duplicate-role":
            inputs.append(copy.deepcopy(inputs[0]))
        elif name == "parent-path":
            inputs[0]["path"] = "../outside.json"
        elif name == "missing-file":
            inputs[0]["path"] = "inputs/not-retained.json"
        elif name == "changed-bytes":
            inputs[0]["sha256"] = "0" * 64
        else:
            inputs[0]["complete"] = True
        controls.append({"case_id": f"lp03-reject-{name}", "stage": "shared-input-loader",
                         "expected_rejection": rejection, "inputs": inputs})
    artifacts["rejected-inputs.json"] = encoded({"schema_version": 1, "controls": controls})
    vendors = json.loads((LP01 / "retained-inputs.json").read_bytes())["vendors"]
    retained = []
    bindings = []
    for vendor in vendors:
        inputs = [{"role": "package", "path": f"packages/{vendor['package_id']}.{vendor['version']}.nupkg",
                   "sha256": vendor["sha256"]}]
        expected = result("scope-missing", inputs)
        retained.append({
            "case_id": f"lp03-retained-{vendor['vendor']}", "row_id": "LP-03", "inputs": inputs,
            "expected": expected,
            "rationale": "Exact root bytes alone supply neither explicit assessment TFMs, a closure nor dependency license evidence.",
            "evidence_pointers": expected["evidence_references"],
        })
        bindings.append({name: vendor[name] for name in ("vendor", "package_id", "version", "size_bytes", "sha256")})
        bindings[-1]["binding"] = f"LP03_{'SYNCFUSION' if vendor['vendor'] == 'syncfusion' else 'INFRAGISTICS'}_NUPKG"
    artifacts["acceptance.json"] = encoded({"schema_version": 1, "cases": retained})
    artifacts["retained-inputs.json"] = encoded({"schema_version": 1, "vendors": bindings})
    input_index = [{"path": path, "size_bytes": len(data), "sha256": digest(data)}
                   for path, data in sorted(artifacts.items())
                   if path.startswith(("inputs/", "packages/", "grants/", "policy/"))]
    artifacts["input-manifest.json"] = encoded({"schema_version": 1, "inputs": input_index})
    artifacts["acceptance-manifest.json"] = encoded({
        "schema_version": 1, "phase": "A", "row_id": "LP-03", "scope": "repository-wide",
        "area": "Licensing and provenance", "clause": "1.3",
        "obligation": "All direct and transitive dependencies use OSI-approved, non-copyleft licenses.",
        "resolver_id": "lp03-dependency-licenses", "resolver_version": "1.0.0",
        "production_implementation": "absent; LP-03 remains tbd",
        "synthetic_cases": 40, "rejected_inputs": 5, "retained_expectations": 2, "total_named_cases": 47,
        "input_count": len(input_index), "input_bytes": sum(item["size_bytes"] for item in input_index),
        "branch_status_mapping": {branch: details[0] for branch, details in BRANCHES.items()},
        "manifest_sha256": {path: digest(artifacts[path]) for path in
                            ("cases.json", "rejected-inputs.json", "acceptance.json", "retained-inputs.json", "input-manifest.json")},
        "contract_sha256": digest((ROOT / "README.md").read_bytes()),
        "policy_sha256": POLICY_SHA, "spdx_sha256": CORPUS_SHA,
    })
    return artifacts


def check(artifacts):
    suite = json.loads(artifacts["cases.json"])
    for case in suite["cases"]:
        actual = classify(pinned_inputs(case["inputs"], artifacts))
        require(result(actual, case["inputs"]) == case["expected"], f"{case['case_id']}: expected {case['expected']['branch_id']}, got {actual}")
    for control in json.loads(artifacts["rejected-inputs.json"])["controls"]:
        try:
            pinned_inputs(control["inputs"], artifacts)
        except ValueError as error:
            require(str(error) == control["expected_rejection"], f"{control['case_id']}: wrong rejection: {error}")
        else:
            raise ValueError(f"{control['case_id']}: accepted rejected input")
    require({case["expected"]["branch_id"] for case in suite["cases"]} == set(BRANCHES), "branch-coverage")


def retained_bindings(bindings, supplied):
    allowed = {vendor["binding"] for vendor in bindings}
    require(not set(supplied) - allowed, "unknown-retained-binding")
    require(set(supplied) == allowed, "both-retained-bindings-required")
    checked = {}
    for vendor in bindings:
        path = Path(supplied[vendor["binding"]])
        require(path.is_file(), "retained-file-missing")
        require(path.stat().st_size == vendor["size_bytes"], "retained-size-mismatch")
        data = path.read_bytes()
        require(len(data) == vendor["size_bytes"], "retained-size-mismatch")
        require(digest(data) == vendor["sha256"], "retained-digest-mismatch")
        checked[vendor["binding"]] = data
    return checked


def check_retained(artifacts, requested):
    bindings = json.loads(artifacts["retained-inputs.json"])["vendors"]
    supplied = {name: value for name, value in os.environ.items() if name.startswith("LP03_")}
    # Negative binding checks cannot depend on local proprietary package availability.
    for values, expected in (
        ({}, "both-retained-bindings-required"),
        ({bindings[0]["binding"]: "not-opened"}, "both-retained-bindings-required"),
        ({"LP03_MISSPELLED_NUPKG": "not-opened"}, "unknown-retained-binding"),
    ):
        try:
            retained_bindings(bindings, values)
        except ValueError as error:
            require(str(error) == expected, f"retained-negative-check: {error}")
        else:
            raise ValueError("retained-negative-check accepted")
    with tempfile.TemporaryDirectory(prefix="lp03-binding-check-") as directory:
        wrong = Path(directory) / "not-a-vendor-package.nupkg"
        wrong.write_bytes(b"x")
        for size_check, expected in ((True, "retained-size-mismatch"), (False, "retained-digest-mismatch")):
            miniature = copy.deepcopy(bindings)
            if not size_check:
                for vendor in miniature:
                    vendor["size_bytes"] = 1
            try:
                retained_bindings(miniature, {vendor["binding"]: str(wrong) for vendor in miniature})
            except ValueError as error:
                require(str(error) == expected, f"retained-byte-negative-check: {error}")
            else:
                raise ValueError("retained-byte-negative-check accepted")
    if not supplied and not requested:
        return "not run (both local bindings absent; negative binding checks passed)"
    checked_roots = retained_bindings(bindings, supplied)
    for vendor, case in zip(bindings, json.loads(artifacts["acceptance.json"])["cases"]):
        data = checked_roots[vendor["binding"]]
        checked = {"package": (case["inputs"][0], data)}
        require(result(classify(checked), case["inputs"]) == case["expected"], "retained-expectation-mismatch")
    return "2 exact retained roots checked; both scope-missing / not tested"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true", help="Materialize only independently specified synthetic expectations.")
    parser.add_argument("--retained", action="store_true", help="Require both exact locally supplied retained-package bindings.")
    args = parser.parse_args()
    artifacts = build()
    check(artifacts)
    if args.write and (ROOT / "input-manifest.json").is_file():
        old_inputs = json.loads((ROOT / "input-manifest.json").read_bytes())["inputs"]
        for item in old_inputs:
            path = item["path"]
            require(safe_path(path) and path.startswith(("inputs/", "packages/", "grants/", "policy/")),
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
            require(path.is_file() and path.read_bytes() == data, f"frozen-oracle-mismatch: {name}")
    if not args.write:
        actual = {path.relative_to(ROOT).as_posix() for folder in ("inputs", "packages", "grants", "policy")
                  for path in (ROOT / folder).rglob("*") if path.is_file()}
        expected = {name for name in artifacts if name.startswith(("inputs/", "packages/", "grants/", "policy/"))}
        require(actual == expected, "unindexed-or-missing-input")
    manifest = json.loads(artifacts["acceptance-manifest.json"])
    print(f"LP-03 Phase A: 40 oracle cases, 5 loader rejections, 12/12 branches; "
          f"{manifest['input_count']} inputs / {manifest['input_bytes']} bytes.")
    print(f"Retained: {check_retained(artifacts, args.retained)}.")
    print(f"Input manifest SHA-256: {digest(artifacts['input-manifest.json'])}")
    print("No resolver, package-manager execution, license-text interpretation or network used.")


if __name__ == "__main__":
    main()
