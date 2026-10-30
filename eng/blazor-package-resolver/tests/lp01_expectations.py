"""Freeze LP-01 package license-policy expectations without dispatching a resolver."""

import argparse
import hashlib
import io
import json
import os
import re
import subprocess
import sys
import unicodedata
import warnings
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path
from xml.parsers import expat

ROOT = Path(__file__).parent / "fixtures" / "lp01"
POLICY = ROOT / "policy" / "lp01-policy-1.0.0.json"
CATALOG = ROOT / "policy" / "spdx-licenses-3.27.0.json"
CATALOG_SHA = "157789ba91984aecba5c67b84168dbbd41a235b910a7569aa64bf42a88b24290"
NS = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"
LICENSE = '<license type="expression">{}</license>'
ZIP_DATE = (2020, 1, 1, 0, 0, 0)
MAX_EXPRESSION_BYTES = 4096
MAX_EXPRESSION_TOKENS = 255
MAX_EXPRESSION_DEPTH = 16
MAX_ALTERNATIVES = 128
MAX_GRANT_BYTES = 16384
TERM = r"(?:DocumentRef-[A-Za-z0-9.-]+:)?LicenseRef-[A-Za-z0-9.-]+|[A-Za-z0-9][A-Za-z0-9.+-]*"
LEXEME = re.compile(rf"(?:{TERM})|[()]")
REPOSITORY = "https://example.test/fixture/owner-lp01"
COMMIT = "a" * 40
RELEASE = "refs/tags/v1.0.0"
GRANT = (b"SPDX-License-Identifier: MIT\nMIT License\n\n"
         b"Copyright (c) 2026 Example Fixture Maintainer\n\n"
         b"Permission is hereby granted, free of charge, to any person obtaining a copy\n"
         b"of this software and associated documentation files (the \"Software\"), to deal\n"
         b"in the Software without restriction, including without limitation the rights\n"
         b"to use, copy, modify, merge, publish, distribute, sublicense, and/or sell\n"
         b"copies of the Software, and to permit persons to whom the Software is\n"
         b"furnished to do so, subject to the following conditions:\n\n"
         b"The above copyright notice and this permission notice shall be included in all\n"
         b"copies or substantial portions of the Software.\n\n"
         b"THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR\n"
         b"IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,\n"
         b"FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE\n"
         b"AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER\n"
         b"LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,\n"
         b"OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE\n"
         b"SOFTWARE.\n")
GRANT_PATH = "grants/mit-full.txt"
BSD_GRANT = (
    b"SPDX-License-Identifier: BSD-2-Clause\nBSD 2-Clause License\n\n"
    b"Copyright (c) 2026 Example Fixture Maintainer\nAll rights reserved.\n\n"
    b"Redistribution and use in source and binary forms, with or without\n"
    b"modification, are permitted provided that the following conditions are met:\n\n"
    b"1. Redistributions of source code must retain the above copyright notice,\n"
    b"   this list of conditions and the following disclaimer.\n"
    b"2. Redistributions in binary form must reproduce the above copyright notice,\n"
    b"   this list of conditions and the following disclaimer in the documentation\n"
    b"   and/or other materials provided with the distribution.\n\n"
    b"THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS \"AS IS\"\n"
    b"AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE\n"
    b"IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE\n"
    b"ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE\n"
    b"LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR\n"
    b"CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF\n"
    b"SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS\n"
    b"INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN\n"
    b"CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)\n"
    b"ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE\n"
    b"POSSIBILITY OF SUCH DAMAGE.\n"
)
GRANTS = {"MIT": ("grants/mit-full.txt", GRANT), "BSD-2-Clause": ("grants/bsd-full.txt", BSD_GRANT)}
OWNER_ORDER = (
    "schema_version", "caller_accepts_prevalidated_authority", "owner_identity",
    "owner_reference", "package_id", "package_sha256", "repository_url",
    "source_commit", "release_ref", "release_version", "spdx_grant_expression",
    "grant_bindings", "prevalidation_references",
    "policy_version", "policy_sha256",
    "spdx_version", "spdx_sha256",
)
OWNER_FIELDS = frozenset(OWNER_ORDER)
BINDING_ORDER = ("license_id", "role", "path", "sha256", "caller_accepts_complete_grant")
VENDORS = (
    ("syncfusion", "syncfusion.blazor.toolkit.2.0.1.nupkg",
     "87cf6795afe5106c9d6c4db5e63e7f0ef0c741a850243557b3bbab1854c6121d",
     4912251, "Syncfusion.Blazor.Toolkit.nuspec",
     "e9a55efdfc3385c1df49206839689f3f01c0bf5731b6130f05a601632a77acb7"),
    ("infragistics", "igniteui.blazor.lite.0.3.0.nupkg",
     "e9565c7c7093ff3798b62abdc2e67fe9bc438807eb0fa9b9b43f4573f9df0e63",
     1417667, "IgniteUI.Blazor.Lite.nuspec",
     "8399959e38391faa853f874f6d4c2d10a2724f3e978af5fc79fbd46a7a74833c"),
)


def require(condition, message):
    if not condition:
        raise ValueError(message)


def encoded(value):
    return (json.dumps(value, indent=2, ensure_ascii=True) + "\n").encode()


def canonical_owner_bytes(record):
    ordered = {key: record[key] for key in OWNER_ORDER}
    ordered["grant_bindings"] = [
        {key: binding[key] for key in BINDING_ORDER}
        for binding in record["grant_bindings"]
    ]
    return encoded(ordered)


def sha(content):
    return hashlib.sha256(content).hexdigest()


def policy_data():
    policy_bytes = POLICY.read_bytes()
    catalog_bytes = CATALOG.read_bytes()
    require(sha(catalog_bytes) == CATALOG_SHA, "Pinned SPDX catalog SHA-256 differs.")
    policy = json.loads(policy_bytes)
    catalog = json.loads(catalog_bytes)
    validate_policy(policy, catalog, sha(catalog_bytes))
    return policy_bytes, catalog_bytes, policy, {
        item["licenseId"]: item for item in catalog["licenses"]
    }


def validate_policy(policy, catalog, catalog_digest):
    require(policy["policy_version"] == "1.0.0" and
            policy["spdx_version"] == catalog["licenseListVersion"] == "3.27.0" and
            policy["spdx_sha256"] == catalog_digest == CATALOG_SHA,
            "LP-01 policy provenance is inconsistent.")
    licenses = {item["licenseId"]: item for item in catalog["licenses"]}
    require(len(licenses) == len(catalog["licenses"]), "Duplicate SPDX license identifiers.")
    groups = [policy[name] for name in ("non_copyleft_allowlist", "copyleft", "weak_copyleft")]
    ids = [name for group in groups for name in group]
    require(len(ids) == len(set(ids)) and all(
        name in licenses and licenses[name]["isOsiApproved"] and
        not licenses[name].get("isDeprecatedLicenseId", False) for name in ids),
        "LP-01 policy classifications overlap, are deprecated or lack OSI approval.")


class ExpressionLimitError(ValueError):
    pass


class ProhibitedXmlDeclaration(ValueError):
    pass


def parse_expression(text):
    if len(text) > MAX_EXPRESSION_BYTES or len(text.encode("utf-8")) > MAX_EXPRESSION_BYTES:
        raise ExpressionLimitError("SPDX expression exceeds the character/byte cap.")
    require(all(ord(char) < 128 and (char.isalnum() or char in ".+-(): \t\r\n")
                for char in text), "Invalid SPDX character or non-ASCII whitespace.")
    tokens = []
    offset = 0
    while offset < len(text):
        if text[offset] in " \t\r\n":
            offset += 1
            continue
        token = LEXEME.match(text, offset)
        require(token is not None, "Invalid SPDX token.")
        tokens.append(token.group())
        if len(tokens) > MAX_EXPRESSION_TOKENS:
            raise ExpressionLimitError("SPDX expression exceeds the token cap.")
        offset = token.end()
    position = 0

    def atom(depth):
        nonlocal position
        require(position < len(tokens), "Incomplete SPDX expression.")
        if tokens[position] == "(":
            if depth >= MAX_EXPRESSION_DEPTH:
                raise ExpressionLimitError("SPDX expression exceeds the nesting cap.")
            position += 1
            value = alternatives(depth + 1)
            require(position < len(tokens) and tokens[position] == ")", "Unclosed SPDX group.")
            position += 1
            return value
        token = tokens[position]
        require(re.fullmatch(TERM, token) is not None and
                token not in ("AND", "OR", "WITH"), "Invalid SPDX license term.")
        position += 1
        if position < len(tokens) and tokens[position] == "WITH":
            position += 1
            require(position < len(tokens) and
                    re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9.+-]*", tokens[position]) is not None and
                    tokens[position] not in ("AND", "OR", "WITH"),
                    "Missing SPDX exception.")
            exception = tokens[position]
            position += 1
            return [frozenset((token, "WITH:" + exception))], ("WITH", token, exception)
        return [frozenset((token,))], ("ID", token)

    def conjunction(depth):
        nonlocal position
        result, tree = atom(depth)
        while position < len(tokens) and tokens[position] == "AND":
            position += 1
            right, right_tree = atom(depth)
            if len(result) * len(right) > MAX_ALTERNATIVES:
                raise ExpressionLimitError("SPDX expression exceeds the alternative cap.")
            result = [left | term for left in result for term in right]
            tree = ("AND", tree, right_tree)
        return result, tree

    def alternatives(depth):
        nonlocal position
        result, tree = conjunction(depth)
        while position < len(tokens) and tokens[position] == "OR":
            position += 1
            right, right_tree = conjunction(depth)
            if len(result) + len(right) > MAX_ALTERNATIVES:
                raise ExpressionLimitError("SPDX expression exceeds the alternative cap.")
            result += right
            tree = ("OR", tree, right_tree)
        return result, tree

    require(tokens, "Empty SPDX expression.")
    options, tree = alternatives(0)
    require(position == len(tokens), "Trailing or misplaced SPDX tokens.")
    return options, tree


def canonical_expression(tree):
    operator = tree[0]
    if operator in ("ID", "WITH"):
        return tree
    children = []
    for child in tree[1:]:
        normalized = canonical_expression(child)
        children.extend(normalized[1:] if normalized[0] == operator else (normalized,))
    unique = tuple(sorted(set(children)))
    return unique[0] if len(unique) == 1 else (operator, *unique)


def expression_state(text, policy, licenses):
    """Evaluate every SPDX alternative; an unreviewed choice cannot become a positive."""
    options, _ = parse_expression(text)
    terms = set().union(*options)
    if any(term.startswith("WITH:") for term in terms):
        return "exception-unreviewed"
    if any(term.startswith(("LicenseRef-", "DocumentRef-")) for term in terms):
        return "custom-license"
    if any(term not in licenses for term in terms):
        return "unknown-license"
    if any(licenses[term].get("isDeprecatedLicenseId", False) or term.endswith("+")
           for term in terms):
        return "deprecated-license"
    if any(not licenses[term]["isOsiApproved"] for term in terms):
        if all(any(not licenses[term]["isOsiApproved"] for term in option) for option in options):
            return "non-osi"
        return "mixed-alternatives"
    allowed = set(policy["non_copyleft_allowlist"])
    strong = set(policy["copyleft"])
    weak = set(policy["weak_copyleft"])
    if all(option <= allowed for option in options):
        return "approved-non-copyleft"
    if all(option & (strong | weak) for option in options):
        if all(option & weak and not option & strong for option in options):
            return "weak-copyleft"
        return "copyleft"
    if any(option & (strong | weak) for option in options) and any(
            option <= allowed for option in options):
        return "mixed-alternatives"
    return "unclassified-osi"


