"""Polyglot source-to-test pairing analyzer using tree-sitter.

Given a repo root, identifies which source files are NOT covered by any test
file via two complementary heuristics:

  1. Identifier overlap: a test file declares (or references) the same name
     a source file declares as a top-level symbol.
  2. Import resolution: a test file imports a module/path that resolves to a
     source file.

Supports any language tree-sitter-language-pack can parse and classify (Python,
TypeScript, JavaScript, Go, Java, Rust, C#, Ruby, ...).

Output: JSON to stdout matching the schema used by the C# `find-untested-sources`
skill, so the same prompt patterns can consume both tools.

Dependencies:
    pip install tree-sitter-language-pack
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path, PurePosixPath
from typing import Iterable

try:
    from tree_sitter_language_pack import (
        ProcessConfig,
        detect_language_from_path,
        process,
    )
except ImportError as e:
    print(
        "ERROR: tree-sitter-language-pack is not installed.\n"
        "Run: pip install tree-sitter-language-pack",
        file=sys.stderr,
    )
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
    "TestResults",
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

    def __hash__(self) -> int:
        return id(self)

    def __eq__(self, other: object) -> bool:
        return self is other


def should_skip_filename(name: str) -> bool:
    return any(p.search(name) for p in SKIP_FILENAME_PATTERNS)


def walk_files(root: Path, lang_filter: set[str] | None = None) -> Iterable[Path]:
    """Yield files under root, pruning common build/vendor directories."""
    stack = [root]
    while stack:
        current = stack.pop()
        try:
            children = list(current.iterdir())
        except (PermissionError, OSError):
            continue
        for child in children:
            try:
                if child.is_symlink():
                    continue
            except OSError:
                continue
            if child.is_dir():
                if child.name.lower() in PRUNE_DIRS:
                    continue
                stack.append(child)
                continue
            if not child.is_file():
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


# Word-character regex used to harvest identifiers from a test file's source
# for the "identifier overlap" pairing strategy. Tree-sitter's `symbols` output
# only gives us declared names; references (`new Foo()`, `Foo.bar()`) won't
# appear, so we fall back to a simple word scan over the file body.
IDENTIFIER_RE = re.compile(r"[A-Za-z_][A-Za-z0-9_]*")


def harvest_identifiers(text: str) -> set[str]:
    return set(IDENTIFIER_RE.findall(text))


def parse_file(path: Path, root: Path) -> FileInfo | None:
    rel_str = path.relative_to(root).as_posix()
    rel = PurePosixPath(rel_str)
    lang = detect_language_from_path(str(path))
    if lang is None or lang not in SUPPORTED_LANGUAGES:
        return None
    try:
        text = path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return None

    is_test = is_test_path(rel, lang)
    info = FileInfo(path=path, rel=rel, lang=lang, is_test=is_test)

    try:
        cfg = ProcessConfig(language=lang, structure=True, imports=True, symbols=True)
        result = process(text, cfg)
    except Exception as exc:
        # Emit a diagnostic so users can see when tree-sitter parsing silently
        # degrades results (e.g. 0-declaration sources because the parser
        # blew up on an unusual construct). Keep it short — one line per file.
        print(f"WARN: tree-sitter parse failed for {rel_str} ({lang}): {exc}", file=sys.stderr)
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

    # Imports: keep the raw `source` field; we'll normalize per language.
    if getattr(result, "imports", None):
        for imp in result.imports:
            src = getattr(imp, "source", None)
            if src:
                info.imports.append(src)

    # For test files only, scan all identifier-like tokens — caller uses these
    # to pair with source declarations by name.
    if is_test:
        info.referenced_identifiers = harvest_identifiers(text)

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
        joined_str = (test_rel.parent / base).as_posix()
        # Collapse any number of "<seg>/../" pairs, including chained ones
        # like "a/b/../../c" → "c". The previous regex only collapsed a single
        # occurrence so chained "../../foo" imports stayed partially
        # un-normalized and failed to match indexed paths.
        prev = None
        while prev != joined_str:
            prev = joined_str
            joined_str = re.sub(r"(?:^|/)[^/]+/\.\./", "/", joined_str)
        joined_str = joined_str.lstrip("/")
        joined = PurePosixPath(joined_str)
    # Try various extensions and /index suffix.
    candidates = set()
    stems = [str(joined)]
    if str(joined).endswith("/index"):
        stems.append(str(joined)[: -len("/index")])
    for stem in stems:
        for ext in (".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs"):
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
    by_rel: dict[str, FileInfo] = {s.rel.as_posix().lower(): s for s in sources}
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
        p = s.rel.as_posix().lower()
        parts = p.split("/")
        for i in range(len(parts)):
            suffix = "/".join(parts[i:])
            by_path_suffix.setdefault(suffix, []).append(s)
        by_filename.setdefault(s.rel.name.lower(), []).append(s)
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
        key = path.as_posix().lower()
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
                key = (last + ".go").lower()
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
        if not sources:
            continue
        for s in sources:
            if s.lang == test.lang:
                found.add(s)
    return found


def build_pairings(
    sources: list[FileInfo],
    tests: list[FileInfo],
) -> tuple[dict[FileInfo, set[FileInfo]], list[FileInfo]]:
    """Return (source -> covering tests) and the list of orphan test files."""
    by_lang_indexes: dict[str, dict] = {}
    for lang in SUPPORTED_LANGUAGES:
        lang_sources = [s for s in sources if s.lang == lang]
        if lang_sources:
            by_lang_indexes[lang] = _build_indexes(lang_sources)

    source_to_tests: dict[FileInfo, set[FileInfo]] = {s: set() for s in sources}
    orphans: list[FileInfo] = []

    for t in tests:
        idx = by_lang_indexes.get(t.lang)
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


def _suggest_test_path(source: FileInfo) -> str:
    rel = source.rel
    lang = source.lang
    stem = rel.stem
    parent = rel.parent

    if lang == "python":
        return str(parent / f"test_{stem}.py")
    if lang == "go":
        return str(parent / f"{stem}_test.go")
    if lang in ("typescript", "tsx"):
        return str(parent / f"{stem}.test.{rel.suffix.lstrip('.')}")
    if lang == "javascript":
        return str(parent / f"{stem}.test.js")
    if lang == "java":
        return str(parent / f"{stem}Test.java")
    if lang == "rust":
        return str(parent / f"{stem}_test.rs")
    if lang == "csharp":
        return str(parent / f"{stem}Tests.cs")
    if lang == "ruby":
        return str(parent / f"{stem}_spec.rb")
    return ""


def build_output(
    sources: list[FileInfo],
    tests: list[FileInfo],
    source_to_tests: dict[FileInfo, set[FileInfo]],
    orphans: list[FileInfo],
    repo_root: Path,
) -> dict:
    untested: list[dict] = []
    tested: list[dict] = []
    for s in sources:
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
            entry["suggested_test_path"] = _suggest_test_path(s)
            untested.append(entry)

    return {
        "repo_root": str(repo_root),
        "summary": {
            "source_files": len(sources),
            "test_files": len(tests),
            "tested_source_files": len(tested),
            "untested_source_files": len(untested),
            "orphan_test_files": len(orphans),
            "languages": sorted({s.lang for s in sources} | {t.lang for t in tests}),
        },
        "untested_sources": sorted(untested, key=lambda e: (-e["declaration_count"], e["path"])),
        "tested_sources": sorted(tested, key=lambda e: e["path"]),
        "orphan_tests": [
            {"path": t.rel.as_posix(), "language": t.lang} for t in sorted(orphans, key=lambda t: t.rel.as_posix())
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
        "--include-tested",
        action="store_true",
        help="Include tested_sources in the output (omitted by default to keep payload small).",
    )
    args = parser.parse_args()

    root = args.root.resolve()
    if not root.is_dir():
        print(f"ERROR: not a directory: {root}", file=sys.stderr)
        return 1

    lang_filter = set(args.lang) if args.lang else None
    print(f"Scanning {root}...", file=sys.stderr)

    sources: list[FileInfo] = []
    tests: list[FileInfo] = []
    parsed_count = 0
    for path in walk_files(root, lang_filter=lang_filter):
        info = parse_file(path, root)
        if info is None:
            continue
        parsed_count += 1
        if info.is_test:
            tests.append(info)
        else:
            sources.append(info)
    print(f"Parsed {parsed_count} files: {len(sources)} source, {len(tests)} test.", file=sys.stderr)

    source_to_tests, orphans = build_pairings(sources, tests)
    output = build_output(sources, tests, source_to_tests, orphans, root)

    if args.limit_untested and args.limit_untested > 0:
        output["untested_sources"] = output["untested_sources"][: args.limit_untested]
    if not args.include_tested:
        output.pop("tested_sources", None)

    json.dump(output, sys.stdout, indent=2)
    sys.stdout.write("\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
