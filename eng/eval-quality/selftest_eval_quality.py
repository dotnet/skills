"""Prove the eval quality gate catches each bug class it claims to.

Injects each defect into a scratch copy of a real eval, runs the gate, and
asserts it fails; then restores and asserts it passes. Without this the gate
is just a script that has never been shown to fire.
"""
import os
import shutil
import subprocess
import sys
import tempfile

REPO = os.getcwd()
GATE = os.path.join(REPO, "eng", "eval-quality", "check_eval_quality.py")


def run_gate(cwd):
    r = subprocess.run([sys.executable, GATE], cwd=cwd, capture_output=True, text=True)
    return r.returncode, r.stdout + r.stderr


def scratch():
    """A minimal repo-shaped tree the gate can scan."""
    d = tempfile.mkdtemp()
    ev = os.path.join(d, "tests", "demo", "widget")
    os.makedirs(os.path.join(ev, "fixtures", "sample"))
    os.makedirs(os.path.join(d, "plugins", "demo", "skills", "widget"))
    with open(os.path.join(ev, "fixtures", "sample", "Thing.cs"), "w") as f:
        f.write("class Thing {}\n")
    with open(os.path.join(ev, "eval.yaml"), "w") as f:
        f.write(
            "name: widget\n"
            "stimuli:\n"
            "  - name: Does the thing\n"
            "    prompt: do it\n"
            "    environment:\n"
            "      files:\n"
            "        - src: fixtures/sample\n"
            "          dest: sample\n"
            "    rubric:\n"
            "      - Did the thing\n"
        )
    # Make everything git-tracked so the tracked-files check is satisfied.
    # The commit matters: without a HEAD, `git diff --cached` fails, which used
    # to make the untracked-fixture case pass for the wrong reason and hid a
    # false negative in git_tracked_files().
    subprocess.run(["git", "init", "-q"], cwd=d, check=True)
    subprocess.run(["git", "config", "user.email", "selftest@example.invalid"], cwd=d, check=True)
    subprocess.run(["git", "config", "user.name", "eval-quality self-test"], cwd=d, check=True)
    subprocess.run(["git", "add", "-A"], cwd=d, check=True)
    subprocess.run(["git", "commit", "-qm", "baseline"], cwd=d, check=True)
    return d


def case(label, mutate, expect_fail):
    d = scratch()
    try:
        mutate(d)
        subprocess.run(["git", "add", "-A"], cwd=d, capture_output=True, check=True)
        code, out = run_gate(d)
        failed = code != 0
        ok = failed == expect_fail
        want = "FAIL" if expect_fail else "PASS"
        got = "FAIL" if failed else "PASS"
        print(f"  [{'OK ' if ok else 'BAD'}] {label:<52} expected={want} got={got}")
        if not ok:
            print("        " + out.strip().replace("\n", "\n        ")[:900])
        return ok
    finally:
        shutil.rmtree(d, ignore_errors=True)


def output_case(label, mutate, expect_substring):
    """Assert on what the gate *reports*, for checks that warn rather than fail.

    The exit code is asserted too: warnings are printed before errors, so a
    scratch tree that failed for an unrelated reason would still emit the
    expected substring and this case would pass while the gate was broken.

    Staging is checked for the same reason: a silent `git add` failure would
    change what the gate sees for any mutation that adds a new file.
    """
    d = scratch()
    try:
        mutate(d)
        subprocess.run(["git", "add", "-A"], cwd=d, capture_output=True, check=True)
        code, out = run_gate(d)
        ok = code == 0 and expect_substring in out
        print(f"  [{'OK ' if ok else 'BAD'}] {label:<52} expected={expect_substring!r}")
        if not ok:
            print(f"        exit={code}")
            print("        " + out.strip().replace("\n", "\n        ")[:900])
        return ok
    finally:
        shutil.rmtree(d, ignore_errors=True)


EV = lambda d: os.path.join(d, "tests", "demo", "widget", "eval.yaml")


def clean(d):
    pass


def missing_fixture(d):
    shutil.rmtree(os.path.join(d, "tests", "demo", "widget", "fixtures", "sample"))


def untracked_fixture(d):
    # Present on disk but excluded from git — the .gitignore class of bug.
    with open(os.path.join(d, ".gitignore"), "w") as f:
        f.write("Thing.cs\n")
    subprocess.run(["git", "rm", "--cached", "-q",
                    "tests/demo/widget/fixtures/sample/Thing.cs"], cwd=d, capture_output=True)


def bad_cobertura(d):
    p = os.path.join(d, "tests", "demo", "widget", "fixtures", "sample", "coverage.cobertura.xml")
    with open(p, "w") as f:
        f.write(
            '<?xml version="1.0"?><coverage line-rate="0.5"><packages><package name="p">'
            '<classes><class name="C" filename="C.cs" line-rate="0.5"><methods>'
            '<method name="M" signature="()" line-rate="0.90">'  # claims 90%
            '<lines><line number="1" hits="1"/><line number="2" hits="0"/></lines>'  # actually 50%
            "</method></methods></class></classes></package></packages></coverage>"
        )