# Exact expected branch is independent of the expression parser's result.
# name, metadata, branch, optional packaged license text
SPECS = (
    ("mit", LICENSE.format(" MIT "), "approved-non-copyleft", None),
    ("apache", LICENSE.format("Apache-2.0"), "approved-non-copyleft", None),
    ("bsd", LICENSE.format("BSD-3-Clause"), "approved-non-copyleft", None),
    ("and-permissive", LICENSE.format("MIT AND Apache-2.0"), "approved-non-copyleft", None),
    ("or-permissive", LICENSE.format("MIT OR BSD-3-Clause"), "approved-non-copyleft", None),
    ("parenthesized", LICENSE.format("( MIT OR Apache-2.0 ) AND BSD-2-Clause"), "approved-non-copyleft", None),
    ("nested", LICENSE.format("MIT AND ( Apache-2.0 OR ( BSD-2-Clause AND ISC ) )"), "approved-non-copyleft", None),
    ("old-namespace", LICENSE.format("MIT"), "approved-non-copyleft", None),
    ("no-namespace", LICENSE.format("MIT"), "approved-non-copyleft", None),
    ("type-case", '<license type="Expression">MIT</license>', "approved-non-copyleft", None),
    ("supported-version", '<license type="expression" version="1.0.0">MIT</license>', "approved-non-copyleft", None),
    ("namespace-declaration", '<license xmlns:hint="urn:harmless" type="expression">MIT</license>', "approved-non-copyleft", None),
    ("gpl", LICENSE.format("GPL-3.0-only"), "copyleft", None),
    ("agpl", LICENSE.format("AGPL-3.0-only"), "copyleft", None),
    ("gpl-and-mit", LICENSE.format("MIT AND GPL-3.0-only"), "copyleft", None),
    ("gpl-or-agpl", LICENSE.format("GPL-3.0-only OR AGPL-3.0-only"), "copyleft", None),
    ("gpl-or-mpl", LICENSE.format("GPL-3.0-only OR MPL-2.0"), "copyleft", None),
    ("mpl", LICENSE.format("MPL-2.0"), "weak-copyleft", None),
    ("lgpl", LICENSE.format("LGPL-2.1-only"), "weak-copyleft", None),
    ("epl-and-mit", LICENSE.format("EPL-2.0 AND MIT"), "weak-copyleft", None),
    ("mit-or-gpl", LICENSE.format("MIT OR GPL-3.0-only"), "mixed-alternatives", None),
    ("mit-or-mpl", LICENSE.format("MIT OR MPL-2.0"), "mixed-alternatives", None),
    ("cc0", LICENSE.format("CC0-1.0"), "non-osi", None),
    ("mit-and-cc0", LICENSE.format("MIT AND CC0-1.0"), "non-osi", None),
    ("mit-or-cc0", LICENSE.format("MIT OR CC0-1.0"), "mixed-alternatives", None),
    ("osi-unclassified", LICENSE.format("Artistic-2.0"), "unclassified-osi", None),
    ("custom", LICENSE.format("LicenseRef-Private"), "custom-license", None),
    ("document-custom", LICENSE.format("DocumentRef-other:LicenseRef-local"), "custom-license", None),
    ("exception", LICENSE.format("GPL-3.0-only WITH Classpath-exception-2.0"), "exception-unreviewed", None),
    ("exception-or", LICENSE.format("MIT WITH OR"), "invalid-expression", None),
    ("exception-with", LICENSE.format("MIT WITH WITH"), "invalid-expression", None),
    ("exception-and", LICENSE.format("MIT WITH AND"), "invalid-expression", None),
    ("unknown", LICENSE.format("Unknown-1.0"), "unknown-license", None),
    ("deprecated", LICENSE.format("GPL-2.0+"), "deprecated-license", None),
    ("case-sensitive", LICENSE.format("mit"), "unknown-license", None),
    ("plus-suffix", LICENSE.format("GPL-3.0+"), "deprecated-license", None),
    ("bad-operator", LICENSE.format("MIT XOR Apache-2.0"), "invalid-expression", None),
    ("bad-operator-case", LICENSE.format("MIT and Apache-2.0"), "invalid-expression", None),
    ("trailing-term", LICENSE.format("MIT MIT"), "invalid-expression", None),
    ("reserved-term", LICENSE.format("OR"), "invalid-expression", None),
    ("missing-operand", LICENSE.format("MIT AND"), "invalid-expression", None),
    ("unclosed-group", LICENSE.format("(MIT OR Apache-2.0"), "invalid-expression", None),
    ("blank-expression", LICENSE.format("  "), "invalid-expression", None),
    ("nbsp-whitespace", LICENSE.format("MIT\u00a0OR Apache-2.0"), "invalid-expression", None),
    ("em-whitespace", LICENSE.format("MIT\u2003OR Apache-2.0"), "invalid-expression", None),
    ("ascii-whitespace", LICENSE.format("MIT\tAND\nApache-2.0"), "approved-non-copyleft", None),
    ("bytes-at-limit", LICENSE.format("MIT" + " " * (MAX_EXPRESSION_BYTES - 3)), "approved-non-copyleft", None),
    ("bytes-over-limit", LICENSE.format("MIT" + " " * (MAX_EXPRESSION_BYTES - 2)), "expression-limit", None),
    ("multibyte-at-limit", LICENSE.format("MIT" + " " * 4091 + "\u00e9"), "invalid-expression", None),
    ("multibyte-over-limit", LICENSE.format("MIT" + " " * 4092 + "\u00e9"), "expression-limit", None),
    ("tokens-at-limit", LICENSE.format(" OR ".join(["MIT"] * 128)), "approved-non-copyleft", None),
    ("tokens-over-limit", LICENSE.format(" OR ".join(["MIT"] * 129)), "expression-limit", None),
    ("tokens-over-malformed", LICENSE.format(" OR ".join(["MIT"] * 129) + " AND"), "expression-limit", None),
    ("bytes-over-malformed", LICENSE.format("MIT AND" + " " * MAX_EXPRESSION_BYTES), "expression-limit", None),
    ("invalid-char-before-tokens", LICENSE.format("MIT \u00a0 " + " OR MIT" * 129), "invalid-expression", None),
    ("syntax-before-depth", LICENSE.format("MIT AND OR " + "(" * 17 + "MIT" + ")" * 17), "invalid-expression", None),
    ("depth-before-trailing", LICENSE.format("(" * 17 + "MIT" + ")" * 17 + " AND"), "expression-limit", None),
    ("syntax-before-alternatives", LICENSE.format("MIT AND OR (" + " OR ".join(["MIT"] * 8) +
     ") AND (" + " OR ".join(["BSD-2-Clause"] * 17) + ")"), "invalid-expression", None),
    ("alternatives-before-trailing", LICENSE.format("(" + " OR ".join(["MIT"] * 8) +
     ") AND (" + " OR ".join(["BSD-2-Clause"] * 17) + ") AND"), "expression-limit", None),
    ("depth-at-limit", LICENSE.format("(" * MAX_EXPRESSION_DEPTH + "MIT" + ")" * MAX_EXPRESSION_DEPTH), "approved-non-copyleft", None),
    ("depth-over-limit", LICENSE.format("(" * (MAX_EXPRESSION_DEPTH + 1) + "MIT" + ")" * (MAX_EXPRESSION_DEPTH + 1)), "expression-limit", None),
    ("alternatives-at-limit", LICENSE.format("(" + " OR ".join(["MIT"] * 8) +
     ") AND (" + " OR ".join(["Apache-2.0"] * 16) + ")"), "approved-non-copyleft", None),
    ("alternatives-over-limit", LICENSE.format("(" + " OR ".join(["MIT"] * 8) +
     ") AND (" + " OR ".join(["Apache-2.0"] * 17) + ")"), "expression-limit", None),
    ("file-only", '<license type="file">LICENSE.txt</license>', "file-unverified", b"License text alone is not an SPDX grant.\n"),
    ("file-declared", '<license type="file">LICENSE.txt</license>', "file-declared", b"SPDX-License-Identifier: MIT\n"),
    ("file-missing", '<license type="file">LICENSE.txt</license>', "file-unverified", None),
    ("file-blank", '<license type="file">  </license>', "file-unverified", None),
    ("file-unsafe-path", '<license type="file">../LICENSE.txt</license>', "file-unverified", None),
    ("expression-file-match", LICENSE.format("MIT"), "approved-non-copyleft", b"SPDX-License-Identifier: MIT\n"),
    ("expression-file-conflict", LICENSE.format("MIT"), "conflicting-declarations", b"SPDX-License-Identifier: GPL-3.0-only\n"),
    ("url-only", "<licenseUrl>https://licenses.nuget.org/MIT</licenseUrl>", "missing-license", None),
    ("expression-url", LICENSE.format("MIT") + "<licenseUrl>https://licenses.nuget.org/MIT</licenseUrl>", "approved-non-copyleft", None),
    ("expression-url-different", LICENSE.format("MIT") + "<licenseUrl>https://example.test/gpl</licenseUrl>", "approved-non-copyleft", None),
    ("missing", "", "missing-license", None),
    ("unsupported-type", '<license type="url">MIT</license>', "unsafe-metadata", None),
    ("duplicate-license", LICENSE.format("MIT") + LICENSE.format("GPL-3.0-only"), "unsafe-metadata", None),
    ("foreign-license", '<x:license xmlns:x="urn:other" type="expression">MIT</x:license>', "unsafe-metadata", None),
    ("version-newer", '<license type="expression" version="2.0.0">MIT</license>', "unsafe-metadata", None),
    ("unknown-attribute", '<license type="expression" custom="yes">MIT</license>', "unsafe-metadata", None),
    ("invalid-xml", "</metadata", "unsafe-package", None),
    ("dtd-entity", '<license type="expression">&license;</license>', "unsafe-package", None),
    ("dtd-unused", LICENSE.format("MIT"), "unsafe-package", None),
    ("utf16-le-clean", LICENSE.format("MIT"), "approved-non-copyleft", None),
    ("utf16-be-clean", LICENSE.format("MIT"), "approved-non-copyleft", None),
    ("utf16-le-dtd-entity", '<license type="expression">&license;</license>', "unsafe-package", None),
    ("utf16-be-dtd-entity", '<license type="expression">&license;</license>', "unsafe-package", None),
    ("utf16-le-dtd-unused", LICENSE.format("MIT"), "unsafe-package", None),
    ("utf16-be-dtd-unused", LICENSE.format("MIT"), "unsafe-package", None),
    ("duplicate-license-entry", LICENSE.format("MIT"), "unsafe-package", None),
    ("alias-license-entry", LICENSE.format("MIT"), "unsafe-package", None),
    ("duplicate-readme-entry", LICENSE.format("MIT"), "unsafe-package", None),
    ("foreign-root", LICENSE.format("MIT"), "unsafe-package", None),
    ("foreign-metadata", LICENSE.format("MIT"), "unsafe-package", None),
    ("foreign-metadata-after", LICENSE.format("MIT"), "unsafe-package", None),
    ("no-nuspec", LICENSE.format("MIT"), "unsafe-package", None),
    ("multiple-nuspec", LICENSE.format("MIT"), "unsafe-package", None),
    ("subfolder-nuspec", LICENSE.format("MIT"), "unsafe-package", None),
    ("corrupt-archive", "", "unsafe-package", None),
    ("no-package", "", "missing-package", None),
    ("no-policy", LICENSE.format("MIT"), "missing-policy", None),
    ("no-corpus", LICENSE.format("MIT"), "missing-corpus", None),
    ("policy-version-mismatch", LICENSE.format("MIT"), "invalid-policy", None),
    ("policy-overlap", LICENSE.format("MIT"), "invalid-policy", None),
    ("catalog-version-mismatch", LICENSE.format("MIT"), "invalid-policy", None),
)

