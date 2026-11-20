"""Polyglot source-to-test pairing analyzer using tree-sitter.

Given a repo root, identifies which source files are NOT referenced by any test
file via two complementary heuristics:

  1. Identifier overlap: a test file declares (or references) the same name
     a source file declares as a top-level symbol.
  2. Import resolution: a test file imports a module/path that resolves to a
     source file.

Supports any language tree-sitter-language-pack can parse and classify (Python,
TypeScript, JavaScript, Go, Java, Rust, C#, Ruby, Kotlin, Swift, PowerShell,
C++, ...).

Output: legacy engine-specific JSON, or a shared envelope with --output-format
json-v1. Pairing is heuristic static evidence, not behavioral coverage.

Dependencies:
    pip install tree-sitter-language-pack==1.8.1
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.metadata
import json
import posixpath
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path, PurePosixPath
from typing import Iterable

try:
    from tree_sitter_language_pack import (
        ProcessConfig,
        detect_language_from_path,
        get_parser,
        process,
    )
except ImportError as e:
    print(
        "ERROR: tree-sitter-language-pack is not installed.\n"
        "Run: pip install tree-sitter-language-pack",
        file=sys.stderr,
    )
    if "json-v1" in sys.argv or "--output-format=json-v1" in sys.argv:
        print(json.dumps({
            "schema_version": "1", "tool": "find_untested_sources", "tool_version": "1.0",
            "status": "error", "scope": {}, "inputs": [], "data": {},
            "diagnostics": [{"code": "DEPENDENCY_MISSING", "severity": "error", "message": str(e)}],
        }))
    raise SystemExit(2) from e


# Languages we'll process. Anything not in this set is skipped even if the
# pack can parse it, because we have no test-detection heuristic for it.
SUPPORTED_LANGUAGES = {
    "python",
    "typescript",
    "tsx",
    "javascript",
    "go",
    "java",
    "rust",
    "csharp",
    "ruby",
    "kotlin",
    "swift",
    "powershell",
    "cpp",
}

# Directories we never descend into. Lowercase match on segment name.
PRUNE_DIRS = {
    ".git",
    ".hg",
    ".svn",
    ".vs",
    ".vscode",
    ".idea",
    "node_modules",
    "bower_components",
    "vendor",
    "third_party",
    "dist",
    "build",
    "out",
    "target",  # rust + java
    "bin",
    "obj",
    "packages",
    "__pycache__",
    ".pytest_cache",
    ".mypy_cache",
    ".tox",
    ".venv",
    "venv",
    "env",
    ".nuget",
    "testresults",
    "coverage",
    ".next",
    ".nuxt",
    ".cache",
    ".gradle",
    ".terraform",
    "site-packages",
}

# Filename patterns to skip (generated, minified, declaration files).
SKIP_FILENAME_PATTERNS = (
    re.compile(r"\.min\.(js|css)$", re.IGNORECASE),
    re.compile(r"\.d\.ts$", re.IGNORECASE),
    re.compile(r"\.designer\.cs$", re.IGNORECASE),
    re.compile(r"\.g\.cs$", re.IGNORECASE),
    re.compile(r"\.g\.i\.cs$", re.IGNORECASE),
    re.compile(r"\.generated\.cs$", re.IGNORECASE),
    re.compile(r"AssemblyInfo\.cs$"),
    re.compile(r"AssemblyAttributes\.cs$"),
    re.compile(r"GlobalUsings?\.cs$"),
    re.compile(r"_pb2\.py$"),
    re.compile(r"_pb\.go$"),
    re.compile(r"\.pb\.go$"),
)


def is_test_path(rel: PurePosixPath, lang: str) -> bool:
    """Best-effort per-language test classification using path/filename."""
    parts = [p.lower() for p in rel.parts]
    name = rel.name.lower()
    stem = rel.stem.lower()

    if lang == "python":
        if any(p in ("tests", "test") for p in parts):
            return True
        if name.startswith("test_") or stem.endswith("_test"):
            return True
        if "conftest.py" in name:
            return True
        return False

    if lang in ("typescript", "tsx", "javascript"):
        if any(p in ("__tests__", "tests", "test", "spec", "e2e") for p in parts):
            return True
        if any(s in stem for s in (".test", ".spec")):
            return True
        return False

    if lang == "go":
        return stem.endswith("_test")

    if lang == "java":
        if "test" in parts or "tests" in parts:
            return True
        return name.endswith("test.java") or name.endswith("tests.java")

    if lang == "rust":
        if any(p in ("tests", "benches") for p in parts):
            return True
        return False

    if lang == "csharp":
        if any(p in ("tests", "test") for p in parts):
            return True
        for p in parts:
            if p.endswith(".tests") or p.endswith(".test") or p.endswith(".unittests") or p.endswith(".integrationtests"):
                return True
        # Tokenize the original (un-lowered) stem on PascalCase boundaries
        # and treat it as a test file when the final word is "Test" / "Tests".
        # This matches the .NET convention (`UserServiceTests`, `MyTest`)
        # without misclassifying non-test files whose lower-cased stem
        # coincidentally ends in "test"/"tests" (e.g. "Contest.cs",
        # "Latest.cs", "Manifest.cs").
        words = re.findall(r"[A-Z][a-z]*|[a-z]+|[0-9]+", rel.stem)
        if words and words[-1] in ("Test", "Tests"):
            return True
        return False

    if lang == "ruby":
        if any(p in ("spec", "test", "tests") for p in parts):
            return True
        return stem.endswith("_spec") or stem.endswith("_test")

    if lang == "kotlin":
        if any(p in ("test", "tests", "spec", "specs") for p in parts):
            return True
        words = re.findall(r"[A-Z][a-z]*|[a-z]+|[0-9]+", rel.stem)
        return bool(words and words[-1] in ("Test", "Tests", "Spec", "Specs"))

    if lang == "swift":
        if any(p in ("test", "tests", "uitests", "integrationtests") for p in parts):
            return True
        words = re.findall(r"[A-Z][a-z]*|[a-z]+|[0-9]+", rel.stem)
        return bool(words and words[-1] in ("Test", "Tests"))

    if lang == "powershell":
        if any(p in ("test", "tests", "pester") for p in parts):
            return True
        return name.endswith(".tests.ps1") or name.endswith(".test.ps1")

    if lang == "cpp":
        if any(p in ("test", "tests", "testing") for p in parts):
            return True
        return (
            stem.startswith("test_")
            or stem.endswith("_test")
            or stem.endswith("_tests")
        )

    return False


@dataclass
class FileInfo:
    path: Path
    rel: PurePosixPath
    lang: str
    is_test: bool
    declarations: set[str] = field(default_factory=set)
    referenced_identifiers: set[str] = field(default_factory=set)
    imports: list[str] = field(default_factory=list)
    is_source: bool = True
    complete: bool = True
    sha256: str | None = None
    test_regions: list[dict] = field(default_factory=list)

    def __hash__(self) -> int:
        return id(self)

    def __eq__(self, other: object) -> bool:
        return self is other


def should_skip_filename(name: str) -> bool:
    return any(p.search(name) for p in SKIP_FILENAME_PATTERNS)


def diagnostic(diagnostics: list[dict], code: str, path: str, error: object) -> None:
    diagnostics.append({"code": code, "severity": "warning", "path": path, "message": str(error)})


def language_family(lang: str) -> str:
    return "javascript" if lang in {"typescript", "tsx", "javascript"} else lang


def walk_files(
    root: Path, lang_filter: set[str] | None = None, diagnostics: list[dict] | None = None
) -> Iterable[Path]:
    """Yield files under root, pruning common build/vendor directories."""
    stack = [root]
    if diagnostics is None:
        diagnostics = []
    while stack:
        current = stack.pop()
        try:
            children = sorted(current.iterdir())
        except OSError as exc:
            diagnostic(diagnostics, "WALK_FAILED", str(current), exc)
            continue
        for child in children:
            try:
                if child.is_symlink():
                    continue
                is_dir, is_file = child.is_dir(), child.is_file()
            except OSError as exc:
                diagnostic(diagnostics, "STAT_FAILED", str(child), exc)
                continue
            if is_dir:
                if child.name.lower() in PRUNE_DIRS:
                    continue
                stack.append(child)
                continue
            if not is_file:
                continue
            if should_skip_filename(child.name):
                continue
            lang = detect_language_from_path(str(child))
            if lang is None:
                continue
            if lang not in SUPPORTED_LANGUAGES:
                continue
            if lang_filter is not None and lang not in lang_filter:
                continue
            yield child


def syntax_nodes(root):
    stack = [root]
    while stack:
        node = stack.pop()
        if "comment" in node.kind():
            continue
        yield node
        stack.extend(node.named_child(i) for i in reversed(range(node.named_child_count())))


def node_text(node, source: bytes) -> str:
    return source[node.start_byte():node.end_byte()].decode("utf-8")


def harvest_identifiers(text: str, lang: str, root=None, regions=None) -> set[str]:
    root = root if root is not None else get_parser(lang).parse(text).root_node()
    source = text.encode("utf-8")
    kinds = {
        "identifier", "type_identifier", "field_identifier", "property_identifier",
        "constant", "simple_name", "simple_identifier", "command_name",
    }
    result = set()
    for node in syntax_nodes(root):
        if node.kind() not in kinds:
            continue
        if regions is not None and not any(start <= node.start_byte() < end for start, end in regions):
            continue
        value = node_text(node, source)
        if re.fullmatch(r"[A-Za-z_][A-Za-z0-9_-]*", value):
            result.add(value)
    return result


def harvest_declarations(text: str, lang: str, root=None) -> set[str]:
    """Recover common declarations when the language-pack symbol view is sparse."""
    if lang not in {"powershell", "cpp"}:
        return set()
    root = root if root is not None else get_parser(lang).parse(text).root_node()
    source = bytearray(text.encode("utf-8"))
    stack = [root]
    while stack:
        node = stack.pop()
        if "comment" in node.kind() or "string" in node.kind():
            source[node.start_byte():node.end_byte()] = b" " * (node.end_byte() - node.start_byte())
            continue
        stack.extend(node.named_child(i) for i in range(node.named_child_count()))
    code = source.decode("utf-8")
    if lang == "powershell":
        return set(re.findall(r"(?im)^\s*function\s+([A-Za-z_][A-Za-z0-9-]*)", code))
    if lang == "cpp":
        return set(
            re.findall(
                r"\b(?:class|struct|enum(?:\s+class)?)\s+([A-Za-z_][A-Za-z0-9_]*)",
                code,
            )
        )
    return set()


def rust_regions(root, source: bytes) -> tuple[list[tuple[int, int]], list[dict]]:
    excluded = []
    tests = []
    for parent in syntax_nodes(root):
        attributes = []
        for i in range(parent.named_child_count()):
            node = parent.named_child(i)
            kind = node.kind()
            if kind == "attribute_item":
                attributes.append(node_text(node, source))
                continue
            if kind in {"mod_item", "function_item"}:
                if any(re.search(r"#\[\s*cfg\s*\(\s*test\s*\)\s*\]", attr) for attr in attributes):
                    excluded.append((node.start_byte(), node.end_byte()))
                if kind == "function_item" and any(
                    re.fullmatch(r"#\[\s*(?:\w+::)*test(?:\s*\([^]]*\))?\s*\]", attr)
                    for attr in attributes
                ):
                    excluded.append((node.start_byte(), node.end_byte()))
                    name = node.child_by_field_name("name")
                    tests.append({
                        "name": node_text(name, source),
                        "start_line": source[:node.start_byte()].count(b"\n") + 1,
                        "end_line": source[:node.end_byte()].count(b"\n") + 1,
                        "start_byte": node.start_byte(), "end_byte": node.end_byte(),
                        "kind": "inline-test",
                    })
            attributes = []
    return excluded, tests


def parse_file(path: Path, root: Path, diagnostics: list[dict] | None = None) -> FileInfo | None:
    rel_str = path.relative_to(root).as_posix()
    rel = PurePosixPath(rel_str)
    lang = detect_language_from_path(str(path))
    if lang is None or lang not in SUPPORTED_LANGUAGES:
        return None
    if diagnostics is None:
        diagnostics = []
    is_test = is_test_path(rel, lang)
    info = FileInfo(path=path, rel=rel, lang=lang, is_test=is_test, is_source=not is_test)
    try:
        raw = path.read_bytes()
        info.sha256 = hashlib.sha256(raw).hexdigest()
        text = raw.decode("utf-8-sig")
    except (OSError, UnicodeError) as exc:
        info.complete = False
        diagnostic(diagnostics, "READ_FAILED", rel_str, exc)
        return info

    try:
        syntax = get_parser(lang).parse(text).root_node()
        if syntax.has_error():
            info.complete = False
            diagnostic(diagnostics, "SYNTAX_ERROR", rel_str, "Parser reported missing or erroneous syntax.")
        cfg = ProcessConfig(language=lang, structure=True, imports=True, symbols=True)
        result = process(text, cfg)
    except Exception as exc:
        info.complete = False
        diagnostic(diagnostics, "PARSE_FAILED", rel_str, exc)
        return info

    # Top-level declarations: union of structure + symbols. The two views are
    # complementary depending on the language — e.g. for Go, `structure` lists
    # functions/methods but not `type` declarations, while `symbols` lists the
    # types. We filter `module`/`namespace` kinds (they're packaging, not
    # declarations) to avoid false-positive pairings on package names.
    excluded_kinds = {"module", "namespace"}

    def _kind_str(item: object) -> str:
        k = getattr(item, "kind", None)
        return str(k).lower() if k is not None else ""

    for item in getattr(result, "structure", None) or []:
        if _kind_str(item) in excluded_kinds:
            continue
        name = getattr(item, "name", None)
        if name:
            info.declarations.add(name)
    for sym in getattr(result, "symbols", None) or []:
        if _kind_str(sym) in excluded_kinds:
            continue
        name = getattr(sym, "name", None)
        if name:
            info.declarations.add(name)
    info.declarations.update(harvest_declarations(text, lang, syntax))

    reference_regions = None
    if lang == "rust":
        source = text.encode("utf-8")
        excluded, info.test_regions = rust_regions(syntax, source)
        if raw.startswith(b"\xef\xbb\xbf"):
            for region in info.test_regions:
                region["start_byte"] += 3
                region["end_byte"] += 3
        info.declarations = set()
        for node in syntax_nodes(syntax):
            if node.kind() not in {"function_item", "struct_item", "enum_item", "trait_item", "type_item", "const_item", "static_item"}:
                continue
            if any(start <= node.start_byte() < end for start, end in excluded):
                continue
            name = node.child_by_field_name("name")
            if name is not None:
                info.declarations.add(node_text(name, source))
        if info.test_regions:
            info.is_test = True
            info.is_source = not is_test and bool(info.declarations)
            if not is_test:
                reference_regions = excluded

    # Imports: keep the raw `source` field; we'll normalize per language.
    if getattr(result, "imports", None):
        for imp in result.imports:
            src = getattr(imp, "source", None)
            if src:
                info.imports.append(src)
    if lang == "rust" and info.is_source and info.is_test:
        info.imports.clear()

    if info.is_test:
        info.referenced_identifiers = harvest_identifiers(text, lang, syntax, reference_regions)

    return info


# --- Per-language import-to-path resolution --------------------------------


def _strip_quoted(value: str) -> str:
    """Return the first quoted substring or the original string trimmed."""
    m = re.search(r"""['"`]([^'"`]+)['"`]""", value)
    if m:
        return m.group(1)
    return value.strip()


def _resolve_relative_js(test_rel: PurePosixPath, target: str) -> set[PurePosixPath]:
    """Resolve ./ or ../ relative import paths to candidate source paths."""
    if not (target.startswith("./") or target.startswith("../") or target.startswith("/")):
        return set()
    base = PurePosixPath(target)
    if target.startswith("/"):
        joined = PurePosixPath(target.lstrip("/"))
    else:
        joined = PurePosixPath(posixpath.normpath((test_rel.parent / base).as_posix()))
        if ".." in joined.parts:
            return set()
    # Try various extensions and /index suffix.
    extensions = (".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs")
    if joined.suffix in extensions:
        return {joined}
    candidates = set()
    stems = [str(joined)]
    if str(joined).endswith("/index"):
        stems.append(str(joined)[: -len("/index")])
    for stem in stems:
        for ext in extensions:
            candidates.add(PurePosixPath(stem + ext))
        candidates.add(PurePosixPath(stem + "/index.ts"))
        candidates.add(PurePosixPath(stem + "/index.tsx"))
        candidates.add(PurePosixPath(stem + "/index.js"))
    return candidates


def _extract_python_module(import_text: str) -> str | None:
    """Extract `pkg.mod` from `from pkg.mod import x` or `import pkg.mod`."""
    s = import_text.strip()
    m = re.match(r"from\s+([\.\w]+)\s+import", s)
    if m:
        return m.group(1)
    m = re.match(r"import\s+([\.\w]+)", s)
    if m:
        return m.group(1)
    return None


def _python_module_to_path(
    module: str, test_rel: PurePosixPath | None = None
) -> set[PurePosixPath]:
    """Resolve a Python import target to candidate source paths.

    Leading dots (PEP 328 relative imports) are resolved against `test_rel`'s
    directory: one dot = same package as the test file, each extra dot walks
    one directory up. Without a `test_rel` context we cannot resolve them
    safely, so we return an empty set rather than risk pairing the test with
    an unrelated `utils.py` at the repo root.
    """
    leading_dots = len(module) - len(module.lstrip("."))
    rest = module[leading_dots:]
    if leading_dots > 0:
        if test_rel is None or not rest:
            # Either we have no context to resolve against, or this is a
            # `from . import x` form where the regex captured only the dots
            # and we don't know the imported name. Skip rather than guess.
            return set()
        base_dir = test_rel.parent
        for _ in range(leading_dots - 1):
            base_dir = base_dir.parent
        parts = rest.split(".")
        base_path = base_dir.joinpath(*parts) if base_dir.parts else PurePosixPath(*parts)
        return {
            PurePosixPath(str(base_path) + ".py"),
            PurePosixPath(str(base_path) + "/__init__.py"),
        }
    parts = rest.split(".")
    candidates: set[PurePosixPath] = set()
    base = "/".join(parts)
    candidates.add(PurePosixPath(base + ".py"))
    candidates.add(PurePosixPath(base + "/__init__.py"))
    return candidates


def _extract_java_fqcn(import_text: str) -> str | None:
    m = re.match(r"import\s+(?:static\s+)?([\w\.]+)", import_text.strip())
    if not m:
        return None
    fqcn = m.group(1).rstrip(";").rstrip(".*")
    return fqcn


def _java_fqcn_to_path(fqcn: str) -> PurePosixPath:
    return PurePosixPath(fqcn.replace(".", "/") + ".java")


def _extract_csharp_using(import_text: str) -> str | None:
    m = re.match(r"using\s+(?:static\s+)?([\w\.]+)", import_text.strip())
    if not m:
        return None
    return m.group(1).rstrip(";")


def _extract_rust_use(import_text: str) -> set[str]:
    """Extract crate-relative paths from `use foo::bar::Baz;`."""
    s = import_text.strip().rstrip(";")
    m = re.match(r"use\s+(.+)", s)
    if not m:
        return set()
    body = m.group(1).strip()
    # Strip `as` aliases; ignore grouped imports for simplicity.
    body = body.split(" as ")[0].strip()
    return {body}


def _extract_go_import_targets(import_text: str) -> set[str]:
    s = import_text.strip()
    targets: set[str] = set()
    for m in re.finditer(r'"([^"]+)"', s):
        targets.add(m.group(1))
    return targets


# --- Pairing engine --------------------------------------------------------


def _build_indexes(sources: list[FileInfo]) -> dict:
    """Build lookup indexes used to resolve test imports to source files."""
    by_rel: dict[str, FileInfo] = {s.rel.as_posix(): s for s in sources}
    by_decl: dict[str, list[FileInfo]] = {}
    for s in sources:
        for d in s.declarations:
            by_decl.setdefault(d, []).append(s)
    # For Java/C#: index by FQCN-like trailing path.
    by_path_suffix: dict[str, list[FileInfo]] = {}
    # For Go: index by basename so import resolution is O(1) per target
    # instead of O(#sources) per import target.
    by_filename: dict[str, list[FileInfo]] = {}
    for s in sources:
        p = s.rel.as_posix()
        parts = p.split("/")
        for i in range(len(parts)):
            suffix = "/".join(parts[i:])
            by_path_suffix.setdefault(suffix, []).append(s)
        by_filename.setdefault(s.rel.name, []).append(s)
    return {
        "by_rel": by_rel,
        "by_decl": by_decl,
        "by_path_suffix": by_path_suffix,
        "by_filename": by_filename,
    }


def _resolve_test_imports(test: FileInfo, indexes: dict, lang: str) -> set[FileInfo]:
    """Given a test file's imports, return source files those imports resolve to."""
    found: set[FileInfo] = set()
    by_rel = indexes["by_rel"]
    by_path_suffix = indexes["by_path_suffix"]

    def add_candidate(path: PurePosixPath) -> None:
        key = path.as_posix()
        if key in by_rel:
            found.add(by_rel[key])
            return
        # Try matching by suffix (covers monorepo / aliased layouts).
        matches = by_path_suffix.get(key, [])
        if len(matches) == 1:
            found.add(matches[0])

    for raw in test.imports:
        if lang in ("typescript", "tsx", "javascript"):
            target = _strip_quoted(raw)
            for c in _resolve_relative_js(test.rel, target):
                add_candidate(c)
        elif lang == "python":
            module = _extract_python_module(raw)
            if module is None:
                continue
            for c in _python_module_to_path(module, test.rel):
                add_candidate(c)
        elif lang == "go":
            by_filename = indexes["by_filename"]
            for tgt in _extract_go_import_targets(raw):
                segs = tgt.split("/")
                if not segs:
                    continue
                last = segs[-1]
                key = last + ".go"
                for info in by_filename.get(key, ()):
                    if not info.is_test:
                        found.add(info)
        elif lang == "java":
            fqcn = _extract_java_fqcn(raw)
            if fqcn:
                add_candidate(_java_fqcn_to_path(fqcn))
        elif lang == "rust":
            for use_path in _extract_rust_use(raw):
                segs = use_path.split("::")
                if not segs:
                    continue
                last = segs[-1]
                add_candidate(PurePosixPath(last + ".rs"))
                if len(segs) >= 2:
                    add_candidate(PurePosixPath(segs[-2] + ".rs"))
        elif lang == "csharp":
            ns = _extract_csharp_using(raw)
            if ns:
                # using maps to namespace, not file; we fall back to identifier overlap.
                pass
        elif lang == "ruby":
            target = _strip_quoted(raw)
            if target:
                add_candidate(PurePosixPath(target + ".rb"))
        elif lang == "kotlin":
            target = _strip_quoted(raw)
            if target:
                add_candidate(PurePosixPath(target.replace(".", "/") + ".kt"))
        elif lang in ("swift", "powershell", "cpp"):
            # These ecosystems commonly import modules, dot-source scripts, or
            # include headers rather than source files. Identifier overlap is
            # the reliable cross-project fallback for this analyzer.
            pass

    return found


def _resolve_test_by_identifiers(
    test: FileInfo,
    indexes: dict,
) -> set[FileInfo]:
    """Pair test with sources whose declarations appear in the test's token set."""
    found: set[FileInfo] = set()
    by_decl = indexes["by_decl"]
    # Iterate the test's referenced identifiers (typically O(hundreds))
    # rather than scanning every declaration in the index (O(#decls × #tests)
    # across all tests), so pairing stays linear in repo size on large
    # codebases.
    for ident in test.referenced_identifiers:
        if len(ident) < 4:
            continue
        sources = by_decl.get(ident)
        if not sources or len(sources) != 1:
            continue
        for s in sources:
            if language_family(s.lang) == language_family(test.lang):
                found.add(s)
    return found


def build_pairings(
    sources: list[FileInfo],
    tests: list[FileInfo],
) -> tuple[dict[FileInfo, set[FileInfo]], list[FileInfo]]:
    """Return (source -> covering tests) and the list of orphan test files."""
    by_lang_indexes: dict[str, dict] = {}
    for lang in {language_family(lang) for lang in SUPPORTED_LANGUAGES}:
        lang_sources = [s for s in sources if language_family(s.lang) == lang and s.complete]
        if lang_sources:
            by_lang_indexes[lang] = _build_indexes(lang_sources)

    source_to_tests: dict[FileInfo, set[FileInfo]] = {s: set() for s in sources}
    orphans: list[FileInfo] = []

    for t in tests:
        if not t.complete:
            continue
        idx = by_lang_indexes.get(language_family(t.lang))
        if idx is None:
            orphans.append(t)
            continue
        matched = _resolve_test_imports(t, idx, t.lang) | _resolve_test_by_identifiers(t, idx)
        if not matched:
            orphans.append(t)
            continue
        for s in matched:
            source_to_tests[s].add(t)

    return source_to_tests, orphans


# --- Output ----------------------------------------------------------------


def _test_filename(source: FileInfo) -> str:
    rel = source.rel
    lang = source.lang
    stem = rel.stem

    if lang == "python":
        return f"test_{stem}.py"
    if lang == "go":
        return f"{stem}_test.go"
    if lang in ("typescript", "tsx"):
        return f"{stem}.test.{rel.suffix.lstrip('.')}"
    if lang == "javascript":
        return f"{stem}.test.js"
    if lang == "java":
        return f"{stem}Test.java"
    if lang == "rust":
        return ""
    if lang == "csharp":
        return f"{stem}Tests.cs"
    if lang == "ruby":
        return f"{stem}_spec.rb"
    if lang == "kotlin":
        return f"{stem}Test.kt"
    if lang == "swift":
        return f"{stem}Tests.swift"
    if lang == "powershell":
        return f"{stem}.Tests.ps1"
    if lang == "cpp":
        return f"{stem}_test.cpp"
    return ""


def _suggest_test_path(
    source: FileInfo,
    sibling_test_dirs: dict[tuple[str, PurePosixPath], PurePosixPath],
) -> str:
    filename = _test_filename(source)
    if source.lang == "rust":
        # A sibling *_test.rs is not a Cargo target. Suggest the source file
        # for an inline #[cfg(test)] module without creating or editing it.
        return source.rel.as_posix()
    if not filename:
        return ""

    family = language_family(source.lang)
    parent = sibling_test_dirs.get((family, source.rel.parent), source.rel.parent)
    return (parent / filename).as_posix()


def build_output(
    sources: list[FileInfo],
    tests: list[FileInfo],
    source_to_tests: dict[FileInfo, set[FileInfo]],
    orphans: list[FileInfo],
    repo_root: Path,
) -> dict:
    untested: list[dict] = []
    tested: list[dict] = []
    sibling_test_dirs: dict[tuple[str, PurePosixPath], PurePosixPath] = {}
    for paired_source, covering_tests in source_to_tests.items():
        family = language_family(paired_source.lang)
        key = (family, paired_source.rel.parent)
        for test in covering_tests:
            candidate = test.rel.parent
            current = sibling_test_dirs.get(key)
            if current is None or candidate.as_posix() < current.as_posix():
                sibling_test_dirs[key] = candidate

    for s in sources:
        if not s.complete:
            continue
        covering = sorted(source_to_tests.get(s, set()), key=lambda t: t.rel.as_posix())
        entry = {
            "path": s.rel.as_posix(),
            "language": s.lang,
            "declaration_count": len(s.declarations),
            "declarations": sorted(s.declarations),
        }
        if covering:
            entry["covering_tests"] = [c.rel.as_posix() for c in covering]
            tested.append(entry)
        else:
            entry["suggested_test_path"] = _suggest_test_path(s, sibling_test_dirs)
            if s.lang == "rust":
                entry["suggestion_kind"] = "inline-test-module"
            untested.append(entry)

    return {
        "repo_root": str(repo_root),
        "summary": {
            "files": len({s.rel for s in sources + tests}),
            "source_files": len(sources),
            "test_files": len(tests),
            "tested_source_files": len(tested),
            "untested_source_files": len(untested),
            "orphan_test_files": len(orphans),
            "languages": sorted({s.lang for s in sources} | {t.lang for t in tests}),
            "unassessed_files": len({s.rel for s in sources + tests if not s.complete}),
        },
        "untested_sources": sorted(untested, key=lambda e: (-e["declaration_count"], e["path"])),
        "tested_sources": sorted(tested, key=lambda e: e["path"]),
        "orphan_tests": [
            {"path": t.rel.as_posix(), "language": t.lang} for t in sorted(orphans, key=lambda t: t.rel.as_posix())
        ],
        "test_regions": [
            {"path": t.rel.as_posix(), **region}
            for t in sorted(tests, key=lambda t: t.rel.as_posix())
            for region in t.test_regions
        ],
        "unassessed_files": [
            {"path": info.rel.as_posix(), "language": info.lang}
            for info in sorted(set(sources + tests), key=lambda info: info.rel.as_posix())
            if not info.complete
        ],
    }


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Find untested source files in a polyglot repository using tree-sitter."
    )
    parser.add_argument("root", type=Path, help="Path to repository root.")
    parser.add_argument(
        "--lang",
        action="append",
        choices=sorted(SUPPORTED_LANGUAGES),
        help="Restrict analysis to specified language(s). Repeatable.",
    )
    parser.add_argument(
        "--limit-untested",
        type=int,
        default=0,
        help="If > 0, truncate the untested_sources list to N entries.",
    )
    parser.add_argument(
        "--output-format", choices=("legacy", "json-v1"), default="legacy",
        help="Use json-v1 for versioned provenance and explicit partial results.",
    )
    parser.add_argument(
        "--include-tested",
        action="store_true",
        help="Include tested_sources in the output (omitted by default to keep payload small).",
    )
    args = parser.parse_args()

    root = args.root.resolve()
    if not root.is_dir():
        print(f"ERROR: not a directory: {root}", file=sys.stderr)
        if args.output_format == "json-v1":
            print(json.dumps(error_report("INVALID_INPUT", f"Not a directory: {root}")))
            return 2
        return 1
    if args.limit_untested < 0:
        if args.output_format == "json-v1":
            message = "--limit-untested must be nonnegative"
            print(f"INVALID_INPUT: {message}", file=sys.stderr)
            print(json.dumps(error_report("INVALID_INPUT", message)))
            return 2
        parser.error("--limit-untested must be nonnegative")

    lang_filter = set(args.lang) if args.lang else None
    print(f"Scanning {root}...", file=sys.stderr)

    sources: list[FileInfo] = []
    tests: list[FileInfo] = []
    diagnostics: list[dict] = []
    parsed_count = 0
    for path in walk_files(root, lang_filter=lang_filter, diagnostics=diagnostics):
        info = parse_file(path, root, diagnostics)
        if info is None:
            continue
        parsed_count += 1
        if info.is_test:
            tests.append(info)
        if info.is_source:
            sources.append(info)
    print(f"Parsed {parsed_count} files: {len(sources)} source, {len(tests)} test.", file=sys.stderr)

    source_to_tests, orphans = build_pairings(sources, tests)
    output = build_output(sources, tests, source_to_tests, orphans, root)

    if args.limit_untested and args.limit_untested > 0:
        output["untested_sources"] = output["untested_sources"][: args.limit_untested]
    if not args.include_tested:
        output.pop("tested_sources", None)

    for item in diagnostics:
        print(f"{item['code']}: {item['path']}: {item['message']}", file=sys.stderr)
    if args.output_format == "json-v1":
        files = sorted(set(sources + tests), key=lambda item: item.rel.as_posix())
        output = {
            "schema_version": "1", "tool": "find_untested_sources", "tool_version": "1.0",
            "status": "partial" if diagnostics else "complete",
            "scope": {
                "root": str(root), "source_revision": None, "evidence": "static-reference-pairing",
                "engine": "tree-sitter-language-pack",
                "engine_version": importlib.metadata.version("tree-sitter-language-pack"),
                "declarations": "syntax declarations; language-dependent, not a behavioral metric",
                "languages": sorted(lang_filter or SUPPORTED_LANGUAGES),
                "excluded_directories": sorted(PRUNE_DIRS), "exclude_symlinks": True,
                "untested_limit": args.limit_untested,
                "truncated": len(output["untested_sources"]) < output["summary"]["untested_source_files"],
            },
            "inputs": [{"path": item.rel.as_posix(), "sha256": item.sha256} for item in files],
            "diagnostics": diagnostics, "data": output,
        }
    elif diagnostics:
        return 3
    json.dump(output, sys.stdout, indent=2)
    sys.stdout.write("\n")
    return 3 if diagnostics else 0


def error_report(code: str, message: str) -> dict:
    return {
        "schema_version": "1", "tool": "find_untested_sources", "tool_version": "1.0",
        "status": "error", "scope": {}, "inputs": [], "data": {},
        "diagnostics": [{"code": code, "severity": "error", "message": message}],
    }


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        print("CANCELLED: analysis interrupted", file=sys.stderr)
        sys.exit(130)
    except Exception as exc:
        print(f"TOOL_ERROR: {exc}", file=sys.stderr)
        if "json-v1" in sys.argv or "--output-format=json-v1" in sys.argv:
            print(json.dumps(error_report("TOOL_ERROR", str(exc))))
        sys.exit(2)
