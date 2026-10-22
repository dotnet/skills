# Trusted helper regression tests

Run the offline fixture suite from the repository root:

```text
python agentic-workflows/unskip-closed-tests/tests/run_tests.py
```

The driver builds the `net8.0` helper, creates isolated checked-out Git fixture
repositories under `tests/.work`, uses only checked-in GitHub evidence, and removes
the fixture repositories after the run. No live GitHub access is required.

The fixture config supplies `verification.command` as an argv array. The helper
executes that array unchanged and appends exactly one final argument: the absolute
request JSON path. Request schema v1 contains exactly `schema_version`,
`candidate`, `repository`, `source_commit`, and `tests`; `candidate` contains
exactly `candidate_id`, and each test contains exactly `fqn`, `source_path`, and
absolute `result_file`.