# name, expected branch. These controls reuse the package reader but add checked roles.
OWNER_SPECS = (
    ("accepted", "owner-evidence-accepted"),
    ("accepted-and", "owner-evidence-accepted"),
    ("accepted-multi", "owner-evidence-accepted"),
    ("accepted-multi-or", "owner-evidence-accepted"),
    ("equivalent-reordered", "owner-evidence-accepted"),
    ("equivalent-flattened", "owner-evidence-accepted"),
    ("copyright-variant", "owner-evidence-accepted"),
    ("untagged-grant", "owner-evidence-accepted"),
    ("grant-at-limit", "owner-evidence-accepted"),
    ("record-at-limit", "owner-evidence-accepted"),
    ("record-over-limit", "owner-record-invalid"),
    ("missing-bsd", "grant-missing"),
    ("missing-bsd-or", "grant-missing"),
    ("missing-bsd-binding", "grant-missing"),
    ("conflicting-term", "grant-contradiction"),
    ("non-equivalent-operator", "owner-binding-conflict"),
    ("non-equivalent-group", "owner-binding-conflict"),
    ("no-record", "owner-record-missing"),
    ("acceptance-false", "owner-acceptance-missing"),
    ("acceptance-absent", "owner-record-invalid"),
    ("wrong-package", "owner-binding-conflict"),
    ("wrong-package-id", "owner-binding-conflict"),
    ("wrong-repository", "owner-binding-conflict"),
    ("wrong-commit", "owner-binding-conflict"),
    ("different-release-assertion", "owner-evidence-accepted"),
    ("wrong-version", "owner-binding-conflict"),
    ("wrong-expression", "owner-binding-conflict"),
    ("equivalent-whitespace", "owner-evidence-accepted"),
    ("wrong-policy-version", "owner-binding-conflict"),
    ("wrong-policy-digest", "owner-binding-conflict"),
    ("wrong-spdx-version", "owner-binding-conflict"),
    ("wrong-spdx-digest", "owner-binding-conflict"),
    ("owner-reference-missing", "owner-record-invalid"),
    ("wrong-grant-digest", "grant-binding-conflict"),
    ("wrong-grant-path", "grant-binding-conflict"),
    ("wrong-grant-role", "grant-binding-conflict"),
    ("duplicate-grant-binding", "grant-binding-conflict"),
    ("no-grant", "grant-missing"),
    ("grant-completeness-false", "grant-incomplete"),
    ("tag-only", "grant-incomplete"),
    ("tag-no-space", "grant-incomplete"),
    ("tag-tab-space", "grant-incomplete"),
    ("tag-repeated", "grant-incomplete"),
    ("tag-indented", "grant-incomplete"),
    ("tag-comment-prefix", "grant-incomplete"),
    ("tag-crlf-gpl", "grant-contradiction"),
    ("tag-cr-gpl", "grant-contradiction"),
    ("tag-repeated-conflict", "grant-contradiction"),
    ("empty-grant", "grant-incomplete"),
    ("invalid-utf8-grant", "grant-incomplete"),
    ("oversize-grant", "grant-limit"),
    ("grant-gpl", "grant-contradiction"),
    ("grant-gpl-indented", "grant-contradiction"),
    ("package-gpl", "copyleft"),
    ("duplicate-license", "unsafe-metadata"),
    ("type-url", "unsafe-metadata"),
    ("type-file", "file-declared"),
    ("unsupported-version", "unsafe-metadata"),
    ("unknown-license-attribute", "unsafe-metadata"),
    ("dtd-entity", "unsafe-package"),
    ("dtd-unused", "unsafe-package"),
    ("utf16-le-clean", "owner-evidence-accepted"),
    ("utf16-be-clean", "owner-evidence-accepted"),
    ("utf16-le-dtd-entity", "unsafe-package"),
    ("utf16-be-dtd-entity", "unsafe-package"),
    ("utf16-le-dtd-unused", "unsafe-package"),
    ("utf16-be-dtd-unused", "unsafe-package"),
    ("duplicate-license-entry", "unsafe-package"),
    ("alias-license-entry", "unsafe-package"),
    ("duplicate-readme-entry", "unsafe-package"),
    ("foreign-metadata", "unsafe-package"),
    ("duplicate-metadata", "unsafe-package"),
    ("multiple-nuspec", "unsafe-package"),
    ("expression-file-conflict", "conflicting-declarations"),
    ("invalid-expression", "invalid-expression"),
    ("over-limit-expression", "expression-limit"),
    ("unknown-field", "owner-record-invalid"),
    ("schema-version", "owner-record-invalid"),
    ("schema-boolean", "owner-record-invalid"),
    ("reversed-fields", "owner-record-invalid"),
    ("duplicate-json-key", "owner-record-invalid"),
    ("nul-field", "owner-record-invalid"),
    ("surrogate-field", "owner-record-invalid"),
    ("non-nfc-field", "owner-record-invalid"),
    ("record-traversal", "owner-record-invalid"),
    ("record-absolute-path", "owner-record-invalid"),
    ("record-separator-alias", "owner-record-invalid"),
    ("record-dot-segment", "owner-record-invalid"),
    ("role-path-mismatch", "grant-binding-conflict"),
    ("unknown-role", "owner-record-invalid"),
    ("extra-role", "owner-record-invalid"),
    ("malformed-record", "owner-record-invalid"),
    ("oversize-record", "owner-record-invalid"),
    ("conflicting-records", "owner-record-conflict"),
)


def entry(name):
    info = zipfile.ZipInfo(name, ZIP_DATE)
    info.create_system = 3
    info.create_version = info.extract_version = 20
    info.external_attr = 0o100644 << 16
    info.compress_type = zipfile.ZIP_STORED
    return info


def archive(name, metadata, file_text):
    if name in ("no-package",):
        return None
    if name == "corrupt-archive":
        return b"not-a-nupkg\n"
    variant = name.removeprefix("owner-")
    namespace = ("urn:other" if variant == "foreign-root" else
                 "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd"
                 if variant == "old-namespace" else "" if variant == "no-namespace" else NS)
    extra = '<x:metadata xmlns:x="urn:other"/>' if variant.startswith("foreign-metadata") else ""
    dtd = ('<!DOCTYPE package [<!ENTITY license "MIT">]>' if
           variant == "dtd-entity" or variant == "dtd-unused" or
           variant.startswith(("utf16-le-dtd-", "utf16-be-dtd-")) else "")
    recognized = (f"<metadata><id>Sample.LP01</id>"
                  f"<version>1.0.0</version>{metadata}"
                  + (f'<repository url="{REPOSITORY}" commit="{COMMIT}"/>' if name.startswith("owner-") else "")
                  + "</metadata>")
    if variant == "duplicate-metadata":
        extra = recognized
    children = recognized + extra if variant == "foreign-metadata-after" else extra + recognized
    attribute = f' xmlns="{namespace}"' if namespace else ""
    xml_encoding = "utf-16" if variant.startswith(("utf16-le-", "utf16-be-")) else "utf-8"
    xml = (f'<?xml version="1.0" encoding="{xml_encoding}"?>{dtd}'
           f'<package{attribute}>{children}</package>\n')
    nuspec = (b"\xff\xfe" + xml.encode("utf-16-le") if variant.startswith("utf16-le-") else
              b"\xfe\xff" + xml.encode("utf-16-be") if variant.startswith("utf16-be-") else xml.encode())
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", allowZip64=False) as package:
        if variant != "no-nuspec":
            location = "nested/Sample.LP01.nuspec" if variant == "subfolder-nuspec" else "Sample.LP01.nuspec"
            package.writestr(entry(location), nuspec)
        if variant == "multiple-nuspec":
            package.writestr(entry("Second.LP01.nuspec"), nuspec)
        if variant == "no-nuspec":
            package.writestr(entry("README.txt"), b"No root nuspec.\n")
        if variant == "duplicate-license-entry":
            package.writestr(entry("LICENSE.txt"), b"SPDX-License-Identifier: GPL-3.0-only\n")
            with warnings.catch_warnings():
                warnings.filterwarnings("ignore", message=r"Duplicate name: 'LICENSE.txt'", category=UserWarning)
                package.writestr(entry("LICENSE.txt"), b"SPDX-License-Identifier: MIT\n")
        elif variant == "alias-license-entry":
            package.writestr(entry("LICENSE.txt"), b"SPDX-License-Identifier: MIT\n")
            package.writestr(entry("license.txt"), b"SPDX-License-Identifier: GPL-3.0-only\n")
        elif file_text is not None:
            package.writestr(entry("LICENSE.txt"), file_text)
        if variant == "duplicate-readme-entry":
            package.writestr(entry("README.txt"), b"First entry.\n")
            with warnings.catch_warnings():
                warnings.filterwarnings("ignore", message=r"Duplicate name: 'README.txt'", category=UserWarning)
                package.writestr(entry("README.txt"), b"Second entry.\n")
    return stream.getvalue()


def expected_archive_entries(name, file_text):
    variant = name.removeprefix("owner-")
    if variant == "no-nuspec":
        return ["README.txt"]
    entries = ["nested/Sample.LP01.nuspec" if variant == "subfolder-nuspec" else
               "Sample.LP01.nuspec"]
    if variant == "multiple-nuspec":
        entries.append("Second.LP01.nuspec")
    if variant == "duplicate-license-entry":
        entries.extend(("LICENSE.txt", "LICENSE.txt"))
    elif variant == "alias-license-entry":
        entries.extend(("LICENSE.txt", "license.txt"))
    elif file_text is not None:
        entries.append("LICENSE.txt")
    if variant == "duplicate-readme-entry":
        entries.extend(("README.txt", "README.txt"))
    return entries


def verify_special_archive(package, name):
    variant = name.removeprefix("owner-")
    entries = package.infolist()
    if variant == "duplicate-license-entry":
        require([package.read(info) for info in entries[-2:]] == [
            b"SPDX-License-Identifier: GPL-3.0-only\n",
            b"SPDX-License-Identifier: MIT\n"
        ], f"Duplicate LICENSE entry order or declarations changed: {name}")
    elif variant == "alias-license-entry":
        require([package.read(info) for info in entries[-2:]] == [
            b"SPDX-License-Identifier: MIT\n",
            b"SPDX-License-Identifier: GPL-3.0-only\n"
        ], f"Case-aliased LICENSE entry declarations changed: {name}")
    elif variant == "duplicate-readme-entry":
        require([package.read(info) for info in entries[-2:]] ==
                [b"First entry.\n", b"Second entry.\n"],
                f"Duplicate non-license entry bytes changed: {name}")
    if variant.startswith(("utf16-le-", "utf16-be-")):
        bom = b"\xff\xfe" if variant.startswith("utf16-le-") else b"\xfe\xff"
        require(package.read(entries[0]).startswith(bom), f"UTF-16 nuspec BOM changed: {name}")
    if variant in ("dtd-entity", "dtd-unused") or variant.endswith(("-dtd-entity", "-dtd-unused")):
        try:
            checked_xml_root(package.read(entries[0]))
        except ProhibitedXmlDeclaration:
            pass
        else:
            raise ValueError(f"DTD was not rejected by the parser callback: {name}")


