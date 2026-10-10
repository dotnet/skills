"""Controller-only source bindings and Vally stimulus scaffolding; never staged to an assessor."""

import hashlib
import json
from pathlib import Path
import sys

import yaml


HERE = Path(__file__).resolve().parent
EVAL = HERE.parents[1] / "eval.yaml"
FILES = ("DockDispatch.csproj", "DockBoard.razor", "DispatchPanelBase.cs", "Outbox.cs",
         "SnapshotCodec.cs", "EnvelopeCodec.cs", "CalibrationExport.cs")
CASES = ("segment-01", "segment-02")
NAMES = ("Assess a dock dispatch component", "Assess another dock dispatch component")
PROMPT = """Review the selected Blazor component in fixture/component against the one
serialization criterion in fixture/criterion.json. The criterion supplies the complete
supported context, status definitions and a small decision worksheet contract for this
review. Follow the component's applicable outbound value paths and ground the decision
in the supplied source; do not infer a whole mechanism from one example or unrelated code.
Write out/serialization-decision.json and explain the result and limits in your final
response. Inspect source only: do not change it, build or execute assessed code, install
tools, access the network, or produce a full package/component assessment."""

PROGRAM = '''import hashlib, json, sys
from pathlib import Path, PurePosixPath, PureWindowsPath

def finish(category, message):
    passed = category == "pass"
    prefix = {"pass": "VALID source-bound path worksheet: ",
              "fail": "FAIL assessor output: ", "error": "INVALID fixture/grader: "}[category]
    print(json.dumps({"name": "beq19-outbound-routing", "kind": "code", "passed": passed,
                      "score": int(passed), "status": "error" if category == "error" else "success",
                      "evidence": prefix + message}))
    raise SystemExit(0)

def unique(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("duplicate JSON key")
        result[key] = value
    return result

def constant(value):
    raise ValueError("non-JSON constant")

def load(value):
    return json.loads(value, object_pairs_hook=unique, parse_constant=constant)

def source(path):
    return path.read_bytes().decode("utf-8").replace("\\r\\n", "\\n")

def symbol(value):
    if not isinstance(value, str):
        raise ValueError("symbol must be text")
    return value.removeprefix("global::").removeprefix("DockDispatch.").removeprefix("System.Text.Json.")

try:
    binding = sys.argv[1]
    assert hashlib.sha256(binding.encode()).hexdigest() == sys.argv[2], "controller binding differs"
    expected = load(binding)
    assert set(expected) == {"status", "files", "criterion_sha256", "paths", "excluded"}
    assert expected["status"] == ("gap" if any(p["serialization"]["kind"] == "raw-value"
                                             for p in expected["paths"]) else "verified")
    root = Path("fixture")
    assert root.is_dir() and not root.is_symlink(), "missing/linked staged root"
    entries = list(root.rglob("*"))
    assert not any(p.is_symlink() for p in entries), "linked staged input"
    assert {p.relative_to(root).as_posix() for p in entries if p.is_dir()} == {"component"}
    assert {p.relative_to(root).as_posix() for p in entries if p.is_file()} == (
        {"criterion.json"} | {"component/" + f for f in expected["files"]}), "staged inventory differs"
    texts = {f: source(root / "component" / f) for f in expected["files"]}
    for name, content in texts.items():
        assert hashlib.sha256(content.encode()).hexdigest() == expected["files"][name], "source digest differs"
    criterion = source(root / "criterion.json")
    assert hashlib.sha256(criterion.encode()).hexdigest() == expected["criterion_sha256"], "criterion digest differs"
    assert load(criterion)["requirement_id"] == "BEQ-19"
    for record in expected["paths"] + expected["excluded"]:
        for anchor in record["anchors"]:
            assert texts[anchor["path"]].splitlines()[anchor["line"] - 1] == anchor["text"], "anchor differs"
except (AssertionError, IndexError, KeyError, TypeError, ValueError, OSError, UnicodeError) as error:
    finish("error", str(error) or "invalid controller configuration")

def reject(message):
    finish("fail", message)

def citations(records, anchors):
    if not isinstance(records, list) or not records:
        reject("missing source grounding")
    ranges = []
    for record in records:
        if not isinstance(record, dict) or set(record) != {"path", "start_line", "end_line", "quote"}:
            reject("citation shape differs from the common contract")
        name = record["path"]
        if not isinstance(name, str):
            reject("citation path must be text")
        path = PurePosixPath(name.replace("\\\\", "/"))
        if path.is_absolute() or PureWindowsPath(name).drive or ".." in path.parts or path.as_posix() not in texts:
            reject("citation is outside the supplied source")
        lines = texts[path.as_posix()].splitlines()
        start, end = record["start_line"], record["end_line"]
        if type(start) is not int or type(end) is not int or not 1 <= start <= end <= len(lines):
            reject("invalid citation range")
        if record["quote"] != "\\n".join(lines[start - 1:end]):
            reject("fabricated source quotation")
        ranges.append((path.as_posix(), start, end))
    for anchor in anchors:
        if not any(p == anchor["path"] and first <= anchor["line"] <= last for p, first, last in ranges):
            reject("citations do not ground a required relationship")

try:
    answer = load(Path("out/serialization-decision.json").read_bytes().decode("utf-8"))
except FileNotFoundError:
    reject("missing worksheet")
except (ValueError, UnicodeError):
    reject("worksheet must be strict UTF-8 JSON with unique keys")
except OSError as error:
    finish("error", "worksheet read failed: " + type(error).__name__)

try:
    assert isinstance(answer, dict) and set(answer) == {"requirement_id", "status", "paths", "excluded", "limits"}
    assert answer["requirement_id"] == "BEQ-19"
    assert answer["status"] in ("verified", "gap", "owner evidence required", "not tested", "not applicable")
    if answer["status"] != expected["status"]:
        reject("source does not support this conclusion (including legitimate abstention)")
    assert isinstance(answer["limits"], dict) and set(answer["limits"]) == {
        "browser_executed", "exploitability_established", "application_ids_hostile"}
    assert all(value is False for value in answer["limits"].values()), "unsupported execution/trust claim"
    paths = answer["paths"]
    assert isinstance(paths, list) and len(paths) == len(expected["paths"]), "outbound shape inventory differs"
    remaining = {p["condition"]: p for p in expected["paths"]}
    for record in paths:
        assert isinstance(record, dict) and set(record) == {
            "condition", "value", "route", "serialization", "sink", "citations"}
        required = remaining.pop(record["condition"])
        assert record["value"] == required["value"], "wrong outbound value"
        assert isinstance(record["route"], list)
        assert [symbol(v) for v in record["route"]] == required["route"], "fabricated or incomplete reached route"
        assert symbol(record["sink"]) == required["sink"], "wrong outbound sink"
        serialization = record["serialization"]
        assert isinstance(serialization, dict) and set(serialization) == {"method", "kind", "escaping"}
        assert symbol(serialization["method"]) == required["serialization"]["method"], "wrong writer API"
        assert {k: serialization[k] for k in ("kind", "escaping")} == {
            k: required["serialization"][k] for k in ("kind", "escaping")}, "wrong serialization operation"
        citations(record["citations"], required["anchors"])
    assert not remaining, "missing supported branch"
    assert isinstance(answer["excluded"], list) and len(answer["excluded"]) == len(expected["excluded"])
    for record, required in zip(answer["excluded"], expected["excluded"], strict=True):
        assert isinstance(record, dict) and set(record) == {"entry", "reason", "citations"}
        assert symbol(record["entry"]) == required["entry"] and record["reason"] == required["reason"], "wrong reachability"
        citations(record["citations"], required["anchors"])
except (AssertionError, KeyError, TypeError, ValueError) as error:
    reject(str(error) or "structured path explanation differs from the supplied source")
finish("pass", "typed normal path, applicable secondary shape, source relationships and scope limits checked")
'''


