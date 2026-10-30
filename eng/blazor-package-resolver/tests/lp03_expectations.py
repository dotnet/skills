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
    ("missing-closure", "evidence-missing", "A receipt alone is not a resolved closure."),
    ("missing-capture", "evidence-missing", "An unobserved graph is not an acquisition completeness basis."),
    ("acquisition-failed", "closure-unresolved", "Failed acquisition cannot manufacture a complete inventory."),
    ("zero-dependencies", "dependencies-policy-satisfied", "A checked empty graph in every requested target permits a scoped vacuous finding."),
    ("multi-tfm-versions", "dependencies-policy-satisfied", "Keep both per-target versions and a shared transitive version, with a complete grant at the inclusive 16384-byte limit."),
    ("multi-tfm-second-gap", "incompatible-dependency-license", "Complete GPL evidence for a dependency unique to the second target must refute the union."),
    ("multi-tfm-second-unresolved", "license-evidence-unresolved", "Only the second target's unique dependency lacks a grant; the first target alone cannot establish a positive."),
    ("transitive-weak-copyleft", "incompatible-dependency-license", "A fully evidenced transitive weak-copyleft license also violates policy."),
    ("non-osi", "incompatible-dependency-license", "A current catalog ID explicitly not OSI-approved is incompatible."),
    ("missing-reviews", "license-evidence-unresolved", "All grants, packages and closure are present; absence of the entire applicability-review document alone prevents a positive."),
    ("missing-one-review", "license-evidence-unresolved", "All grants and packages are present; one missing review in a multi-dependency closure prevents a positive."),
    ("missing-evidence-before-gap", "license-evidence-unresolved", "Only a transitive grant is absent; its absence preempts another node's complete GPL evidence."),
    ("owner-before-gap", "owner-evidence-required", "A reviewed unpublished owner grant requirement precedes another dependency's complete incompatible evidence."),
    ("osi-unclassified", "license-evidence-unresolved", "Current OSI-approved EUPL-1.2 is outside every finite local classification; neither allow nor deny is inferred."),
    ("deprecated-id", "license-evidence-unresolved", "Deprecated BSD-2-Clause-FreeBSD is unresolved before its catalog non-OSI flag could create a gap."),
    ("with-expression", "license-evidence-unresolved", "An MIT WITH exception cannot be stripped to infer an allowed single license."),
    ("dual-expression", "license-evidence-unresolved", "No license election is invented for OR, even when both terms might qualify."),
    ("conjunctive-expression", "license-evidence-unresolved", "AND is outside this single-license profile, not implicitly interpreted."),
    ("custom-expression", "license-evidence-unresolved", "Unknown custom terms do not create a non-OSI gap."),
    ("missing-dependency", "dependency-bytes-missing", "An unavailable exact dependency version is unresolved."),
    ("wrong-root-id", "binding-conflict", "Root ID binds independently of root version and digest."),
    ("wrong-root-version", "binding-conflict", "Root version binds independently of root ID and digest."),
    ("wrong-graph-root", "binding-conflict", "A rehashed graph for different root bytes conflicts even though the receipt binds its new graph digest."),
    ("receipt-target-mismatch", "closure-unresolved", "Only the receipt target set differs; the scope, graph and completeness target sets still agree."),
    ("dangling-edge", "closure-unresolved", "Every direct and transitive edge must resolve to an exact node."),
    ("incomplete-basis", "closure-unresolved", "Only acquisition node counts are wrong; incomplete closure evidence preempts complete GPL evidence."),
    ("same-tfm-version-conflict", "closure-unresolved", "Every node, package, grant, edge and count is present, but competing versions of one ID within one target are unresolved."),
    ("duplicate-key", "input-invalid", "Duplicate JSON fields are rejected, not last-wins."),
    ("unknown-field", "input-invalid", "A caller-authored complete=true is an unsupported field, not evidence."),
    ("unsafe-grant-path", "input-invalid", "Retained and in-package grant paths must be safe canonical relative paths."),
    ("grant-over-limit", "input-invalid", "16385 grant bytes exceed the inclusive 16384-byte profile."),
    ("archive-declaration-mismatch", "binding-conflict", "Receipt and review consistently claim BSD but the exact dependency archive declares MIT; every digest still agrees."),
    ("review-unresolved", "license-evidence-unresolved", "Only the review disposition is unresolved; all applicable-looking declarations and byte bindings agree."),
    ("review-nonaccepting-basis", "license-evidence-unresolved", "A single-license review with a non-accepting unpublished-grant basis cannot establish a positive."),
    ("review-package-binding", "binding-conflict", "Only the applicability review targets a different package digest; package and grant bindings otherwise agree."),
    ("packaged-grant-binding", "binding-conflict", "Retained grant bytes, path, capture digest and review digest agree with each other, but not with the actual packaged entry."),
    ("dependency-role-index", "binding-conflict", "A consistently renamed checked dependency role still violates the sorted-union role index."),
    ("duplicate-reviews", "binding-conflict", "Two identical applicable reviews for one node cannot be silently deduplicated."),
    ("out-of-inventory-review", "binding-conflict", "An extra otherwise well-shaped review for an unassessed version must not be ignored."),
    ("unexpected-evidence-role", "binding-conflict", "An extra safe, checked grant role outside the inventory is not harmless evidence."),
    ("later-binding-before-unresolved", "binding-conflict", "The first node lacks only its retained grant; a later node's review-package conflict must still win."),
    ("changed-policy", "policy-unavailable", "Even internally plausible replacement policy bytes cannot override the pin."),
)