def declarations(text):
    """Only unprefixed SPDX-License-Identifier lines with ASCII indentation are recognized."""
    identifiers = []
    remaining = []
    ambiguous = False
    for line in re.split(r"\r\n|\r|\n", text):
        match = re.fullmatch(r"[ \t]*SPDX-License-Identifier:[ \t]*([A-Za-z0-9][A-Za-z0-9.+-]*)[ \t]*", line)
        if match:
            identifiers.append(match[1])
        elif "spdx-license-identifier" in line.lower():
            ambiguous = True
        else:
            remaining.append(line)
    return identifiers, remaining, ambiguous


def checked_xml_root(nuspec):
    parser = expat.ParserCreate()

    def reject_declaration(*_):
        raise ProhibitedXmlDeclaration("DTD and entity declarations are not package evidence.")

    parser.StartDoctypeDeclHandler = reject_declaration
    parser.EntityDeclHandler = reject_declaration
    parser.ExternalEntityRefHandler = reject_declaration
    parser.Parse(nuspec, True)
    return ET.fromstring(nuspec)


def classify_package(content, policy, licenses):
    """One package-reader branch for package-only and owner-bound expectation controls."""
    if content is None:
        return "missing-package", None, None
    if len(content) > 64 * 1024 * 1024:
        return "unsafe-package", None, None
    try:
        with zipfile.ZipFile(io.BytesIO(content)) as package:
            entries = package.infolist()
            if len(entries) > 4096:
                return "unsafe-package", None, None
            names = [info.filename for info in entries]
            if len(names) != len(set(name.casefold() for name in names)):
                return "unsafe-package", None, None
            nuspecs = [info for info in entries if "/" not in info.filename and
                       info.filename.lower().endswith(".nuspec")]
            if len(nuspecs) != 1 or nuspecs[0].file_size > 1024 * 1024:
                return "unsafe-package", None, None
            nuspec = package.read(nuspecs[0])
            root = checked_xml_root(nuspec)
            namespace = root.tag.removesuffix("package")
            if root.tag != namespace + "package" or namespace not in (
                "", f"{{{NS}}}",
                "{http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd}"
            ):
                return "unsafe-package", None, None
            metadata = [child for child in root if child.tag == namespace + "metadata"]
            if len(metadata) != 1 or any(
                child.tag.split("}")[-1] == "metadata" and child.tag != namespace + "metadata"
                for child in root
            ):
                return "unsafe-package", None, None
            metadata = metadata[0]
            license_nodes = [child for child in metadata if child.tag == namespace + "license"]
            if any(child.tag.split("}")[-1] == "license" and child.tag != namespace + "license"
                   for child in metadata) or len(license_nodes) > 1:
                return "unsafe-metadata", None, None
            if not license_nodes:
                return "missing-license", metadata, None
            license_node = license_nodes[0]
            attrs = license_node.attrib
            if len(license_node) or set(attrs) - {"type", "version"} or "type" not in attrs:
                return "unsafe-metadata", None, None
            if "version" in attrs:
                version = attrs["version"].split(".")
                if not 2 <= len(version) <= 4 or any(
                    re.fullmatch(r"[0-9]+", part) is None for part in version
                ) or tuple(map(int, version)) > (1, 0, 0, 0)[:len(version)]:
                    return "unsafe-metadata", None, None
            value = (license_node.text or "").strip()
            if attrs["type"].lower() == "file":
                if not safe_path(value) or value not in package.namelist():
                    return "file-unverified", metadata, None
                file_text = package.read(value).decode("utf-8")
                ids, remainder, ambiguous = declarations(file_text)
                return ("file-declared" if ids == ["MIT"] and not ambiguous and
                        not "".join(remainder).strip() else "file-unverified"), metadata, None
            if attrs["type"].lower() != "expression":
                return "unsafe-metadata", None, None
            try:
                state = expression_state(license_node.text or "", policy, licenses)
            except ExpressionLimitError:
                return "expression-limit", metadata, None
            except ValueError:
                return "invalid-expression", metadata, None
            if "LICENSE.txt" in package.namelist():
                file_text = package.read("LICENSE.txt").decode("utf-8")
                ids, _, ambiguous = declarations(file_text)
                if ambiguous or any(identifier not in set(leaf_ids(parse_expression(
                        license_node.text or "")[1])) for identifier in ids):
                    return "conflicting-declarations", metadata, None
            return state, metadata, license_node.text
    except (zipfile.BadZipFile, expat.ExpatError, ET.ParseError,
            ProhibitedXmlDeclaration, UnicodeDecodeError, RuntimeError):
        return "unsafe-package", None, None


def canonical_spdx(text):
    _, tree = parse_expression(text)
    tree = canonical_expression(tree)

    def render(node, parent=None):
        if node[0] == "ID":
            return node[1]
        if node[0] == "WITH":
            return node[1] + " WITH " + node[2]
        body = (" " + node[0] + " ").join(render(child, node[0]) for child in node[1:])
        return "(" + body + ")" if parent == "AND" and node[0] == "OR" else body
    return render(tree)


def safe_text(value):
    return (type(value) is str and 0 < len(value) <= 256 and
            unicodedata.normalize("NFC", value) == value and
            not any(ord(c) < 32 or 0x7f <= ord(c) <= 0x9f or 0xd800 <= ord(c) <= 0xdfff
                    for c in value))


def safe_path(value):
    return (safe_text(value) and re.fullmatch(r"[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)*", value) is not None and
            all(part not in (".", "..") for part in value.split("/")))


def grant_role(license_id):
    return "grant-" + license_id.lower()


def owner_record(package_digest, policy_digest, expression="MIT", grants=None):
    grants = GRANTS if grants is None else grants
    _, tree = parse_expression(expression)
    ids = sorted({term for term in leaf_ids(tree)})
    return dict(schema_version=1, caller_accepts_prevalidated_authority=True,
                owner_identity="Example Fixture Maintainer",
                owner_reference="urn:fixture:owner-lp01:2026",
                package_id="Sample.LP01", package_sha256=package_digest,
                repository_url=REPOSITORY, source_commit=COMMIT,
                release_ref=RELEASE, release_version="1.0.0",
                spdx_grant_expression=canonical_spdx(expression),
                grant_bindings=[dict(license_id=license_id, role=grant_role(license_id),
                                     path=grants[license_id][0], sha256=sha(grants[license_id][1]),
                                     caller_accepts_complete_grant=True) for license_id in ids],
                prevalidation_references=[],
                policy_version="1.0.0", policy_sha256=policy_digest,
                spdx_version="3.27.0", spdx_sha256=CATALOG_SHA)


def leaf_ids(tree):
    if tree[0] in ("ID", "WITH"):
        return [tree[1]]
    return [item for child in tree[1:] for item in leaf_ids(child)]


def padded_record(record, target):
    record["prevalidation_references"] = []
    while target - len(encoded(record)) > 250:
        record["prevalidation_references"].append("urn:fixture:" + "x" * 220)
        require(len(record["prevalidation_references"]) < 32, "Too many synthetic references.")
    remaining = target - len(encoded(record))
    if remaining < 22:
        record["prevalidation_references"][-1] += "x" * remaining
        require(len(encoded(record)) == target, "Record padding changed.")
        return record
    for count in range(245):
        record["prevalidation_references"].append("urn:fixture:" + "x" * count)
        if len(encoded(record)) == target:
            return record
        record["prevalidation_references"].pop()
    raise ValueError(f"Cannot construct canonical owner record of {target} bytes.")