def inconsistent_file_totals(d):
    # Every method agrees with its own <lines>; only the whole-file summary
    # attributes disagree with the declared file line-rate. This is the shape
    # that shipped in coverage-analysis/partial-coverage and that the
    # method-level check alone could not see.
    p = os.path.join(d, "tests", "demo", "widget", "fixtures", "sample", "coverage.cobertura.xml")
    with open(p, "w") as f:
        f.write(
            '<?xml version="1.0"?>'
            '<coverage line-rate="0.50" lines-covered="35" lines-valid="60">'  # 35/60 = 0.58
            '<packages><package name="p" line-rate="0.50">'
            '<classes><class name="C" filename="C.cs" line-rate="0.50"><methods>'
            '<method name="M" signature="()" line-rate="0.50">'
            '<lines><line number="1" hits="1"/><line number="2" hits="0"/></lines>'
            "</method></methods></class></classes></package></packages></coverage>"
        )


def aggregate_contradicts_payload(d):
    # Every method agrees with its own <lines>, and the file summary attributes
    # agree with the declared file line-rate — so checks 3 and 4 both pass. Only
    # the file/package/class rates contradict the lines actually enumerated
    # (1/4 = 0.25, not 0.75). This is the coverage-analysis/plateau shape.
    p = os.path.join(d, "tests", "demo", "widget", "fixtures", "sample", "coverage.cobertura.xml")
    with open(p, "w") as f:
        f.write(
            '<?xml version="1.0"?>'
            '<coverage line-rate="0.75" lines-covered="3" lines-valid="4">'
            '<packages><package name="p" line-rate="0.75">'
            '<classes><class name="C" filename="C.cs" line-rate="0.75"><methods>'
            '<method name="Covered" signature="()" line-rate="1.00">'
            '<lines><line number="1" hits="1"/></lines>'
            "</method>"
            '<method name="Blocker" signature="()" line-rate="0.00">'
            '<lines><line number="3" hits="0"/><line number="4" hits="0"/>'
            '<line number="5" hits="0"/></lines>'
            "</method></methods></class></classes></package></packages></coverage>"
        )


def empty_grader_config(d):
    # An edit that leaves `- type: output-matches` / `config:` with the pattern
    # attached to the NEXT list item. The document still parses; the grader
    # silently enforces nothing.
    with open(EV(d), "a") as f:
        f.write(
            "    graders:\n"
            "      - type: output-matches\n"
            "        config:\n"
            "      - type: output-matches\n"
            "        config:\n"
            "          pattern: Thing\n"
        )


def three_scenarios(d):
    # n=3, so the power warning must quote t(n-1)/sqrt(n) = 4.303/sqrt(3) = 2.48.
    # Reading the critical value at t(n) instead yields 1.84 — the off-by-one
    # that made thin evals look close to credible when they were not.
    with open(EV(d), "a") as f:
        for i in (2, 3):
            f.write(
                f"  - name: Does the thing {i}\n"
                f"    prompt: do it {i}\n"
                f"    rubric:\n"
                f"      - Did the thing {i}\n"
            )


def guard_with_reject_skills(d):
    with open(EV(d), "a") as f:
        f.write(
            "  - name: Decline off-target request\n"
            "    prompt: write me something else\n"
            "    expect_activation: false\n"
            "    rubric:\n"
            "      - Did not derail into widget analysis\n"
            "    constraints:\n"
            "      reject_skills:\n"
            '        - "*"\n'
        )


def guard_ok(d):
    with open(EV(d), "a") as f:
        f.write(
            "  - name: Decline off-target request\n"
            "    prompt: write me something else\n"
            "    expect_activation: false\n"
            "    rubric:\n"
            "      - Did not derail into widget analysis\n"
        )


print("Eval quality gate — self-test\n")
results = [
    case("clean tree", clean, expect_fail=False),
    case("fixture referenced but missing on disk", missing_fixture, expect_fail=True),
    case("fixture present but NOT tracked by git", untracked_fixture, expect_fail=True),
    case("Cobertura line-rate contradicts its <lines>", bad_cobertura, expect_fail=True),
    case("Cobertura file totals contradict file line-rate", inconsistent_file_totals, expect_fail=True),
    case("Cobertura aggregate rate contradicts its payload", aggregate_contradicts_payload, expect_fail=True),
    case("grader with an empty config enforces nothing", empty_grader_config, expect_fail=True),
    case("dormancy guard also sets reject_skills", guard_with_reject_skills, expect_fail=True),
    case("well-formed dormancy guard", guard_ok, expect_fail=False),
    output_case("power threshold uses t(n-1), not t(n)", three_scenarios, "> 2.48"),
]
print()
if all(results):
    print(f"All {len(results)} self-tests passed: the gate fires on every bug class and stays "
          f"quiet on well-formed input.")
else:
    print("SELF-TEST FAILURE — the gate does not behave as documented.")
raise SystemExit(0 if all(results) else 1)
