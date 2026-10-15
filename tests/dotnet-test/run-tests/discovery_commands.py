"""Validate the requested standalone dotnet discovery commands, without executing them."""

import json
import os
from pathlib import Path
import re


def parse_line(line):
    result = []
    word = []
    started = False
    quote = None
    index = 0
    while index < len(line):
        char = line[index]
        following = line[index + 1:index + 2]
        assert not (quote != "'" and char == "$" and following == "("), (
            "Command substitution is not a standalone discovery command"
        )
        assert not (quote is None and char in "<>" and following == "("), (
            "Process substitution is not a standalone discovery command"
        )
        if quote != "'" and char in ("\\", "`") and following in (
            "'", '"', "\\", " ", "\t", ";", "&", "|", "#",
        ):
            word.append(following)
            started = True
            index += 2
            continue
        assert char != "`" or quote == "'", "Dynamic shell expansion is not supported"
        if quote:
            if char == quote:
                quote = None
            else:
                word.append(char)
        elif char in ("'", '"'):
            quote = char
            started = True
        elif char == "#" and not started:
            break
        elif char.isspace() or char in ";&|":
            if started:
                result.append(("".join(word), False))
                word = []
                started = False
            if char in ";&|":
                result.append((char, True))
        else:
            word.append(char)
            started = True
        index += 1
    assert quote is None, "Unclosed quote in command"
    if started:
        result.append(("".join(word), False))
    return result, line[:index].rstrip()


def tokens(line):
    return parse_line(line)[0]


def validate_command(command):
    assert command[0].replace("\\", "/").rsplit("/", 1)[-1].lower() in (
        "dotnet", "dotnet.exe",
    ), "Wrapped/scripted commands are not standalone discovery commands"
    assert len(command) > 1 and command[1] == "test", "Command is not discovery-only"
    args = command[2:]
    before, after = [], None
    flags = set()
    solution = None
    positional = []
    index = 0
    value_options = {
        "--solution", "--configuration", "-c", "--framework", "-f",
        "--results-directory", "--logger", "--filter", "--verbosity", "-v",
    }
    while index < len(args):
        argument = args[index]
        if argument == "--":
            assert after is None, "Repeated application separator"
            after = []
        else:
            target = before if after is None else after
            if argument in value_options:
                assert index + 1 < len(args), f"Missing value for {argument}"
                value = args[index + 1]
                if argument == "--solution":
                    assert after is None and solution is None, "Invalid solution selector"
                    solution = value
                target.append(argument)
                index += 1
            elif argument in {"--no-build", "--list-tests", "-t", "--no-restore", "--nologo"}:
                target.append(argument)
                flags.add(argument)
            else:
                assert not argument.startswith("-"), f"Unsupported discovery option: {argument}"
                positional.append(argument)
        index += 1
    assert "--no-build" in before, "Missing executable SDK --no-build"
    if solution is not None:
        assert solution == "Native.slnx" and not positional, "Invalid native solution"
        assert "--list-tests" in flags and "-t" not in flags, "Missing native discovery"
    elif positional == ["Bridge.slnx"]:
        assert after is not None and "--list-tests" in after, "Missing bridge discovery"
        assert "-t" not in flags, "VSTest alias cannot select MTP discovery"
    else:
        assert positional == ["VSTest.slnx"] and after is None, "Invalid VSTest solution"
        assert flags & {"--list-tests", "-t"}, "Missing VSTest discovery"
    sdk_options = {
        "--solution", "--configuration", "-c", "--framework", "-f", "--verbosity", "-v",
        "--no-build", "--no-restore", "--nologo", "--logger", "--filter",
    }
    assert after is None or not sdk_options.intersection(after), (
        "SDK option forwarded to the application"
    )
    return solution or positional[0]