# Fixed weakened-oracle variants reuse these exact cases, never add input variants.
MUTATIONS = (
    ("optional-reviews", (("missing-reviews", "dependencies-policy-satisfied"), ("missing-one-review", "dependencies-policy-satisfied"))),
    ("first-target-licenses", (("multi-tfm-second-gap", "dependencies-policy-satisfied"), ("multi-tfm-second-unresolved", "dependencies-policy-satisfied"))),
    ("unclassified-allow", (("osi-unclassified", "dependencies-policy-satisfied"),)),
    ("unclassified-gap", (("osi-unclassified", "incompatible-dependency-license"),)),
    ("deprecated-id", (("deprecated-id", "incompatible-dependency-license"),)),
    ("strip-compound", (("with-expression", "dependencies-policy-satisfied"), ("dual-expression", "dependencies-policy-satisfied"), ("conjunctive-expression", "dependencies-policy-satisfied"))),
    ("archive-declaration", (("archive-declaration-mismatch", "dependencies-policy-satisfied"),)),
    ("review-disposition", (("review-unresolved", "dependencies-policy-satisfied"),)),
    ("review-basis", (("review-nonaccepting-basis", "dependencies-policy-satisfied"),)),
    ("review-package", (("review-package-binding", "dependencies-policy-satisfied"),)),
    ("packaged-grant", (("packaged-grant-binding", "dependencies-policy-satisfied"),)),
    ("receipt-targets", (("receipt-target-mismatch", "dependencies-policy-satisfied"),)),
    ("graph-root", (("wrong-graph-root", "dependencies-policy-satisfied"),)),
    ("dependency-role", (("dependency-role-index", "dependencies-policy-satisfied"),)),
    ("duplicate-reviews", (("duplicate-reviews", "dependencies-policy-satisfied"),)),
    ("out-of-inventory-review", (("out-of-inventory-review", "dependencies-policy-satisfied"),)),
    ("unexpected-role", (("unexpected-evidence-role", "dependencies-policy-satisfied"),)),
    ("gap-before-owner", (("owner-before-gap", "incompatible-dependency-license"),)),
    ("early-missing-license", (("later-binding-before-unresolved", "license-evidence-unresolved"),)),
    ("root-identity", (("wrong-root-id", "dependencies-policy-satisfied"),)),
    ("receipt-root", (("wrong-root-version", "dependencies-policy-satisfied"),)),
    ("completeness-counts", (("incomplete-basis", "incompatible-dependency-license"),)),
    ("same-target-versions", (("same-tfm-version-conflict", "dependencies-policy-satisfied"),)),
    ("policy-pin", (("changed-policy", "dependencies-policy-satisfied"),)),
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


def classify(checked, weakened_rule=None):
    """Classify normalized observations only; never parse packages, solve or interpret grant text."""
    require(weakened_rule is None or weakened_rule in {name for name, _ in MUTATIONS}, "unknown-weakened-rule")
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
    if weakened_rule != "policy-pin" and (
            digest(checked["policy"][1]) != POLICY_SHA or digest(checked["corpus"][1]) != CORPUS_SHA):
        return "policy-unavailable"
    if scope["root"]["sha256"] != checked["package"][0]["sha256"]:
        return "binding-conflict"
    root_fact = PACKAGE_FACTS.get(checked["package"][0]["sha256"])
    if root_fact is None:
        return "input-invalid"
    if weakened_rule != "root-identity" and scope["root"] != root_fact["identity"]:
        return "binding-conflict"
    if capture is None:
        return "evidence-missing"
    if weakened_rule != "receipt-root" and capture["root"] != scope["root"]:
        return "binding-conflict"
    if capture["result"] != "captured":
        return "closure-unresolved"
    if graph is None:
        return "evidence-missing"
    if (weakened_rule != "graph-root" and graph["root"] != scope["root"]
            or capture["graph_sha256"] != checked["dependency-graph"][0]["sha256"]):
        return "binding-conflict"
    requested = sorted(scope["target_frameworks"])
    if (weakened_rule != "receipt-targets" and sorted(capture["target_frameworks"]) != requested
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
                or len(all_edges) > 128 or any(edge not in nodes for edge in all_edges)):
            return "closure-unresolved"
        if (weakened_rule != "same-target-versions"
                and len({name.split("/")[0] for name in nodes}) != len(nodes)):
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
    if len(union) > 64 or weakened_rule != "completeness-counts" and sorted(
            counts, key=lambda item: item["tfm"]) != sorted(
                capture["completeness_basis"]["targets"], key=lambda item: item["tfm"]):
        return "closure-unresolved"
    inventory = {item["key"]: item for item in capture["packages"]}
    if len(inventory) != len(capture["packages"]) or set(inventory) != union:
        return "closure-unresolved"
    review_map = {item["key"]: item for item in reviews["reviews"]} if reviews else {}
    if reviews and (
            weakened_rule != "duplicate-reviews" and len(review_map) != len(reviews["reviews"])
            or weakened_rule != "out-of-inventory-review" and not set(review_map) <= union):
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
                or weakened_rule != "dependency-role" and item["role"] != f"dependency-{index:03d}"):
            return "binding-conflict"
        role = item["role"]
        used.add(role)
        if role not in checked:
            missing_package = True
        elif checked[role][0]["sha256"] != package_identity["sha256"]:
            return "binding-conflict"
        elif PACKAGE_FACTS.get(package_identity["sha256"], {}).get("identity") != package_identity:
            return "binding-conflict"
        elif (weakened_rule != "archive-declaration"
              and PACKAGE_FACTS[package_identity["sha256"]]["expression"] != item["declared_expression"]):
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
            if weakened_rule != "packaged-grant" and role in checked and (
                    grant["package_entry"] != "LICENSE.txt"
                    or PACKAGE_FACTS[package_identity["sha256"]]["grant_sha256"] != grant["sha256"]):
                return "binding-conflict"
        if review and (weakened_rule != "review-package" and review["package_sha256"] != package_identity["sha256"]
                       or grant is not None and review["grant_sha256"] != grant["sha256"]):
            return "binding-conflict"
        if weakened_rule == "first-target-licenses" and name not in {
                node["key"] for node in graph["targets"][0]["nodes"]}:
            continue
        if grant is None or grant["role"] not in checked or review is None:
            if weakened_rule == "optional-reviews" and review is None and grant and grant["role"] in checked:
                continue
            if weakened_rule == "early-missing-license":
                return "license-evidence-unresolved"
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
        if weakened_rule == "strip-compound":
            expression = expression.split(" ", 1)[0]
        if (weakened_rule != "review-disposition" and review["disposition"] != "applicable-single-license"
                or weakened_rule != "review-basis" and review["basis"] != "published-package-grant"
                or expression not in catalog
                or weakened_rule != "deprecated-id" and catalog[expression]["isDeprecatedLicenseId"]):
            unresolved = True
            continue
        if expression in policy["non_copyleft_allowlist"]:
            continue
        if (expression in policy["copyleft"] or expression in policy["weak_copyleft"]
                or not catalog[expression]["isOsiApproved"]):
            incompatible = True
        else:
            if weakened_rule == "unclassified-gap":
                incompatible = True
            elif weakened_rule != "unclassified-allow":
                unresolved = True
    if weakened_rule != "unexpected-role" and set(checked) - used:
        return "binding-conflict"
    if missing_package:
        return "dependency-bytes-missing"
    if unresolved:
        return "license-evidence-unresolved"
    if owner and not (weakened_rule == "gap-before-owner" and incompatible):
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
    catalog = {item["licenseId"]: item for item in json.loads(corpus)["licenses"]}
    classifications = json.loads(policy)
    require(catalog["EUPL-1.2"]["isOsiApproved"] and not catalog["EUPL-1.2"]["isDeprecatedLicenseId"]
            and all("EUPL-1.2" not in classifications[category]
                    for category in ("non_copyleft_allowlist", "copyleft", "weak_copyleft")),
            "unclassified-witness-not-independent")
    require(catalog["BSD-2-Clause-FreeBSD"]["isDeprecatedLicenseId"]
            and not catalog["BSD-2-Clause-FreeBSD"]["isOsiApproved"], "deprecated-witness-not-independent")
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
        if name == "zero-dependencies":
            graph["targets"][0]["direct"] = []
            nodes.clear()
            licenses.clear()
        if name.startswith("multi-tfm"):
            scope["target_frameworks"].append("net9.0")
            second = copy.deepcopy(graph["targets"][0])
            second["tfm"] = "net9.0"
            if name == "multi-tfm-versions":
                second["direct"] = ["sample.a/2.0.0"]
                second["nodes"][0]["key"] = "sample.a/2.0.0"
                licenses["sample.a/2.0.0"] = "MIT"
            else:
                second["direct"].append("sample.c/1.0.0")
                second["nodes"].append({"key": "sample.c/1.0.0", "dependencies": []})
                licenses["sample.c/1.0.0"] = "GPL-3.0-only" if name == "multi-tfm-second-gap" else "MIT"
            graph["targets"].append(second)
        if name == "same-tfm-version-conflict":
            nodes.append({"key": "sample.a/2.0.0", "dependencies": []})
            nodes[0]["dependencies"].append("sample.a/2.0.0")
            licenses["sample.a/2.0.0"] = "MIT"
        if name in ("incomplete-basis", "missing-evidence-before-gap"):
            licenses["sample.a/1.0.0"] = "GPL-3.0-only"
        if name == "transitive-weak-copyleft":
            licenses["sample.b/1.0.0"] = "LGPL-2.1-only"
        if name == "non-osi":
            licenses["sample.a/1.0.0"] = "CC-BY-NC-4.0"
        if name in ("osi-unclassified", "deprecated-id", "with-expression",
                    "dual-expression", "conjunctive-expression", "custom-expression", "owner-before-gap"):
            licenses["sample.a/1.0.0"] = {
                "osi-unclassified": "EUPL-1.2", "deprecated-id": "BSD-2-Clause-FreeBSD",
                "with-expression": "MIT WITH LLVM-exception",
                "dual-expression": "MIT OR BSD-2-Clause", "conjunctive-expression": "MIT AND BSD-2-Clause",
                "custom-expression": "LicenseRef-unclassified", "owner-before-gap": "LicenseRef-owner-grant",
            }[name]
        if name == "owner-before-gap":
            licenses["sample.b/1.0.0"] = "GPL-3.0-only"
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
            if name == "owner-before-gap" and index == 0:
                grant = b"This synthetic package requires an individual unpublished grant from Example Owner.\n"
            if name in ("multi-tfm-versions", "grant-over-limit") and index == 0:
                grant += b" " * ((16384 if name == "multi-tfm-versions" else 16385) - len(grant))
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
                "basis": "unpublished-owner-grant" if name == "owner-before-gap" and index == 0 else "published-package-grant",
                "disposition": "owner-grant-required" if name == "owner-before-gap" and index == 0 else "applicable-single-license",
                "reviewer_reference": "synthetic-reviewer/1", "authority_reference": "synthetic-grant-authority/1",
                "review_reference": "synthetic-applicability-review/1",
            })
        if name == "dangling-edge":
            nodes[0]["dependencies"].append("sample.missing/1.0.0")
        for item in graph["targets"]:
            capture["completeness_basis"]["targets"].append({
                "tfm": item["tfm"], "node_count": len(item["nodes"]),
                "edge_count": len(item["direct"]) + sum(len(node["dependencies"]) for node in item["nodes"]),
            })
        if name == "incomplete-basis":
            capture["completeness_basis"]["targets"][0]["node_count"] = 1
        if name in ("wrong-root-id", "wrong-root-version"):
            field = "id" if name == "wrong-root-id" else "version"
            capture["root"][field] = "other.root" if field == "id" else "2.0.0"
            if name == "wrong-root-id":
                scope["root"][field] = capture["root"][field]
                graph["root"][field] = capture["root"][field]
        if name == "wrong-graph-root":
            graph["root"]["sha256"] = "0" * 64
        if name == "receipt-target-mismatch":
            capture["target_frameworks"] = ["net9.0"]
        if name == "archive-declaration-mismatch":
            capture["packages"][0]["declared_expression"] = "BSD-2-Clause"
            reviews["reviews"][0]["license_id"] = "BSD-2-Clause"
        if name == "review-unresolved":
            reviews["reviews"][0]["disposition"] = "unresolved"
        if name == "review-nonaccepting-basis":
            reviews["reviews"][0]["basis"] = "unpublished-owner-grant"
        if name == "review-package-binding":
            reviews["reviews"][0]["package_sha256"] = "0" * 64
        if name == "packaged-grant-binding":
            grant = mit + b"\nSynthetic retained-only annotation.\n"
            grant_path = f"grants/{digest(grant)}.txt"
            local["grant-000"] = (grant_path, grant)
            capture["packages"][0]["grant"].update(path=grant_path, sha256=digest(grant))
            reviews["reviews"][0]["grant_sha256"] = digest(grant)
        if name == "dependency-role-index":
            capture["packages"][0]["role"] = "dependency-009"
            local["dependency-009"] = local.pop("dependency-000")
        if name == "duplicate-reviews":
            reviews["reviews"].append(copy.deepcopy(reviews["reviews"][0]))
        if name == "out-of-inventory-review":
            extra = copy.deepcopy(reviews["reviews"][0])
            extra["key"] = "sample.unlisted/1.0.0"
            reviews["reviews"].append(extra)
        if name == "unexpected-evidence-role":
            local["grant-999"] = local["grant-000"]
        if name == "unsafe-grant-path":
            capture["packages"][0]["grant"]["package_entry"] = "../LICENSE.txt"
        if name == "unknown-field":
            capture["complete"] = "caller-asserted"
        if name == "missing-reviews":
            reviews = None
        if name == "missing-one-review":
            reviews["reviews"].pop()
        if name == "missing-evidence-before-gap":
            local.pop("grant-001")
        if name == "multi-tfm-second-unresolved":
            local.pop("grant-002")
        if name == "later-binding-before-unresolved":
            local.pop("grant-000")
            reviews["reviews"][1]["package_sha256"] = "0" * 64
        if name == "missing-dependency":
            local.pop("dependency-001")
        graph_bytes = encoded(graph)
        capture["graph_sha256"] = digest(graph_bytes)
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
        for case_name, role in (("missing-package", "package"), ("missing-closure", "dependency-graph"),
                                ("missing-capture", "acquisition-receipt")):
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
    require(len(cases) == 43, "case-count")
    artifacts["cases.json"] = encoded({"schema_version": 1, "cases": cases})
    baseline = next(item for item in cases if item["case_id"] == "lp03-multi-tfm-versions")["inputs"]
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
    artifacts["discrimination.json"] = encoded({
        "schema_version": 1,
        "method": "fixed-weakened-oracle-rules-existing-inputs-only",
        "variants": [{"weakened_rule": rule, "witnesses": [
            {"case_id": f"lp03-{name}", "weakened_branch": branch} for name, branch in witnesses]}
            for rule, witnesses in MUTATIONS],
    })
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
        "phase_a_correction_parent": "b91de23a90490f6f219e3183d5c19d160da498ea",
        "synthetic_cases": 43, "rejected_inputs": 5, "retained_expectations": 2, "total_named_cases": 50,
        "weakened_rule_variants": len(MUTATIONS), "existing_case_witness_runs": sum(len(items) for _, items in MUTATIONS),
        "input_count": len(input_index), "input_bytes": sum(item["size_bytes"] for item in input_index),
        "branch_status_mapping": {branch: details[0] for branch, details in BRANCHES.items()},
        "manifest_sha256": {path: digest(artifacts[path]) for path in
                            ("cases.json", "rejected-inputs.json", "acceptance.json", "retained-inputs.json",
                             "input-manifest.json", "discrimination.json")},
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
    retained = json.loads(artifacts["acceptance.json"])["cases"]
    controls = json.loads(artifacts["rejected-inputs.json"])["controls"]
    ids = [case["case_id"] for case in suite["cases"] + retained + controls]
    require(len(ids) == len(set(ids)), "duplicate-expectation-id")
    require({case["expected"]["branch_id"] for case in suite["cases"] + retained} == set(BRANCHES), "branch-coverage")
    require(len(ids) == 50, "named-expectation-limit")
    require(len(MUTATIONS) == 24 and len({rule for rule, _ in MUTATIONS}) == 24, "bounded-mutation-limit")
    by_id = {case["case_id"]: case for case in suite["cases"]}
    for rule, witnesses in MUTATIONS:
        for name, weakened_branch in witnesses:
            case = by_id[f"lp03-{name}"]
            actual = classify(pinned_inputs(case["inputs"], artifacts), weakened_rule=rule)
            require(actual == weakened_branch and actual != case["expected"]["branch_id"],
                    f"non-discriminating-matrix: {rule}, {name}: got {actual}, expected weakened {weakened_branch}")


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
    print(f"LP-03 Phase A: 43 oracle cases, 5 loader rejections, 2 retained expectations, 12 branch expectations; "
          f"{manifest['input_count']} inputs / {manifest['input_bytes']} bytes.")
    print(f"Discrimination: {len(MUTATIONS)} fixed weakened-rule variants rejected by "
          f"{sum(len(items) for _, items in MUTATIONS)} existing-case witness runs; no additional inputs.")
    print(f"Retained: {check_retained(artifacts, args.retained)}.")
    print(f"Input manifest SHA-256: {digest(artifacts['input-manifest.json'])}")
    print("No resolver, package-manager execution, license-text interpretation or network used.")


if __name__ == "__main__":
    main()