def owner_inputs(name, policy_bytes, catalog_bytes):
    multi = name in ("accepted-multi", "accepted-multi-or", "equivalent-reordered", "equivalent-flattened",
                     "missing-bsd", "missing-bsd-or", "missing-bsd-binding", "conflicting-term",
                     "non-equivalent-operator", "non-equivalent-group")
    expression = ("MIT AND (BSD-2-Clause AND MIT)" if name == "equivalent-flattened" else
                  "MIT AND (BSD-2-Clause OR MIT)" if name == "non-equivalent-group" else
                  "MIT OR BSD-2-Clause" if name in ("accepted-multi-or", "missing-bsd-or") else
                  "MIT AND BSD-2-Clause" if multi else "MIT AND MIT" if name == "accepted-and" else
                  "GPL-3.0-only" if name == "package-gpl" else
                  "MIT AND" if name == "invalid-expression" else
                  "MIT" + " " * (MAX_EXPRESSION_BYTES - 2) if name == "over-limit-expression" else
                  " MIT " if name == "equivalent-whitespace" else "MIT")
    metadata = {
        "duplicate-license": LICENSE.format("MIT") + LICENSE.format("GPL-3.0-only"),
        "type-url": '<license type="url">MIT</license>',
        "type-file": '<license type="file">LICENSE.txt</license>',
        "unsupported-version": '<license type="expression" version="2.0.0">MIT</license>',
        "unknown-license-attribute": '<license type="expression" custom="yes">MIT</license>',
        "dtd-entity": '<license type="expression">&license;</license>',
        "utf16-le-dtd-entity": '<license type="expression">&license;</license>',
        "utf16-be-dtd-entity": '<license type="expression">&license;</license>',
    }.get(name, LICENSE.format(expression))
    file_text = (b"SPDX-License-Identifier: GPL-3.0-only\n" if name == "expression-file-conflict" else
                 b"SPDX-License-Identifier: MIT\n" if name == "type-file" else None)
    content = archive("owner-" + name, metadata, file_text)
    grant_bytes = dict(GRANTS)
    mit = GRANT
    tag = b"SPDX-License-Identifier: MIT"
    if name == "tag-only":
        mit = tag + b"\n"
    elif name == "tag-no-space":
        mit = b"SPDX-License-Identifier:MIT\r\n"
    elif name == "tag-tab-space":
        mit = b"SPDX-License-Identifier:\t MIT\r"
    elif name == "tag-repeated":
        mit = tag + b"\r\n" + tag + b"\r\n"
    elif name == "tag-indented":
        mit = b"\t " + tag + b"\r"
    elif name == "tag-comment-prefix":
        mit = GRANT.replace(tag, b"# " + tag)
    elif name in ("tag-crlf-gpl", "tag-cr-gpl", "tag-repeated-conflict"):
        mit = GRANT.replace(tag, b"SPDX-License-Identifier: GPL-3.0-only", 1)
        if name == "tag-repeated-conflict":
            mit = tag + b"\n" + mit
        else:
            mit = mit.replace(b"\n", b"\r\n" if name == "tag-crlf-gpl" else b"\r")
    elif name == "grant-gpl":
        mit = GRANT.replace(tag, b"SPDX-License-Identifier: GPL-3.0-only")
    elif name == "grant-gpl-indented":
        mit = GRANT.replace(tag, b"  SPDX-License-Identifier: GPL-3.0-only")
    elif name == "grant-at-limit":
        notice = b"Fixture copyright notice.\n"
        count, remainder = divmod(MAX_GRANT_BYTES - len(GRANT), len(notice))
        mit = GRANT + notice * count + b"X" * remainder
    elif name == "oversize-grant":
        mit = GRANT + b"x" * (MAX_GRANT_BYTES + 1 - len(GRANT))
    elif name == "empty-grant":
        mit = b""
    elif name == "invalid-utf8-grant":
        mit = b"\xff" + GRANT
    elif name == "copyright-variant":
        mit = GRANT.replace(b"2026 Example", b"2027 Example")
    elif name == "untagged-grant":
        mit = GRANT.replace(tag + b"\n", b"")
    if mit != GRANT:
        grant_bytes["MIT"] = (f"grants/{name}.txt", mit)
    if name == "conflicting-term":
        grant_bytes["BSD-2-Clause"] = ("grants/conflicting-term.txt",
                                       BSD_GRANT.replace(b"BSD-2-Clause", b"MIT", 1))
    owner_expression = ("BSD-2-Clause AND MIT" if name == "equivalent-reordered" else
                        "MIT OR BSD-2-Clause" if name in ("non-equivalent-operator", "non-equivalent-group") else
                        "MIT" if name in ("package-gpl", "wrong-expression", "invalid-expression",
                                         "over-limit-expression") else expression)
    record = owner_record(sha(content), sha(policy_bytes), owner_expression, grant_bytes)
    if name == "wrong-expression":
        record["spdx_grant_expression"] = "Apache-2.0"
    changes = {
        "acceptance-false": ("caller_accepts_prevalidated_authority", False),
        "wrong-package": ("package_sha256", "0" * 64),
        "wrong-package-id": ("package_id", "Other.Package"),
        "wrong-repository": ("repository_url", "https://example.test/other"),
        "wrong-commit": ("source_commit", "b" * 40),
        "different-release-assertion": ("release_ref", "refs/tags/release-1.0.0"),
        "wrong-version": ("release_version", "2.0.0"),
        "wrong-policy-version": ("policy_version", "2.0.0"),
        "wrong-policy-digest": ("policy_sha256", "0" * 64),
        "wrong-spdx-version": ("spdx_version", "0.0.0"),
        "wrong-spdx-digest": ("spdx_sha256", "0" * 64),
        "owner-reference-missing": ("owner_reference", ""),
        "nul-field": ("owner_identity", "Fixture\u0000Owner"),
        "surrogate-field": ("owner_identity", "Fixture\ud800Owner"),
        "non-nfc-field": ("owner_identity", "Cafe\u0301"),
        "unknown-field": ("unrecognized", "unsafe"),
        "schema-version": ("schema_version", 2),
        "schema-boolean": ("schema_version", True),
    }
    if name in changes:
        field, value = changes[name]
        record[field] = value
    if name == "wrong-grant-digest":
        record["grant_bindings"][0]["sha256"] = "0" * 64
    if name == "missing-bsd-binding":
        record["grant_bindings"] = record["grant_bindings"][1:]
    if name == "wrong-grant-path":
        record["grant_bindings"][0]["path"] = "grants/other.txt"
    if name == "wrong-grant-role":
        record["grant_bindings"][0]["role"] = "grant-other"
    if name == "duplicate-grant-binding":
        record["grant_bindings"].append(record["grant_bindings"][0].copy())
    if name in ("record-traversal", "record-absolute-path", "record-separator-alias", "record-dot-segment"):
        record["grant_bindings"][0]["path"] = {
            "record-traversal": "grants/../secret.txt", "record-absolute-path": "C:/grants/mit-full.txt",
            "record-separator-alias": r"grants\mit-full.txt", "record-dot-segment": "grants/./mit-full.txt",
        }[name]
    if name == "grant-completeness-false":
        record["grant_bindings"][0]["caller_accepts_complete_grant"] = False
    if name == "acceptance-absent":
        del record["caller_accepts_prevalidated_authority"]
    if name in ("record-at-limit", "record-over-limit"):
        padded_record(record, 8192 if name == "record-at-limit" else 8193)
    record_bytes = encoded(record)
    if name == "reversed-fields":
        record_bytes = encoded(dict(reversed(list(record.items()))))
    if name == "duplicate-json-key":
        record_bytes = record_bytes.replace(b'"schema_version": 1,', b'"schema_version": 1,\n  "schema_version": 1,', 1)
    if name == "malformed-record":
        record_bytes = b"{broken json\n"
    if name == "oversize-record":
        record_bytes = encoded({**record, "owner_identity": "A" * 8192})
    second = encoded({**record, "owner_identity": "Conflicting Fixture Owner"}) if name == "conflicting-records" else None
    inputs = [dict(role="package", path=f"packages/owner-{name}.nupkg", sha256=sha(content)),
              dict(role="policy", path="policy/" + POLICY.name, sha256=sha(policy_bytes)),
              dict(role="corpus", path="policy/" + CATALOG.name, sha256=sha(catalog_bytes))]
    if name != "no-record":
        inputs.append(dict(role="owner-record", path=f"owners/{name}.json", sha256=sha(record_bytes)))
    if second is not None:
        inputs.append(dict(role="owner-record-conflict", path="owners/conflicting-records-secondary.json",
                           sha256=sha(second)))
    grants = {}
    for license_id in sorted(set(leaf_ids(parse_expression(owner_expression)[1])) & set(grant_bytes)):
        role = grant_role(license_id)
        if name in ("no-grant", "no-record") or name in ("missing-bsd", "missing-bsd-or") and license_id == "BSD-2-Clause":
            continue
        path, data = grant_bytes[license_id]
        inputs.append(dict(role=role, path=path, sha256=sha(data)))
        grants[role] = data
    if name in ("unknown-role", "extra-role"):
        inputs.append(dict(role="unknown-input" if name == "unknown-role" else "grant-bsd-2-clause",
                           path="grants/bsd-full.txt", sha256=sha(BSD_GRANT)))
        if name == "extra-role":
            grants["grant-bsd-2-clause"] = BSD_GRANT
    if name == "role-path-mismatch":
        record["grant_bindings"][0]["path"] = "grants/other.txt"
        record_bytes = encoded(record)
        inputs[3]["sha256"] = sha(record_bytes)
    return inputs, content, record_bytes, second, grants


def owner_state(inputs, content, record_bytes, second, grants, policy_bytes, policy, licenses):
    roles = [item["role"] for item in inputs]
    if len(roles) != len(set(roles)) or not {"package", "policy", "corpus"} <= set(roles) or any(
        role not in {"package", "policy", "corpus", "owner-record", "owner-record-conflict"} and
        re.fullmatch(r"grant-[a-z0-9][a-z0-9.+-]*", role) is None for role in roles
    ) or any(not safe_path(item["path"]) for item in inputs):
        return "owner-record-invalid"
    checked = {item["role"]: item for item in inputs}
    require(sha(content) == checked["package"]["sha256"] and
            sha(policy_bytes) == checked["policy"]["sha256"] and
            checked["corpus"]["sha256"] == CATALOG_SHA and
            ("owner-record" not in checked or sha(record_bytes) == checked["owner-record"]["sha256"]) and
            (second is None or sha(second) == checked["owner-record-conflict"]["sha256"]),
            "Owner fixture checked input bytes differ from pinned roles.")
    package_branch, metadata, expression = classify_package(content, policy, licenses)
    if package_branch != "approved-non-copyleft":
        return package_branch
    if "owner-record" not in checked:
        return "owner-record-missing"
    if "owner-record-conflict" in checked:
        return "owner-record-conflict"
    if len(record_bytes) > 8192 or record_bytes.startswith(b"\xef\xbb\xbf"):
        return "owner-record-invalid"
    try:
        record = json.loads(record_bytes.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError):
        return "owner-record-invalid"
    if type(record) is not dict or set(record) != OWNER_FIELDS or type(record.get("grant_bindings")) is not list:
        return "owner-record-invalid"
    scalars = OWNER_FIELDS - {"schema_version", "caller_accepts_prevalidated_authority",
                              "grant_bindings", "prevalidation_references"}
    if type(record["schema_version"]) is not int or record["schema_version"] != 1 or any(
        not safe_text(record[key]) for key in scalars
    ) or type(record["caller_accepts_prevalidated_authority"]) is not bool:
        return "owner-record-invalid"
    if type(record["prevalidation_references"]) is not list or len(record["prevalidation_references"]) > 32 or any(
            not safe_text(ref) for ref in record["prevalidation_references"]):
        return "owner-record-invalid"
    if re.fullmatch(r"(?:[a-f0-9]{40}|[a-f0-9]{64})", record["source_commit"]) is None or not record[
            "release_ref"].startswith("refs/tags/") or len(record["release_ref"]) <= len("refs/tags/"):
        return "owner-record-invalid"
    bindings = record["grant_bindings"]
    if type(bindings) is not list or not 1 <= len(bindings) <= 32 or any(
        type(binding) is not dict or set(binding) != {
            "license_id", "role", "path", "sha256", "caller_accepts_complete_grant"} or
        not all(safe_text(binding[key]) for key in ("license_id", "role", "sha256")) or
        not safe_path(binding["path"]) or type(binding["caller_accepts_complete_grant"]) is not bool
        for binding in bindings
    ):
        return "owner-record-invalid"
    if canonical_owner_bytes(record) != record_bytes or any(
            re.fullmatch(r"[a-f0-9]{64}", record[key]) is None
            for key in ("package_sha256", "policy_sha256", "spdx_sha256")) or any(
            re.fullmatch(r"[a-f0-9]{64}", binding["sha256"]) is None for binding in bindings):
        return "owner-record-invalid"
    if not record["caller_accepts_prevalidated_authority"]:
        return "owner-acceptance-missing"
    namespace = metadata.tag.removesuffix("metadata")
    repositories = metadata.findall(namespace + "repository")
    if len(repositories) != 1:
        return "owner-binding-conflict"
    repository = repositories[0]
    try:
        owner_normalized = canonical_spdx(record["spdx_grant_expression"])
        package_normalized = canonical_spdx(expression)
    except (ExpressionLimitError, ValueError):
        return "owner-binding-conflict"
    if (record["package_sha256"] != sha(content) or
        record["package_id"] != metadata.findtext(namespace + "id") or
        record["repository_url"] != repository.get("url") or
        record["source_commit"] != repository.get("commit") or
        record["release_version"] != metadata.findtext(namespace + "version") or
        record["spdx_grant_expression"] != owner_normalized or
        owner_normalized != package_normalized or
        record["policy_version"] != policy["policy_version"] or
        record["policy_sha256"] != sha(policy_bytes) or
        record["spdx_version"] != policy["spdx_version"] or
        record["spdx_sha256"] != CATALOG_SHA):
        return "owner-binding-conflict"
    expected_ids = sorted(set(leaf_ids(parse_expression(expression)[1])))
    if [binding["license_id"] for binding in bindings] != expected_ids:
        return "grant-missing" if set(binding["license_id"] for binding in bindings) < set(expected_ids) else "grant-binding-conflict"
    expected_roles = {grant_role(term) for term in expected_ids}
    if (set(checked) - {"package", "policy", "corpus", "owner-record"}) - expected_roles:
        return "owner-record-invalid"
    for binding in bindings:
        license_id = binding["license_id"]
        role = grant_role(license_id)
        if binding["role"] != role:
            return "grant-binding-conflict"
        if role not in checked:
            return "grant-missing"
        if checked[role]["path"] != binding["path"] or checked[role]["sha256"] != binding["sha256"]:
            return "grant-binding-conflict"
        data = grants[role]
        if sha(data) != binding["sha256"]:
            return "grant-binding-conflict"
        if len(data) > MAX_GRANT_BYTES:
            return "grant-limit"
        try:
            text = data.decode("utf-8")
        except UnicodeDecodeError:
            return "grant-incomplete"
        if not text.strip() or "\0" in text or data.startswith(b"\xef\xbb\xbf"):
            return "grant-incomplete"
        ids, remaining, ambiguous = declarations(text)
        if ambiguous:
            return "grant-incomplete"
        if any(declaration != license_id for declaration in ids):
            return "grant-contradiction"
        if not "".join(remaining).strip():
            return "grant-incomplete"
        if not binding["caller_accepts_complete_grant"]:
            return "grant-incomplete"
    return "owner-evidence-accepted"