def validate_output(response):
    assert isinstance(response, str) and response.strip(), "Missing command response"
    fenced = False
    lanes = []
    for original in response.splitlines():
        line = original.strip()
        code_span = False
        if line.startswith(("```", "~~~")):
            fenced = not fenced
            continue
        if not fenced:
            # Inline code in prose is a mention; a whole-line code span is a command.
            if line.startswith("`") and line.endswith("`") and line.count("`") == 2:
                line = line[1:-1]
                code_span = True
            elif not re.search(
                r"\bdotnet(?:\.exe)?[\"']?\s+\w+", line.split("`", 1)[0], re.I
            ):
                line = re.sub(r"`[^`]+`", "", line)
        prompted = line.startswith(("$ ", "> "))
        if prompted:
            line = line[2:].lstrip()
        caution = re.match(
            r"^(?:avoid\s+(?:running|using|executing|invoking|chaining)|"
            r"(?:do not|don't|never)\s+(?:run|use|execute|invoke|chain))\b",
            line, re.I,
        )
        if not fenced and caution and not any(
            operator for _, operator in tokens(line[caution.end():])
        ):
            continue
        preface = re.match(
            r"^(?:Use\s+dotnet\s+test\s+(?:with|for|to|in)\b|"
            r"(?:These|The(?:\s+following)?)\s+dotnet\s+test\s+commands?\b|"
            r"(?:I|We)\s+(?:(?:have\s+not|haven't)\s+(?:executed|run)|"
            r"(?:did\s+not|didn't)\s+(?:execute|run))\b)",
            line, re.I,
        )
        if not (fenced or code_span or prompted) and preface and line.endswith((".", ":")):
            assert not any(operator for _, operator in tokens(line[preface.end():])), (
                "Prose preface cannot contain a command chain"
            )
            continue
        if not re.search(r"\bdotnet(?:\.exe)?\b", line, re.I):
            parsed, executable = parse_line(line)
            if executable:
                assigned = bool(re.match(r"[A-Za-z_][A-Za-z0-9_]*=", executable))
                if assigned:
                    words = [value for value, operator in tokens(executable) if not operator]
                    while words and re.match(r"[A-Za-z_][A-Za-z0-9_]*=", words[0]):
                        words.pop(0)
                    executable = " ".join(words)
                compound = any(operator for _, operator in parsed)
                shell_chain = any(operator and value in {"&", "|"} for value, operator in parsed)
                segments = [executable]
                if compound:
                    segments = []
                    words = []
                    for value, operator in parsed + [("", True)]:
                        if operator:
                            if words:
                                segments.append(" ".join(
                                    json.dumps(word) if re.search(r"\s", word) else word
                                    for word in words
                                ))
                                words = []
                        else:
                            words.append(value)
                command_shaped = assigned or any(
                    (compound or not re.search(r"(?<![./])[.!?](?=\s|$)", segment)) and (
                        not compound or shell_chain or not re.match(
                            r"^(?:The|These|This|They|It|Avoid)\b|"
                            r"^(?:[a-z][a-z0-9_-]*\s+){1,3}(?:uses|forwards|lists|means|requires|supports)\b",
                            segment, re.I,
                        )
                    ) and re.fullmatch(
                        r"(?:[./\\]|[A-Za-z]:[\\/])\S+(?:\s+.*)?|"
                        r"[a-z][a-z0-9_.-]*(?:[ \t]+(?:[-./\\\w=#]+|\"[^\"]*\"|'[^']*'))*",
                        segment, re.I,
                    )
                    for segment in segments
                )
                assert not (fenced or code_span or prompted or command_shaped), (
                    "Additional command outside the requested discovery lanes"
                )
            continue
        parsed = tokens(line)
        if not parsed:
            continue
        assert not any(operator for _, operator in parsed), "Compound commands are not standalone"
        command = [value for value, _ in parsed]
        lanes.append(validate_command(command))
    assert sorted(lanes) == ["Bridge.slnx", "Native.slnx", "VSTest.slnx"], (
        "Expected exactly one command for each discovery lane"
    )


def main():
    data = json.loads(Path(os.environ["EVALUATE_GRADER_INPUT"]).read_text(encoding="utf-8"))
    validate_output(data["trajectory"]["output"])
    print(json.dumps({
        "name": "discovery-command-segments", "kind": "code", "passed": True,
        "score": 1, "evidence": "All command segments are standalone discovery-only invocations.",
    }))


if __name__ == "__main__":
    main()