def text(path):
    return path.read_bytes().decode("utf-8").replace("\r\n", "\n")


def digest(path):
    return hashlib.sha256(text(path).encode()).hexdigest()


def anchor(case, path, needle):
    lines = text(HERE / case / path).splitlines()
    selected = [(i + 1, line) for i, line in enumerate(lines) if needle in line]
    if len(selected) != 1:
        raise ValueError(f"ambiguous/missing source binding: {case}/{path}: {needle}")
    line, content = selected[0]
    return {"path": path, "line": line, "text": content}


def oracle(index):
    case = CASES[index]
    def a(path, needle):
        return anchor(case, path, needle)
    entry = [a("DockBoard.razor", "@inherits"), a("DockBoard.razor", "@onclick"),
             a("DispatchPanelBase.cs", "protected async Task PublishAsync")]
    normal = {
        "condition": "always", "value": "snapshot",
        "route": ["DispatchPanelBase.PublishAsync", "SnapshotCodec.Encode", "DeliveryChannel.SendAsync"],
        "serialization": {"method": "JsonSerializer.SerializeToUtf8Bytes", "kind": "typed", "escaping": "default-json"},
        "sink": "DeliveryChannel.SendAsync",
        "anchors": entry + [a("DispatchPanelBase.cs", "new DispatchSnapshot"),
                            a("DispatchPanelBase.cs", "channel.SendAsync"),
                            a("SnapshotCodec.cs", "SerializeToUtf8Bytes"),
                            a("Outbox.cs", "LastSent = payload")]}
    queued = {
        "condition": "QueueEnabled", "value": "reservation.Code",
        "route": ["DispatchPanelBase.PublishAsync", "DispatchPlan.Capture", "Outbox.EnqueueAsync",
                  "EnvelopeCodec.Encode"] + (["ScalarLexeme.Quote"] if index == 0 else []) +
                 ["DeliveryChannel.SendAsync"],
        "serialization": {"method": "Utf8JsonWriter." + ("WriteRawValue" if index == 0 else "WriteStringValue"),
                          "kind": "raw-value" if index == 0 else "typed",
                          "escaping": "unescaped" if index == 0 else "default-json"},
        "sink": "DeliveryChannel.SendAsync",
        "anchors": entry + [a("DispatchPanelBase.cs", "public bool QueueEnabled"),
                            a("DispatchPanelBase.cs", "if (QueueEnabled)"),
                            a("DispatchPanelBase.cs", "DispatchPlan.Capture(ReservationCode)"),
                            a("DispatchPanelBase.cs", "await Outbox.EnqueueAsync"),
                            a("DispatchPanelBase.cs", "Capture(string code)"),
                            a("Outbox.cs", "EnvelopeCodec.Encode"),
                            a("Outbox.cs", "return channel.SendAsync"),
                            a("EnvelopeCodec.cs", "new Utf8JsonWriter"),
                            a("EnvelopeCodec.cs", 'WritePropertyName("reservation")'),
                            a("EnvelopeCodec.cs", "writer.WriteRawValue" if index == 0 else "writer.WriteStringValue"),
                            a("Outbox.cs", "LastSent = payload")]}
    if index == 0:
        queued["anchors"] += [a("CalibrationExport.cs", "Quote(string value)"),
                               a("CalibrationExport.cs", "text.Append(value)")]
    excluded = {"entry": "CalibrationExport.Emit", "reason": "not-reachable-from-selected-component",
                "anchors": [a("CalibrationExport.cs", "Task Emit"),
                            a("CalibrationExport.cs", "channel.SendAsync"),
                            a("CalibrationExport.cs", "text.Append(value)")]}
    return {"status": "gap" if index == 0 else "verified", "files": {f: digest(HERE / case / f) for f in FILES},
            "criterion_sha256": digest(HERE / "criterion.json"), "paths": [normal, queued], "excluded": [excluded]}


