"""Time-only migrations preserve unrelated behavior and wire the production clock."""

import json
from pathlib import Path
import re


SIGNATURE = ("public", "void", "ExportSubscription", "(", "Subscription", "sub", ")")
MANAGER_SIGNATURE = ("public", "class", "SubscriptionManager")
CREATE_TRIAL_SIGNATURE = ("public", "Subscription", "CreateTrial", "(", "string", "userId", ")")
IS_ACTIVE_SIGNATURE = ("public", "bool", "IsActive", "(", "Subscription", "sub", ")")
SUBSCRIPTION_SIGNATURE = ("public", "class", "Subscription")
BASELINE = Path(".eval/baseline.json")
SOURCE = Path("FullPipeline/Services/SubscriptionManager.cs")
PROGRAM = Path("FullPipeline/Program.cs")
PROJECT = Path("FullPipeline/FullPipeline.csproj")
STATIC_DEPENDENCIES = ("File", "Directory", "Environment", "Console")


def tokens(source):
    # Tokenize strings before braces so interpolated strings do not affect nesting.
    return [
        token for token in re.findall(r'\$?@?"(?:""|\\.|[^"\\])*"|//[^\n]*|/\*.*?\*/|\w+|[^\s]', source, re.S)
        if not token.startswith(("//", "/*"))
    ]


def block(source, signature):
    source_tokens = tokens(source)
    matches = [
        index for index in range(len(source_tokens) - len(signature) + 1)
        if tuple(source_tokens[index:index + len(signature)]) == signature
    ]
    if len(matches) != 1:
        raise ValueError(f"Expected exactly one token sequence {signature}, found {len(matches)}")
    start = source_tokens.index("{", matches[0] + len(signature))
    depth = 1
    end = start
    while depth:
        end += 1
        depth += (source_tokens[end] == "{") - (source_tokens[end] == "}")
    return source_tokens[start:end + 1]


def export_body(source):
    return block(source, SIGNATURE)


def top_level_members(source, signature):
    body = block(source, signature)[1:-1]
    members = []
    current = []
    depth = 0
    for token in body:
        current.append(token)
        if token == "{":
            depth += 1
        elif token == "}":
            depth -= 1
            if depth == 0:
                members.append(current)
                current = []
        elif token == ";" and depth == 0:
            members.append(current)
            current = []
    if current:
        raise ValueError(f"Unparsed member tokens in {signature}: {current}")
    return members


def has_prefix(member, signature):
    return tuple(member[:len(signature)]) == signature


def normalize_time_access(member):
    normalized = []
    index = 0
    while index < len(member):
        if member[index:index + 3] == ["DateTime", ".", "UtcNow"]:
            normalized.append("<UTC_NOW>")
            index += 3
        elif (
            index + 7 <= len(member)
            and re.fullmatch(r"[A-Za-z_]\w*", member[index])
            and member[index + 1:index + 7]
            == [".", "GetUtcNow", "(", ")", ".", "UtcDateTime"]
        ):
            normalized.append("<UTC_NOW>")
            index += 7
        else:
            normalized.append(member[index])
            index += 1
    return normalized


def validate_manager_members(current_source, original_source):
    original_members = top_level_members(original_source, MANAGER_SIGNATURE)
    original_by_signature = {
        signature: next(member for member in original_members if has_prefix(member, signature))
        for signature in (CREATE_TRIAL_SIGNATURE, IS_ACTIVE_SIGNATURE, SIGNATURE)
    }
    required = {CREATE_TRIAL_SIGNATURE: 0, IS_ACTIVE_SIGNATURE: 0, SIGNATURE: 0}
    constructors = 0
    fields = 0
    for member in top_level_members(current_source, MANAGER_SIGNATURE):
        matched = next((signature for signature in required if has_prefix(member, signature)), None)
        if matched:
            required[matched] += 1
            expected = original_by_signature[matched]
            if matched in (CREATE_TRIAL_SIGNATURE, IS_ACTIVE_SIGNATURE):
                if normalize_time_access(member) != normalize_time_access(expected):
                    raise ValueError(f"Time-only migration changed non-time behavior in {matched[2]}")
            elif member != expected:
                raise ValueError("Time-only migration changed ExportSubscription")
        elif has_prefix(member, ("public", "SubscriptionManager", "(")):
            constructors += 1
            if "TimeProvider" not in member:
                raise ValueError("SubscriptionManager constructor has an unrelated dependency")
        elif member[-1] == ";" and "TimeProvider" in member:
            fields += 1
        else:
            raise ValueError(f"Unexpected SubscriptionManager member: {member[:8]}")
    if any(count != 1 for count in required.values()):
        raise ValueError(f"Expected one of each production method, found {required}")
    if constructors > 1 or fields > 1:
        raise ValueError("Unexpected duplicate TimeProvider constructor or field")


