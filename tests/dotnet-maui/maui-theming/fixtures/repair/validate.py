from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET


def assert_file_set(root: Path, expected: set[str]) -> None:
    def ignored(path: Path) -> bool:
        relative = path.relative_to(root)
        if ".eval" in relative.parts:
            return True
        current = path.parent
        while current != root.parent:
            if (current / "SKILL.md").is_file():
                return True
            if current == root:
                break
            current = current.parent
        return False

    actual = {
        path.relative_to(root).as_posix()
        for path in root.rglob("*")
        if path.is_file() and not ignored(path)
    }
    if actual != expected:
        raise AssertionError(f"unexpected file set: {sorted(actual)}")


def local_name(name: str) -> str:
    return name.rsplit("}", 1)[-1]


def attribute(element: ET.Element, name: str) -> str | None:
    return next(
        (value for key, value in element.attrib.items() if local_name(key) == name),
        None,
    )


def mask_comments_and_literals(source: str) -> str:
    output: list[str] = []
    index = 0
    state = "code"
    while index < len(source):
        char = source[index]
        next_char = source[index + 1] if index + 1 < len(source) else ""
        if state == "code":
            if char == "/" and next_char == "/":
                output.extend("  ")
                index += 2
                state = "line-comment"
                continue
            if char == "/" and next_char == "*":
                output.extend("  ")
                index += 2
                state = "block-comment"
                continue
            if char == "@" and next_char == '"':
                output.extend("  ")
                index += 2
                state = "verbatim-string"
                continue
            if char == '"':
                output.append(" ")
                index += 1
                state = "string"
                continue
            if char == "'":
                output.append(" ")
                index += 1
                state = "char"
                continue
            output.append(char)
            index += 1
            continue
        if state == "line-comment":
            output.append("\n" if char == "\n" else " ")
            index += 1
            if char == "\n":
                state = "code"
            continue
        if state == "block-comment":
            if char == "*" and next_char == "/":
                output.extend("  ")
                index += 2
                state = "code"
            else:
                output.append("\n" if char == "\n" else " ")
                index += 1
            continue
        if state in {"string", "char"}:
            output.append("\n" if char == "\n" else " ")
            index += 1
            if char == "\\" and index < len(source):
                output.append("\n" if source[index] == "\n" else " ")
                index += 1
            elif (state == "string" and char == '"') or (state == "char" and char == "'"):
                state = "code"
            continue
        if state == "verbatim-string":
            output.append("\n" if char == "\n" else " ")
            index += 1
            if char == '"' and next_char == '"':
                output.append(" ")
                index += 1
            elif char == '"':
                state = "code"
    if state in {"block-comment", "string", "char", "verbatim-string"}:
        raise AssertionError(f"unterminated C# {state}")
    return "".join(output)


def matching_brace(code: str, opening: int) -> int:
    depth = 0
    for index in range(opening, len(code)):
        if code[index] == "{":
            depth += 1
        elif code[index] == "}":
            depth -= 1
            if depth < 0:
                raise AssertionError("unbalanced C# braces")
            if depth == 0:
                return index
    raise AssertionError("unbalanced C# braces")


def assert_balanced_braces(code: str) -> None:
    depth = 0
    for char in code:
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth < 0:
                raise AssertionError("unbalanced C# braces")
    if depth != 0:
        raise AssertionError("unbalanced C# braces")


root = Path(sys.argv[1]).resolve()
assert_file_set(root, {"MainPage.xaml", "ThemeService.cs"})

try:
    page = ET.parse(root / "MainPage.xaml").getroot()
except ET.ParseError as error:
    raise AssertionError(f"malformed XAML: {error}") from error
if local_name(page.tag) != "ContentPage":
    raise AssertionError("root element must be ContentPage")
if attribute(page, "BackgroundColor") != "{DynamicResource PageBackgroundColor}":
    raise AssertionError("page background must use DynamicResource")
labels = [element for element in page.iter() if local_name(element.tag) == "Label"]
if (
    len(labels) != 1
    or attribute(labels[0], "Text") != "Welcome"
    or attribute(labels[0], "TextColor") != "{DynamicResource PrimaryTextColor}"
):
    raise AssertionError("exactly one Welcome Label must use the dynamic text color")
code = mask_comments_and_literals((root / "ThemeService.cs").read_text(encoding="utf-8"))
code = mask_comments_and_literals((root / "ThemeService.cs").read_text(encoding="utf-8"))
assert_balanced_braces(code)
methods = list(
    re.finditer(
        r"\bpublic\s+static\s+void\s+ApplyTheme\s*\(\s*ResourceDictionary\s+theme\s*\)\s*\{",
        code,
    )
)
if len(methods) != 1:
    raise AssertionError("exactly one ApplyTheme(ResourceDictionary theme) method is required")
opening = code.find("{", methods[0].start())
body = code[opening + 1 : matching_brace(code, opening)]
if not re.search(r"\bprivate\s+static\s+ResourceDictionary\?\s+_currentTheme\s*;", code):
    raise AssertionError("current theme reference is missing")
if re.search(r"\.\s*Clear\s*\(", body):
    raise AssertionError("theme switching must not clear template dictionaries")
required = [
    r"\bvar\s+merged\s*=\s*Application\.Current!\.Resources\.MergedDictionaries\s*;",
    r"\bif\s*\(\s*_currentTheme\s+is\s+not\s+null\s*\)",
    r"\bmerged\s*\.\s*Remove\s*\(\s*_currentTheme\s*\)\s*;",
    r"\bmerged\s*\.\s*Add\s*\(\s*theme\s*\)\s*;",
    r"\b_currentTheme\s*=\s*theme\s*;",
]
positions: list[int] = []
for pattern in required:
    match = re.search(pattern, body)
    if not match:
        raise AssertionError(f"missing ApplyTheme operation: {pattern}")
    positions.append(match.start())
if positions != sorted(positions):
    raise AssertionError("ApplyTheme operations are in the wrong order")