def stimuli():
    result = []
    for index, case in enumerate(CASES):
        binding = json.dumps(oracle(index), separators=(",", ":"))
        result.append({
            "name": NAMES[index],
            "tags": {"capability": "outbound-serialization-path-discovery",
                     "risk": "alternate-value-path-bypasses-typed-serialization" if index == 0 else
                             "unreachable-raw-code-as-component-gap",
                     "journey": "review-component-implementation"},
            "prompt": PROMPT,
            "environment": {"files": [{"src": f"fixtures/beq19-outbound-routing/{case}/{f}",
                                       "dest": f"fixture/component/{f}"} for f in FILES] +
                                     [{"src": "fixtures/beq19-outbound-routing/criterion.json",
                                       "dest": "fixture/criterion.json"}]},
            "graders": [
                {"type": "file-exists", "config": {"path": "out/serialization-decision.json"}},
                {"type": "program", "config": {"program": "python", "args": [
                    "-I", "-c", PROGRAM, binding, hashlib.sha256(binding.encode()).hexdigest()]}},
                {"type": "prompt"}],
            "rubric": [
                "The conclusion is supported by the actual button entry, inherited conditional route, "
                "projected value, encoder operation and byte-send sink. A guessed status, fabricated "
                "relationship, irrelevant citation or meaningless explanation is not a correct assessment.",
                ("Identifies the reached queued reservation value bypassing typed writes and safe escaping, "
                 "while preserving the satisfactory typed ordinary snapshot path."
                 if index == 0 else
                 "Establishes typed writes with default JSON escaping on both applicable outbound shapes, "
                 "without turning the unreachable calibration export into a component defect."),
                "Distinguishes the .NET serialization mechanism from DOM behavior or exploitability. "
                "Application-controlled identifiers are not automatically hostile input. Does not claim "
                "browser execution, broaden the assessment or rely on a sibling capture.",
                "Natural source-grounded explanations and equivalent descriptions of the actual serializer "
                "APIs are accepted. No particular wording, procedure, tool use or skill activation is rewarded."]})
    return result


class LiteralDumper(yaml.SafeDumper):
    def ignore_aliases(self, data):
        return False if isinstance(data, str) and data in (PROMPT, PROGRAM) else True


def literal(dumper, value):
    return dumper.represent_scalar("tag:yaml.org,2002:str", value, style="|" if "\n" in value else None)


LiteralDumper.add_representer(str, literal)


if __name__ == "__main__":
    if sys.argv[1:] not in (["--emit-stimuli"], ["--append-stimuli"]):
        raise SystemExit("Usage: python controller.py --emit-stimuli|--append-stimuli (no inference)")
    generated = yaml.dump({"stimuli": stimuli()}, Dumper=LiteralDumper, sort_keys=False, width=120)
    addition = "\n" + "\n".join("  " + line if line else "" for line in generated.splitlines()[1:]) + "\n"
    if sys.argv[1] == "--emit-stimuli":
        print(addition, end="")
    else:
        original = EVAL.read_bytes()
        spec = yaml.safe_load(original)
        if len(spec["stimuli"]) != 38 or any(s["name"] in NAMES for s in spec["stimuli"]):
            raise SystemExit("Refusing to append: expected exactly the existing 38 stimuli")
        combined = original + addition.encode()
        check = yaml.safe_load(combined)
        if check["stimuli"][:38] != spec["stimuli"] or check["stimuli"][38:] != stimuli():
            raise SystemExit("Generated addition changed an existing stimulus")
        EVAL.write_bytes(combined)