def policy_variant(name, policy_bytes, catalog_bytes):
    if name == "policy-version-mismatch":
        value = json.loads(policy_bytes)
        value["spdx_version"] = "0.0.0"
        return encoded(value), catalog_bytes
    if name == "policy-overlap":
        value = json.loads(policy_bytes)
        value["copyleft"].append("MIT")
        return encoded(value), catalog_bytes
    if name == "catalog-version-mismatch":
        value = {"licenseListVersion": "0.0.0", "licenses": []}
        return policy_bytes, encoded(value)
    return policy_bytes, catalog_bytes


def disposition(branch):
    if branch == "owner-evidence-accepted":
        return "deterministic", (
            "The checked package SPDX expression is OSI-approved and non-copyleft under pinned "
            "policy; every offered license ID has a checked grant binding matching the prevalidated "
            "owner/repository record. Caller-asserted authority, grant sufficiency and release scope "
            "were accepted at the input boundary, NOT cryptographically established or legally "
            "interpreted by this resolver."
        ), None, None, None
    if branch == "approved-non-copyleft":
        return "escalated", None, (
            "An OSI-approved, non-copyleft package declaration does not establish an owner-authorized "
            "repository license grant or correspondence to the checked release/source scope."
        ), "owner-license-grant-and-release-scope", "package-owner"
    if branch == "file-declared":
        return "escalated", None, (
            "The packaged SPDX-License-Identifier classifies MIT but is not license grant text; "
            "owner authorization and repository/release scope are also missing."
        ), "complete-owner-license-grant-and-release-scope", "package-owner"
    if branch in ("copyleft", "weak-copyleft", "non-osi"):
        return "deterministic", (
            f"The authoritative checked package declaration offers no OSI-approved non-copyleft "
            f"alternative ({branch}); this is a package-scoped negative, not a repository-wide verdict."
        ), None, None, None
    explanations = {
        "owner-record-missing": ("No prevalidated owner/repository evidence record was supplied.", "prevalidated-owner-record", "package-owner"),
        "owner-acceptance-missing": ("Caller did not explicitly accept the owner-authority prevalidation trust boundary.", "explicit-caller-authority-acceptance", "input-owner"),
        "owner-record-invalid": ("The owner record is malformed, noncanonical, over 8192 bytes or has unknown/missing schema fields.", "canonical-versioned-owner-record", "input-owner"),
        "owner-record-conflict": ("Multiple owner records assert conflicting identities for the checked package.", "single-unambiguous-owner-record", "input-owner"),
        "owner-binding-conflict": ("Owner-record package, repository, source/release, expression or pinned policy identity conflicts with checked inputs.", "matching-owner-package-and-policy-bindings", "package-owner"),
        "grant-binding-conflict": ("Owner-record grant path or digest conflicts with the independently checked grant input.", "matching-checked-grant-artifact", "package-owner"),
        "grant-missing": ("The checked complete grant artifact was not supplied.", "checked-complete-license-grant", "package-owner"),
        "grant-incomplete": ("The checked license artifact is tag-only or the caller has not prevalidated its complete grant text.", "prevalidated-complete-license-grant-text", "package-owner"),
        "grant-limit": ("The checked license grant exceeds the 16384-byte v1 inspection limit.", "bounded-complete-license-grant", "package-owner"),
        "grant-contradiction": ("The authoritative package declaration and prevalidated grant evidence contradict each other.", "reconciled-owner-license-grant", "package-owner"),
        "mixed-alternatives": ("An OR alternative is not established as OSI-approved and non-copyleft.", "binding-license-choice", "package-owner"),
        "unclassified-osi": ("SPDX marks the identifier OSI-approved but this policy does not classify its copyleft obligations.", "reviewed-copyleft-classification", "policy-owner"),
        "custom-license": ("A custom LicenseRef has no pinned OSI/copy-left classification.", "reviewed-custom-license", "policy-owner"),
        "exception-unreviewed": ("WITH exception semantics have no pinned policy classification.", "reviewed-exception-policy", "policy-owner"),
        "unknown-license": ("The identifier is not an exact SPDX 3.27.0 license ID.", "corrected-spdx-expression", "package-owner"),
        "deprecated-license": ("Deprecated SPDX spelling cannot be silently normalized.", "current-spdx-expression-and-owner-confirmation", "package-owner"),
        "invalid-expression": ("The expression is not valid under the frozen SPDX expression grammar.", "corrected-spdx-expression", "package-owner"),
        "file-unverified": ("A license file declaration alone does not establish a complete, authoritative package grant or repository/release scope.", "owner-verified-license-grant-and-release-scope", "package-owner"),
        "expression-limit": ("The SPDX expression exceeds the pinned parsing resource limits and cannot be classified safely.", "bounded-owner-confirmed-license-expression", "package-owner"),
        "conflicting-declarations": ("The nuspec expression and packaged explicit SPDX identifier conflict.", "reconciled-license-grant", "package-owner"),
        "missing-license": ("No authoritative license expression or supported file declaration was supplied.", "license-grant-and-owner-confirmation", "package-owner"),
        "unsafe-metadata": ("License metadata is malformed, unsupported or ambiguous.", "corrected-nuspec", "package-owner"),
        "unsafe-package": ("Package or nuspec cannot be safely and unambiguously inspected.", "valid-unambiguous-package", "package-owner"),
        "missing-package": ("Checked package bytes were not supplied.", "package", "input-owner"),
        "missing-policy": ("Pinned LP-01 policy is missing.", "pinned-license-policy", "policy-owner"),
        "missing-corpus": ("Pinned SPDX license catalog is missing.", "pinned-spdx-catalog", "policy-owner"),
        "invalid-policy": ("Pinned LP-01 policy and SPDX catalog are inconsistent.", "consistent-pinned-license-policy", "policy-owner"),
    }
    missing, required, owner = explanations[branch]
    return "escalated", None, missing, required, owner


def result(branch, inputs, reference, contract=None):
    decision, finding, missing, required, owner = disposition(branch)
    if contract is None and branch == "approved-non-copyleft":
        contract = ("Checked package SPDX expression: all alternatives OSI-approved by SPDX 3.27.0 "
                    "and non-copyleft under pinned policy 1.0.0; owner grant and release/source scope not established.")
    if contract is None and branch == "file-declared":
        contract = ("Checked LICENSE.txt contains only SPDX-License-Identifier: MIT; MIT is OSI-approved "
                    "and allowlisted non-copyleft, but the file supplies no actual license grant text.")
    if branch == "owner-evidence-accepted":
        contract = ("Checked package/nuspec, SPDX 3.27.0, policy 1.0.0, canonical prevalidated "
                    "owner record and one separately checked, caller-accepted complete grant per "
                    "distinct normalized SPDX license ID bind exactly; owner authority, release "
                    "correspondence and legal grant sufficiency are trusted at the input boundary, "
                    "not cryptographically proven or legally interpreted by resolver.")
    elif branch.startswith(("owner-", "grant-")):
        contract = ("Available checked package, policy, owner-record and grant inputs were inspected; "
                    "missing or conflicting evidence cannot establish owner authority or license scope.")
    unavailable = ("unsafe-package", "missing-package", "missing-policy", "missing-corpus", "invalid-policy")
    unknown = branch in unavailable
    citations = [] if branch in unavailable else [
        "policy:lp01-policy-1.0.0", "corpus:spdx-licenses-3.27.0"]
    if reference and branch not in unavailable:
        citations.insert(0, reference)
    return dict(row_id="LP-01", resolver_id="lp01-license-policy", resolver_version="1.0.0",
                branch_id=branch, decision=decision,
                applicability="unknown" if unknown else "applicable", inputs=inputs,
                evidence_references=citations,
                evidence_contract=contract, finding=finding, missing_fact=missing,
                required_input=required, next_category=owner)


def suite_data(policy_bytes, catalog_bytes, policy, licenses):
    cases = []
    for name, metadata, branch, file_text in SPECS:
        content = archive(name, metadata, file_text)
        pbytes, cbytes = policy_variant(name, policy_bytes, catalog_bytes)
        inputs = []
        if content is not None:
            inputs.append(dict(role="package", path=f"packages/{name}.nupkg", sha256=sha(content)))
        if name != "no-policy":
            inputs.append(dict(role="policy", path="policy/" + (
                f"{name}.json" if pbytes != policy_bytes else POLICY.name), sha256=sha(pbytes)))
        if name != "no-corpus":
            inputs.append(dict(role="corpus", path="policy/" + (
                f"{name}.json" if cbytes != catalog_bytes else CATALOG.name), sha256=sha(cbytes)))
        actual = ("missing-policy" if name == "no-policy" else
                  "missing-corpus" if name == "no-corpus" else
                  "invalid-policy" if name in (
                      "policy-version-mismatch", "policy-overlap", "catalog-version-mismatch") else
                  classify_package(content, policy, licenses)[0])
        require(actual == branch, f"Shared package reader disagrees with frozen {name}: {actual} != {branch}")
        reference = ("package:LICENSE.txt" if branch == "file-declared" else
                     "package:nuspec/license" if "<license " in metadata or
                     metadata.startswith('<license xmlns:hint=') or "<x:license " in metadata else
                     "package:nuspec/metadata" if content is not None else None)
        if branch == "conflicting-declarations":
            reference = "package:LICENSE.txt"
        pointers = (["package:archive"] if content is not None else [])
        if name != "no-policy":
            pointers.append("policy:lp01-policy-1.0.0")
        if name != "no-corpus":
            pointers.append("corpus:spdx-licenses-3.27.0")
        if reference and reference not in pointers:
            pointers.append(reference)
        expected = result(branch, inputs, reference)
        if branch in ("file-declared", "conflicting-declarations"):
            expected["evidence_references"].insert(0, "package:nuspec/license")
            pointers.append("package:nuspec/license")
        if name == "expression-file-match":
            expected["evidence_references"].append("package:LICENSE.txt")
            pointers.append("package:LICENSE.txt")
        case = dict(case_id="lp01-" + name, row_id="LP-01", inputs=inputs,
                    expected=expected,
                    rationale=f"Frozen LP-01 {name} policy/metadata branch; package-only license evidence, not release-wide compliance.",
                    evidence_pointers=pointers)
        cases.append(case)
    for name, branch in OWNER_SPECS:
        inputs, content, record_bytes, second, grants = owner_inputs(name, policy_bytes, catalog_bytes)
        actual = owner_state(inputs, content, record_bytes, second, grants, policy_bytes, policy, licenses)
        require(actual == branch, f"Owner evidence disagrees with frozen {name}: {actual} != {branch}")
        if branch == "owner-evidence-accepted" or branch.startswith(("owner-", "grant-")):
            references = ["package:nuspec/license", "package:nuspec/repository",
                          "policy:lp01-policy-1.0.0", "corpus:spdx-licenses-3.27.0"]
            if name != "no-record":
                references.append("owner-record:document")
            references.extend(item["role"] + ":document" for item in inputs if item["role"].startswith("grant-"))
            expected = result(branch, inputs, "package:nuspec/license")
            expected["evidence_references"] = references
        else:
            reference = (None if branch == "unsafe-package" else
                         "package:LICENSE.txt" if branch in ("file-declared", "conflicting-declarations") else
                         "package:nuspec/license")
            expected = result(branch, inputs, reference)
            references = expected["evidence_references"]
            if branch in ("file-declared", "conflicting-declarations"):
                references.insert(0, "package:nuspec/license")
        cases.append(dict(case_id="lp01-owner-" + name, row_id="LP-01", inputs=inputs,
                          expected=expected,
                          rationale="Shared package classification precedes owner evidence; only an eligible declaration can use caller-accepted owner and grant bindings.",
                          evidence_pointers=references + ["package:archive"]))
    return dict(schema_version=1, cases=cases)


