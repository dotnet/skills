from pathlib import Path
import re
import sys


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


def method_bodies(code: str, method: str) -> list[str]:
    pattern = re.compile(
        rf"\bprotected\s+override\s+void\s+{re.escape(method)}\s*\([^)]*\)\s*\{{"
    )
    bodies: list[str] = []
    for match in pattern.finditer(code):
        opening = code.find("{", match.start())
        closing = matching_brace(code, opening)
        bodies.append(code[opening + 1 : closing])
    return bodies


target = Path(sys.argv[1]).resolve()
root = target.parent
assert_file_set(root, {"App.cs"})
code = mask_comments_and_literals(target.read_text(encoding="utf-8"))
if "{" not in code:
    raise AssertionError("missing C# type body")
assert_balanced_braces(code)

stopped = method_bodies(code, "OnStopped")
resumed = method_bodies(code, "OnResumed")
deactivated = method_bodies(code, "OnDeactivated")
if len(stopped) != 1 or "Preferences.Set" not in stopped[0] or "base.OnStopped" not in stopped[0]:
    raise AssertionError("OnStopped must persist the draft and call its base method")
if len(resumed) != 1 or "Preferences.Get" not in resumed[0] or "base.OnResumed" not in resumed[0]:
    raise AssertionError("OnResumed must restore the draft and call its base method")
if any("Preferences.Set" in body for body in deactivated):
    raise AssertionError("OnDeactivated must not persist the draft")
