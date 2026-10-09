from __future__ import annotations

from pathlib import Path
import os
import shutil
import subprocess
import tempfile
import yaml


ROOT = Path(__file__).parent
FIXTURES = ROOT / "fixtures"
SOLUTIONS = ROOT / "solutions"
CASES = [
    "static-registration",
    "dam-activation",
    "boxed-type-flow",
    "dynamic-code-island",
    "open-generic-boundary",
    "json-source-generation",
    "unsafe-accessor",
    "intentional-scanning-boundary",
    "trimmable-jit-library",
    "aot-compatible-library",
    "already-compatible",
]


def prepare(case: str, destination: Path) -> None:
    shutil.copytree(FIXTURES / case, destination)
    shutil.copy2(FIXTURES / "common" / "global.json", destination / "global.json")
    shutil.copy2(FIXTURES / "common" / "NuGet.Config", destination / "NuGet.Config")
    shutil.rmtree(destination / ".eval")


def overlay_solution(case: str, destination: Path) -> None:
    solution = SOLUTIONS / case
    if solution.exists():
        for source in solution.rglob("*"):
            if source.is_file():
                relative = source.relative_to(solution)
                target = destination / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(source, target)

def grader_commands() -> dict[str, str]:
    spec = yaml.safe_load((ROOT / "eval.yaml").read_text(encoding="utf-8"))
    commands: dict[str, str] = {}
    for stimulus in spec["stimuli"]:
        if stimulus.get("expect_activation") is False:
            continue
        fixture_sources = [
            item["src"]
            for item in stimulus["environment"]["files"]
            if item["src"].startswith("fixtures/")
            and not item["src"].startswith("fixtures/common/")
        ]
        if len(fixture_sources) != 1:
            raise AssertionError(f"Expected one case fixture for {stimulus['name']}")
        case = fixture_sources[0].split("/", 1)[1]
        run_commands = [
            grader["config"]["command"]
            for grader in stimulus["graders"]
            if grader["type"] == "run-command"
        ]
        if len(run_commands) != 1:
            raise AssertionError(f"Expected one run-command grader for {stimulus['name']}")
        commands[case] = run_commands[0]

    if set(commands) != set(CASES):
        raise AssertionError(f"Eval cases do not match fixture cases: {sorted(commands)}")
    return commands


def verify(destination: Path, command: str, should_pass: bool) -> None:
    if os.name == "nt":
        shell_command = ["sh", "-c", command]
    else:
        shell_command = ["/bin/sh", "-c", command]
    result = subprocess.run(
        shell_command,
        cwd=destination,
        check=False,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
    )
    if (result.returncode == 0) != should_pass:
        expectation = "pass" if should_pass else "fail"
        raise AssertionError(
            f"{destination.name} verifier should {expectation}:\n{result.stdout}"
        )


def restore_input(destination: Path, case: str, relative: str) -> None:
    shutil.copy2(FIXTURES / case / relative, destination / relative)


def apply_mutation(case: str, destination: Path) -> None:
    if case == "static-registration":
        (destination / "Program.cs").write_text(
            'Console.WriteLine("PASS");\n',
            encoding="utf-8",
        )
    elif case == "dam-activation":
        path = destination / "PluginFactory.cs"
        path.write_text(
            path.read_text(encoding="utf-8").replace(
                "DynamicallyAccessedMemberTypes.PublicParameterlessConstructor",
                "DynamicallyAccessedMemberTypes.All",
            ),
            encoding="utf-8",
        )
    elif case == "boxed-type-flow":
        restore_input(destination, case, "RequestDispatcher.cs")
    elif case == "dynamic-code-island":
        restore_input(destination, case, "FormatterFactory.cs")
    elif case == "open-generic-boundary":
        path = destination / "RuntimeGenericFactory.cs"
        path.write_text(
            path.read_text(encoding="utf-8").replace(
                "[RequiresUnreferencedCode",
                "[UnconditionalSuppressMessage(\"Trimming\", \"IL2055\")]\n    // [RequiresUnreferencedCode",
            ),
            encoding="utf-8",
        )
    elif case == "json-source-generation":
        restore_input(destination, case, "MessageSerializer.cs")
    elif case == "unsafe-accessor":
        restore_input(destination, case, "CounterInterop.cs")
    elif case == "intentional-scanning-boundary":
        path = destination / "PluginCatalog.cs"
        path.write_text(
            path.read_text(encoding="utf-8").replace(
                "[RequiresUnreferencedCode(",
                "[UnconditionalSuppressMessage(\"Trimming\", \"IL2026\", Justification = \"Make build green\")]\n    [RequiresUnreferencedCode(",
            ),
            encoding="utf-8",
        )
    elif case == "trimmable-jit-library":
        path = destination / "TrimmableJitLibrary.csproj"
        path.write_text(
            path.read_text(encoding="utf-8").replace("IsTrimmable", "IsAotCompatible"),
            encoding="utf-8",
        )
    elif case == "aot-compatible-library":
        restore_input(destination, case, "AotCompatibleLibrary.csproj")
    elif case == "already-compatible":
        with (destination / "Program.cs").open("a", encoding="utf-8") as stream:
            stream.write("// unnecessary edit\n")
    else:
        raise AssertionError(f"Missing mutation for {case}")


def main() -> None:
    commands = grader_commands()
    with tempfile.TemporaryDirectory(prefix="dotnet-aot-compat-") as temp:
        temp_root = Path(temp)
        for case in CASES:
            destination = temp_root / case
            prepare(case, destination)
            verify(destination, commands[case], should_pass=case == "already-compatible")
            overlay_solution(case, destination)
            verify(destination, commands[case], should_pass=True)
            apply_mutation(case, destination)
            verify(destination, commands[case], should_pass=False)
            pristine = "accepted" if case == "already-compatible" else "rejected"
            print(f"PASS {case}: pristine {pristine}; golden solution accepted; mutation rejected")


if __name__ == "__main__":
    main()