def retained_data(policy_bytes, catalog_bytes, policy, licenses):
    require(expression_state("MIT", policy, licenses) == "approved-non-copyleft",
            "MIT must independently classify as OSI-approved non-copyleft.")
    cases = []
    for vendor, filename, digest, _, _, _ in VENDORS:
        inputs = [
            dict(role="package", path="packages/" + filename, sha256=digest),
            dict(role="policy", path="policy/" + POLICY.name, sha256=sha(policy_bytes)),
            dict(role="corpus", path="policy/" + CATALOG.name, sha256=sha(catalog_bytes)),
        ]
        cases.append(dict(case_id="lp01-retained-" + vendor, row_id="LP-01", inputs=inputs,
                          expected=result("approved-non-copyleft", inputs, "package:nuspec/license",
                                          "SPDX expression MIT; SPDX license-list 3.27.0 OSI=true; LP-01 policy 1.0.0 non_copyleft_allowlist contains MIT."),
                          rationale="Exact retained package declares MIT; independently classified using pinned policy, without inferring repository-wide or owner licensing from package metadata.",
                          evidence_pointers=["package:nuspec/license", "package:archive",
                                             "policy:lp01-policy-1.0.0", "corpus:spdx-licenses-3.27.0"]))
    return dict(schema_version=1, cases=cases)


def rejected_input_data(policy_bytes, catalog_bytes, policy, licenses):
    base, content, record_bytes, second, grants = owner_inputs("accepted", policy_bytes, catalog_bytes)
    controls = (
        ("duplicate-owner-role", "duplicate-role", dict(base[3])),
        ("duplicate-grant-role", "duplicate-role", dict(base[4])),
        ("parent-traversal", "invalid-relative-path", {**base[3], "path": "../owners/accepted.json"}),
        ("absolute-path", "invalid-relative-path", {**base[3], "path": "C:/owners/accepted.json"}),
        ("separator-alias", "invalid-relative-path", {**base[3], "path": r"owners\accepted.json"}),
        ("dot-segment", "invalid-relative-path", {**base[3], "path": "owners/./accepted.json"}),
    )
    cases = []
    for name, reason, changed in controls:
        inputs = [dict(item) for item in base]
        if reason == "duplicate-role":
            inputs.append(changed)
        else:
            inputs[3] = changed
        require((len({item["role"] for item in inputs}) != len(inputs)) ==
                (reason == "duplicate-role") and
                (all(safe_path(item["path"]) for item in inputs)) ==
                (reason != "invalid-relative-path") and
                owner_state(inputs, content, record_bytes, second, grants, policy_bytes, policy, licenses) ==
                "owner-record-invalid",
                f"Input role/path rejection is not prior to role dictionary conversion: {name}")
        cases.append(dict(case_id="lp01-reject-" + name, stage="shared-input-loader",
                          expected_rejection=reason, inputs=inputs))
    return dict(schema_version=1, controls=cases)


