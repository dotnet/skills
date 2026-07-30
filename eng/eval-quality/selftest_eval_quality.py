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


def run_gate(cwd, *extra):
    r = subprocess.run([sys.executable, GATE, *extra], cwd=cwd, capture_output=True, text=True)
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
            # Meets the MIN_TRIALS floor with a single scenario, which keeps the
            # rest of the cases small. Trials = scenarios x runs.
            "defaults:\n"
            "  runs: 5\n"
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


def case(label, mutate, expect_fail, gate_args=()):
    d = scratch()
    try:
        mutate(d)
        subprocess.run(["git", "add", "-A"], cwd=d, capture_output=True, check=True)
        code, out = run_gate(d, *gate_args)
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


def grandfathered_reports_its_arithmetic(d):
    # The gate's job for a grandfathered eval is to tell the contributor what to
    # change, so the reported figure must be the trial arithmetic and not just
    # the scenario count. (This replaces #964's t(n-1) case: the gate is an
    # exact sign test now, so there is no critical value left to misquote.)
    drop_runs(d)
    write_allowlist(d, SPEC)


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


# --- statistical power ------------------------------------------------------
# Trials = scenarios x runs. Below the floor the pass gate cannot reach a
# credible verdict at any effect size, so a new eval must not land there.

SPEC = "tests/demo/widget/eval.yaml"


def write_allowlist(d, *entries):
    path = os.path.join(d, "eng", "eval-quality")
    os.makedirs(path, exist_ok=True)
    with open(os.path.join(path, "underpowered-allowlist.txt"), "w") as f:
        f.write("# debt ledger\n")
        for entry in entries:
            f.write(entry + "\n")


def drop_runs(d):
    """1 scenario x runs=1 = 1 trial: vally reports no interval at all there."""
    with open(EV(d)) as f:
        text = f.read()
    with open(EV(d), "w") as f:
        f.write(text.replace("defaults:\n  runs: 5\n", ""))


def underpowered(d):
    drop_runs(d)


def underpowered_but_allowlisted(d):
    drop_runs(d)
    write_allowlist(d, SPEC)


def allowlisted_eval_that_now_meets_the_floor(d):
    # The eval is fine; the exemption is stale and must be given up, or it
    # silently covers whatever the eval becomes next.
    write_allowlist(d, SPEC)


def allowlist_entry_for_a_spec_that_does_not_exist(d):
    write_allowlist(d, "tests/demo/deleted/eval.yaml")


def runs_lifts_a_single_scenario_over_the_floor(d):
    # Already the scratch tree's shape; asserted explicitly so a regression in
    # the scenarios-x-runs arithmetic can't hide behind the other cases.
    pass


def agent_eval_exempted(d):
    # agent.* evals never receive a verdict, so they never need an exemption —
    # and an entry for one would otherwise sit in the ledger forever.
    ev = os.path.join(d, "tests", "demo", "agent.widget")
    os.makedirs(ev)
    with open(os.path.join(ev, "eval.yaml"), "w") as f:
        f.write("name: agent-widget\nstimuli:\n  - name: One\n    prompt: go\n    rubric:\n      - Did it\n")
    write_allowlist(d, "tests/demo/agent.widget/eval.yaml")


def commit(d, message):
    subprocess.run(["git", "add", "-A"], cwd=d, capture_output=True)
    subprocess.run(["git", "commit", "-qm", message], cwd=d, check=True)


def _seed_allowlist_on_a_base_commit(d):
    """Commit a below-floor eval + its exemption, so HEAD~1 is a real base ref."""
    drop_runs(d)
    write_allowlist(d, SPEC)
    commit(d, "grandfather the existing eval")


def allowlist_unchanged_since_base(d):
    _seed_allowlist_on_a_base_commit(d)
    # No further change: the ledger is identical to its base, which is allowed.


def new_exemption_added_since_base(d):
    _seed_allowlist_on_a_base_commit(d)
    # A second below-floor eval smuggled in together with its own exemption —
    # structurally identical to what the floor exists to prevent.
    ev = os.path.join(d, "tests", "demo", "gadget")
    os.makedirs(ev)
    with open(os.path.join(ev, "eval.yaml"), "w") as f:
        f.write("name: gadget\nstimuli:\n  - name: One\n    prompt: go\n    rubric:\n      - Did it\n")
    write_allowlist(d, SPEC, "tests/demo/gadget/eval.yaml")


def grandfathered_eval_renamed(d):
    # A pure rename carries no new debt. The allowlist is keyed on the eval
    # path, so without rename awareness this is unresolvable: the new path is
    # below the floor and unlisted, the old entry is stale, and listing the new
    # path looks like growth.
    _seed_allowlist_on_a_base_commit(d)
    old = os.path.join(d, "tests", "demo", "widget")
    new = os.path.join(d, "tests", "demo", "widget-renamed")
    subprocess.run(["git", "mv", old, new], cwd=d, check=True)
    write_allowlist(d, "tests/demo/widget-renamed/eval.yaml")


def unresolvable_base_ref(d):
    # A ref that doesn't resolve must fail loudly: silently skipping the
    # ratchet would leave a green build with the guarantee switched off.
    _seed_allowlist_on_a_base_commit(d)


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
    case("eval below the trial floor", underpowered, expect_fail=True),
    case("below the floor but grandfathered", underpowered_but_allowlisted, expect_fail=False),
    output_case("grandfathered warning reports scenarios x runs",
                grandfathered_reports_its_arithmetic, "1 trial(s) = 1 scenario(s) x runs=1"),
    case("stale exemption for an eval that now qualifies", allowlisted_eval_that_now_meets_the_floor, expect_fail=True),
    case("exemption for a spec that no longer exists", allowlist_entry_for_a_spec_that_does_not_exist, expect_fail=True),
    case("exemption for an agent.* eval that never needs one", agent_eval_exempted, expect_fail=True),
    case("runs lifts one scenario over the floor", runs_lifts_a_single_scenario_over_the_floor, expect_fail=False),
    case("ledger unchanged since its base", allowlist_unchanged_since_base,
         expect_fail=False, gate_args=("--base-ref", "HEAD")),
    case("new exemption added since the base ref", new_exemption_added_since_base,
         expect_fail=True, gate_args=("--base-ref", "HEAD")),
    case("grandfathered eval renamed, not newly exempted", grandfathered_eval_renamed,
         expect_fail=False, gate_args=("--base-ref", "HEAD")),
    case("base ref that does not resolve", unresolvable_base_ref,
         expect_fail=True, gate_args=("--base-ref", "origin/no-such-branch")),
]
print()
if all(results):
    print(f"All {len(results)} self-tests passed: the gate fires on every bug class and stays "
          f"quiet on well-formed input.")
else:
    print("SELF-TEST FAILURE — the gate does not behave as documented.")
raise SystemExit(0 if all(results) else 1)
