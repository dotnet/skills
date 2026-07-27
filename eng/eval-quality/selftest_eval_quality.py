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
    subprocess.run(["git", "init", "-q"], cwd=d, check=True)
    subprocess.run(["git", "add", "-A"], cwd=d, check=True)
    return d


def case(label, mutate, expect_fail):
    d = scratch()
    try:
        mutate(d)
        subprocess.run(["git", "add", "-A"], cwd=d, capture_output=True)
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
    case("dormancy guard also sets reject_skills", guard_with_reject_skills, expect_fail=True),
    case("well-formed dormancy guard", guard_ok, expect_fail=False),
]
print()
if all(results):
    print(f"All {len(results)} self-tests passed: the gate fires on every bug class and stays "
          f"quiet on well-formed input.")
else:
    print("SELF-TEST FAILURE — the gate does not behave as documented.")
raise SystemExit(0 if all(results) else 1)