def check_negative_bindings():
    bindings = ["LP01_" + vendor.upper() + "_NUPKG" for vendor, *_ in VENDORS]
    for missing in bindings:
        for typo in (False, True):
            env = os.environ.copy()
            for binding in bindings:
                env[binding] = "unreadable-placeholder"
            env.pop(missing)
            if typo:
                env[missing + "_TYPO"] = "unreadable-placeholder"
            run = subprocess.run([sys.executable, *([] if __debug__ else ["-O"]),
                                  str(Path(__file__)), "--require-retained"],
                                 env=env, capture_output=True, text=True, check=False)
            require(run.returncode != 0 and
                    f"Required retained package bindings missing: {missing}" in run.stderr,
                    f"Negative retained binding check did not fail before archive access: {missing}")
    print("Both missing and misspelled retained bindings rejected.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write-fixtures", action="store_true")
    parser.add_argument("--require-retained", action="store_true")
    parser.add_argument("--check-negative-bindings", action="store_true")
    args = parser.parse_args()
    if args.check_negative_bindings:
        check_negative_bindings()
        return
    policy_bytes, catalog_bytes, policy, licenses = policy_data()
    synthetic = suite_data(policy_bytes, catalog_bytes, policy, licenses)
    acceptance = retained_data(policy_bytes, catalog_bytes, policy, licenses)
    rejected = rejected_input_data(policy_bytes, catalog_bytes, policy, licenses)
    retained = json.loads((ROOT / "retained-inputs.json").read_bytes())
    receipt = json.loads((ROOT / "acceptance-receipt.template.json").read_bytes())
    require(retained["schema_version"] == receipt["schema_version"] == 1 and
            retained["row_id"] == receipt["row_id"] == "LP-01" and
            retained["policy_version"] == policy["policy_version"] and
            retained["spdx_version"] == policy["spdx_version"] and
            retained["spdx_sha256"] == CATALOG_SHA and
            receipt["expectation_commit"] is None and
            receipt["suite"] == "acceptance.json" and
            receipt["resolver_id"] == "lp01-license-policy" and
            receipt["resolver_version"] == "1.0.0" and
            len(retained["vendors"]) == len(receipt["runs"]) == len(acceptance["cases"]) == len(VENDORS),
            "LP-01 retained manifest or receipt differs from frozen policy.")
    for (vendor, filename, digest, size, nuspec_name, nuspec_digest), identity, case, run in zip(
            VENDORS, retained["vendors"], acceptance["cases"], receipt["runs"]):
        require(identity["vendor"] == vendor and identity["binding"] == run["binding"] ==
                "LP01_" + vendor.upper() + "_NUPKG" and
                identity["source_relative_path"].replace("\\", "/").endswith(filename) and
                identity["size_bytes"] == size and identity["sha256"] == digest and
                identity["nuspec_entry"] == nuspec_name and
                identity["nuspec_sha256"] == nuspec_digest and
                run["case_id"] == case["case_id"] == "lp01-retained-" + vendor and
                case["inputs"][0]["sha256"] == digest and
                case["expected"]["branch_id"] == "approved-non-copyleft" and
                all(run[key] is None for key in ("archive_nuspec_and_notice_sha256_verified",
                    "policy_and_catalog_sha256_verified", "replay_exit_code",
                    "result_matches_frozen_expected", "replay_output_sha256")),
                f"LP-01 retained identity or Phase A receipt mismatch: {vendor}")
    if args.write_fixtures:
        (ROOT / "packages").mkdir(parents=True, exist_ok=True)
        (ROOT / "owners").mkdir(parents=True, exist_ok=True)
        (ROOT / "grants").mkdir(parents=True, exist_ok=True)
        for name, metadata, _, file_text in SPECS:
            content = archive(name, metadata, file_text)
            if content is not None:
                (ROOT / "packages" / f"{name}.nupkg").write_bytes(content)
            pbytes, cbytes = policy_variant(name, policy_bytes, catalog_bytes)
            if pbytes != policy_bytes:
                (ROOT / "policy" / f"{name}.json").write_bytes(pbytes)
            if cbytes != catalog_bytes:
                (ROOT / "policy" / f"{name}.json").write_bytes(cbytes)
        for name, _ in OWNER_SPECS:
            inputs, content, record_bytes, second, grants = owner_inputs(name, policy_bytes, catalog_bytes)
            (ROOT / inputs[0]["path"]).write_bytes(content)
            for item in inputs:
                if item["role"] == "owner-record":
                    (ROOT / item["path"]).write_bytes(record_bytes)
                elif item["role"] == "owner-record-conflict":
                    (ROOT / item["path"]).write_bytes(second)
                elif item["role"].startswith("grant-"):
                    (ROOT / item["path"]).write_bytes(grants[item["role"]])
        (ROOT / "cases.json").write_bytes(encoded(synthetic))
        (ROOT / "acceptance.json").write_bytes(encoded(acceptance))
        (ROOT / "rejected-inputs.json").write_bytes(encoded(rejected))
    else:
        require((ROOT / "cases.json").read_bytes() == encoded(synthetic), "LP-01 synthetic expectations changed.")
        require((ROOT / "acceptance.json").read_bytes() == encoded(acceptance), "LP-01 retained expectations changed.")
        require((ROOT / "rejected-inputs.json").read_bytes() == encoded(rejected), "LP-01 input rejections changed.")
    for name, metadata, _, file_text in SPECS:
        content = archive(name, metadata, file_text)
        if content is not None:
            path = ROOT / "packages" / f"{name}.nupkg"
            if not args.write_fixtures:
                require(path.read_bytes() == content, f"LP-01 archive changed: {name}")
            if name != "corrupt-archive":
                with zipfile.ZipFile(io.BytesIO(content)) as package:
                    expected_entries = expected_archive_entries(name, file_text)
                    require(package.namelist() == expected_entries, f"LP-01 ZIP entry order differs: {name}")
                    verify_special_archive(package, name)
                    for entry_info in package.infolist():
                        require(entry_info.create_system == 3 and entry_info.date_time == ZIP_DATE and
                                entry_info.compress_type == zipfile.ZIP_STORED and
                                entry_info.external_attr == 0o100644 << 16 and
                                entry_info.extra == b"" and entry_info.comment == b"",
                                f"Nonportable LP-01 ZIP entry: {name}")
                    if file_text is not None:
                        require(package.read("LICENSE.txt") == file_text, f"LP-01 license fixture changed: {name}")
                    if name in ("mit", "old-namespace", "no-namespace", "type-case",
                                "supported-version", "namespace-declaration",
                                "foreign-metadata", "foreign-metadata-after"):
                        root = ET.fromstring(package.read("Sample.LP01.nuspec"))
                        namespace = root.tag.removesuffix("package")
                        children = list(root)
                        recognized = namespace + "metadata"
                        expected_order = (
                            ["{urn:other}metadata", recognized] if name == "foreign-metadata" else
                            [recognized, "{urn:other}metadata"] if name == "foreign-metadata-after" else
                            [recognized])
                        require([child.tag for child in children] == expected_order and
                                children[expected_order.index(recognized)].find(namespace + "license") is not None,
                                f"LP-01 recognized license metadata differs: {name}")
        pbytes, cbytes = policy_variant(name, policy_bytes, catalog_bytes)
        if not args.write_fixtures and pbytes != policy_bytes:
            require((ROOT / "policy" / f"{name}.json").read_bytes() == pbytes, "Policy mutation changed.")
        if not args.write_fixtures and cbytes != catalog_bytes:
            require((ROOT / "policy" / f"{name}.json").read_bytes() == cbytes, "Catalog mutation changed.")
        if name in ("policy-version-mismatch", "policy-overlap", "catalog-version-mismatch"):
            try:
                validate_policy(json.loads(pbytes), json.loads(cbytes), sha(cbytes))
            except ValueError:
                pass
            else:
                raise ValueError(f"Invalid policy control was accepted: {name}")
    for name, branch in OWNER_SPECS:
        inputs, content, record_bytes, second, grants = owner_inputs(name, policy_bytes, catalog_bytes)
        require(owner_state(inputs, content, record_bytes, second, grants, policy_bytes, policy, licenses) == branch,
                f"Owner record branch differs: {name}")
        if not args.write_fixtures:
            for item in inputs:
                if item["role"] in ("package", "owner-record", "owner-record-conflict") or item["role"].startswith("grant-"):
                    raw = (content if item["role"] == "package" else record_bytes if item["role"] == "owner-record"
                           else second if item["role"] == "owner-record-conflict" else grants[item["role"]])
                    require((ROOT / item["path"]).read_bytes() == raw and sha(raw) == item["sha256"],
                            f"Owner fixture input differs: {name}/{item['role']}")
        with zipfile.ZipFile(io.BytesIO(content)) as package:
            expected_names = expected_archive_entries(
                "owner-" + name,
                b"supplied" if name in ("type-file", "expression-file-conflict") else None)
            require(package.namelist() == expected_names and
                    package.getinfo("Sample.LP01.nuspec").date_time == ZIP_DATE,
                    f"Owner package ZIP changed: {name}")
            verify_special_archive(package, "owner-" + name)
    by_id = {case["case_id"]: case for case in synthetic["cases"]}
    _, _, at_record, _, at_grants = owner_inputs("grant-at-limit", policy_bytes, catalog_bytes)
    at_grant = at_grants["grant-mit"]
    over_grant = owner_inputs("oversize-grant", policy_bytes, catalog_bytes)[-1]["grant-mit"]
    require(len(at_grant) == MAX_GRANT_BYTES and len(over_grant) == MAX_GRANT_BYTES + 1 and
            len(at_record) < 8192 and len("MIT".encode("utf-8")) < MAX_EXPRESSION_BYTES and
            by_id["lp01-owner-grant-at-limit"]["expected"]["decision"] == "deterministic" and
            by_id["lp01-owner-oversize-grant"]["expected"]["branch_id"] == "grant-limit",
            "Grant byte cap must accept 16384 and reject 16385.")
    require(not any(item["role"] == "policy" for item in by_id["lp01-no-policy"]["inputs"]) and
            not any(item["role"] == "corpus" for item in by_id["lp01-no-corpus"]["inputs"]),
            "Missing policy/corpus controls must omit the exact required role.")
    actual_packages = {path.name for path in (ROOT / "packages").glob("*.nupkg")}
    require(actual_packages == ({name + ".nupkg" for name, *_ in SPECS if name != "no-package"} |
                               {"owner-" + name + ".nupkg" for name, _ in OWNER_SPECS}),
            "LP-01 archive inventory differs.")
    require(len({case["case_id"] for case in synthetic["cases"]}) == len(SPECS) + len(OWNER_SPECS),
            "Duplicate LP-01 cases.")
    for name in ("bytes", "tokens", "depth", "alternatives"):
        at = by_id[f"lp01-{name}-at-limit"]["expected"]
        over = by_id[f"lp01-{name}-over-limit"]["expected"]
        require(at["branch_id"] == "approved-non-copyleft" and
                at["decision"] == "escalated" and
                over["branch_id"] == "expression-limit" and
                over["decision"] == "escalated",
                f"LP-01 expression resource threshold changed: {name}")
    metadata_by_name = {name: metadata for name, metadata, _, _ in SPECS}
    require(len(metadata_by_name["bytes-at-limit"].split(">", 1)[1].split("</license>", 1)[0]) ==
            MAX_EXPRESSION_BYTES and
            len(metadata_by_name["bytes-over-limit"].split(">", 1)[1].split("</license>", 1)[0]) ==
            MAX_EXPRESSION_BYTES + 1,
            "Expression byte/character boundary controls changed.")
    for name, expected_bytes, expected_state in (
            ("multibyte-at-limit", MAX_EXPRESSION_BYTES, "invalid-expression"),
            ("multibyte-over-limit", MAX_EXPRESSION_BYTES + 1, "expression-limit")):
        raw = metadata_by_name[name].split(">", 1)[1].split("</license>", 1)[0]
        require(len(raw.encode("utf-8")) == expected_bytes and len(raw) <= MAX_EXPRESSION_BYTES and
                by_id[f"lp01-{name}"]["expected"]["branch_id"] == expected_state,
                f"Independent byte/character boundary differs: {name}")
    for name, count in (("tokens-at-limit", MAX_EXPRESSION_TOKENS),
                        ("tokens-over-limit", MAX_EXPRESSION_TOKENS + 2)):
        text = metadata_by_name[name].split(">", 1)[1].split("</license>", 1)[0]
        require(len(LEXEME.findall(text)) == count, f"SPDX token-count boundary differs: {name}")
    require(8 * 16 == MAX_ALTERNATIVES and 8 * 17 > MAX_ALTERNATIVES and
            len(LEXEME.findall(metadata_by_name["alternatives-over-limit"])) < MAX_EXPRESSION_TOKENS,
            "Alternative-count boundary must be independently reachable below the token cap.")
    for name in ("tokens-over-malformed", "bytes-over-malformed"):
        require(by_id[f"lp01-{name}"]["expected"]["branch_id"] == "expression-limit",
                f"Limit-before-syntax precedence differs: {name}")
    for case in synthetic["cases"] + acceptance["cases"]:
        expected = case["expected"]
        require(expected["inputs"] == case["inputs"] and case["row_id"] == expected["row_id"] == "LP-01",
                "LP-01 replay result input/row mismatch.")
        require(set(expected["evidence_references"]) <= set(case["evidence_pointers"]),
                "Uncited LP-01 evidence.")
        require((expected["finding"] is not None) == (expected["decision"] == "deterministic"),
                "LP-01 finding/decision mismatch.")
        require(all(expected[key] is not None for key in ("missing_fact", "required_input", "next_category")) ==
                (expected["decision"] == "escalated"), "LP-01 escalation contract mismatch.")
        if expected["branch_id"] in ("approved-non-copyleft", "file-declared"):
            require(expected["decision"] == "escalated" and expected["applicability"] == "applicable" and
                    expected["finding"] is None and expected["evidence_contract"] and
                    expected["next_category"] == "package-owner" and
                    "grant-and-release-scope" in expected["required_input"] and
                    "grant" in expected["missing_fact"],
                    f"Package classification resolved repository-wide LP-01: {case['case_id']}")
        if expected["branch_id"] in ("copyleft", "weak-copyleft", "non-osi"):
            require(expected["decision"] == "deterministic" and
                    "package-scoped negative" in expected["finding"],
                    f"LP-01 negative lost its package scope: {case['case_id']}")
    owner_cases = [case for case in synthetic["cases"] if case["case_id"].startswith("lp01-owner-")]
    package_precedence = (
        "package-gpl", "duplicate-license", "type-url", "type-file", "unsupported-version",
        "unknown-license-attribute", "dtd-entity", "foreign-metadata", "duplicate-metadata",
        "multiple-nuspec", "expression-file-conflict", "invalid-expression", "over-limit-expression",
        "dtd-unused", "utf16-le-dtd-entity", "utf16-be-dtd-entity",
        "utf16-le-dtd-unused", "utf16-be-dtd-unused",
        "duplicate-license-entry", "alias-license-entry", "duplicate-readme-entry"
    )
    for name in package_precedence:
        _, content, record_bytes, _, _ = owner_inputs(name, policy_bytes, catalog_bytes)
        expected = by_id["lp01-owner-" + name]
        require(len(record_bytes) < 8192 and any(
            item["role"] == "owner-record" for item in expected["inputs"]
        ) and expected["expected"]["branch_id"] ==
            classify_package(content, policy, licenses)[0],
            f"Owner evidence bypassed the shared package branch: {name}")
    require(len(owner_cases) == len(OWNER_SPECS) and
            [case["expected"]["branch_id"] for case in owner_cases].count("owner-evidence-accepted") == 14 and
            all(case["expected"]["decision"] == "deterministic" and
                "NOT cryptographically established" in case["expected"]["finding"]
                if case["expected"]["branch_id"] == "owner-evidence-accepted" else
                case["expected"]["decision"] == disposition(case["expected"]["branch_id"])[0] and
                ((case["expected"]["finding"] is None) ==
                 (case["expected"]["decision"] == "escalated"))
                for case in owner_cases) and
            all(case["expected"]["decision"] == "escalated" and
                not any(item["role"] == "owner-record" for item in case["inputs"])
                for case in acceptance["cases"]),
            "Owner-evidence or retained authority boundary changed.")
    missing = ["LP01_" + vendor.upper() + "_NUPKG" for vendor, *_ in VENDORS
               if not os.environ.get("LP01_" + vendor.upper() + "_NUPKG")]
    if args.require_retained and missing:
        raise ValueError("Required retained package bindings missing: " + ", ".join(missing))
    for (vendor, filename, digest, size, nuspec_name, nuspec_digest), identity in zip(
            VENDORS, retained["vendors"]):
        binding = "LP01_" + vendor.upper() + "_NUPKG"
        source = os.environ.get(binding)
        if not source:
            print(f"{binding}: skipped (binding absent)")
            continue
        path = Path(source)
        require(path.is_file(), f"{binding}: retained source file unavailable.")
        data = path.read_bytes()
        require(len(data) == size and sha(data) == digest, f"{binding}: archive identity mismatch.")
        with zipfile.ZipFile(io.BytesIO(data)) as package:
            nuspec = package.read(nuspec_name)
            for notice in identity["license_entries"]:
                notice_bytes = package.read(notice["path"])
                require(len(notice_bytes) == notice["size_bytes"] and
                        sha(notice_bytes) == notice["sha256"],
                        f"{binding}: packaged notice identity mismatch: {notice['path']}")
        require(sha(nuspec) == nuspec_digest, f"{binding}: nuspec identity mismatch.")
        root = ET.fromstring(nuspec)
        metadata = root.find(f"{{{NS}}}metadata")
        require(metadata is not None, f"{binding}: recognized metadata missing.")
        license_node = metadata.find(f"{{{NS}}}license")
        require(license_node is not None and license_node.attrib == {"type": "expression"} and
                (license_node.text or "").strip() == "MIT" and
                expression_state("MIT", policy, licenses) == "approved-non-copyleft",
                f"{binding}: MIT policy finding differs from retained archive.")
        print(f"{binding}: verified (archive/nuspec SHA-256; MIT OSI-approved non-copyleft under pinned policy)")
    print(f"LP-01: {len(synthetic['cases'])} cases, {len(actual_packages)} synthetic archives, "
          f"{len(acceptance['cases'])} retained expectations; policy {policy['policy_version']}, "
          f"SPDX {policy['spdx_version']} SHA-256 {CATALOG_SHA}.")


if __name__ == "__main__":
    main()