def top_level_statements(source):
    statements = []
    current = []
    depth = 0
    for token in tokens(source):
        current.append(token)
        if token in ("(", "{", "["):
            depth += 1
        elif token in (")", "}", "]"):
            depth -= 1
        elif token == ";" and depth == 0:
            statements.append(current)
            current = []
    if current or depth != 0:
        raise ValueError("Program contains unsupported top-level control flow")
    return statements


def validate_program(current_source, original_source):
    original = top_level_statements(original_source)
    current = top_level_statements(current_source)
    position = 0
    additions = []
    for index, statement in enumerate(current):
        if position < len(original) and statement == original[position]:
            position += 1
        else:
            additions.append((index, statement))
    if position != len(original):
        raise ValueError("Time-only migration removed or changed existing Program statements")
    clock_registrations = 0
    manager_registrations = 0
    clock_type = r"(?:global::)?(?:System\.)?TimeProvider"
    clock_instance = clock_type + r"\.System"
    clock_method = r"builder\.Services\.(?:AddSingleton|TryAddSingleton)"
    clock_forms = (
        clock_method + rf"(?:<{clock_type}>)?\({clock_instance}\);",
        clock_method + rf"<{clock_type}>\((?:static)?[A-Za-z_]\w*=>{clock_instance}\);",
        clock_method + rf"\(typeof\({clock_type}\),{clock_instance}\);",
    )
    manager_form = (
        r"builder\.Services\.(?:AddSingleton|AddScoped|AddTransient)"
        r"<(?:global::)?(?:FullPipeline\.Services\.)?SubscriptionManager>\(\);"
    )
    build_index = next(
        (index for index, statement in enumerate(current)
         if re.search(r"\bbuilder\.Build\(", "".join(statement))),
        len(current),
    )
    for index, statement in additions:
        expression = "".join(statement)
        if any(re.fullmatch(form, expression) for form in clock_forms):
            if index >= build_index:
                raise ValueError("Production TimeProvider must be registered before builder.Build")
            clock_registrations += 1
        elif re.fullmatch(manager_form, expression):
            manager_registrations += 1
        else:
            raise ValueError("Time-only migration added an unrelated Program statement")
    if clock_registrations != 1 or manager_registrations > 1:
        raise ValueError("Time-only migration requires one production TimeProvider registration")


def baseline_bytes(baseline, path):
    try:
        return bytes.fromhex(baseline[path.as_posix()])
    except (KeyError, ValueError) as error:
        raise ValueError(f"Invalid authenticated baseline entry: {path}") from error


def verify():
    baseline = json.loads(BASELINE.read_text(encoding="utf-8"))
    for path in (SOURCE, PROGRAM, PROJECT):
        if path.is_symlink() or not path.is_file():
            raise ValueError(f"Missing or symlinked protected file: {path}")
    original_source = baseline_bytes(baseline, SOURCE).decode("utf-8-sig")
    current_source = SOURCE.read_text(encoding="utf-8-sig")
    validate_manager_members(current_source, original_source)
    if export_body(current_source) != export_body(original_source):
        raise ValueError(
            "Time-only migration changed ExportSubscription "
            "(filesystem/environment/console behavior)"
        )
    if block(current_source, SUBSCRIPTION_SIGNATURE) != block(
        original_source, SUBSCRIPTION_SIGNATURE
    ):
        raise ValueError("Time-only migration changed the Subscription data contract")
    original_tokens = tokens(original_source)
    current_tokens = tokens(current_source)
    for dependency in STATIC_DEPENDENCIES:
        if current_tokens.count(dependency) != original_tokens.count(dependency):
            raise ValueError(f"Time-only migration changed {dependency} dependency usage")
    original_program = baseline_bytes(baseline, PROGRAM).decode("utf-8-sig")
    validate_program(PROGRAM.read_text(encoding="utf-8-sig"), original_program)
    if PROJECT.read_bytes() != baseline_bytes(baseline, PROJECT):
        raise ValueError("Time-only migration changed the production project file")
    print("Out-of-scope production behavior and project configuration are unchanged.")


def main():
    verify()


if __name__ == "__main__":
    main()
