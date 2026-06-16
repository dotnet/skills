---
name: authoring-github-workflows
description: "Author and review GitHub Actions workflow YAML safely so syntactically-valid YAML can't ship a workflow that GitHub Actions refuses to run. USE FOR: editing, adding, or reviewing any file under .github/workflows/, writing run-name/name/if/env/run values that contain ${{ }} expressions, diagnosing a run that fails with 'This run likely failed because of a workflow file issue' and no jobs starting, deciding when a workflow scalar must be quoted, validating workflows with actionlint. DO NOT USE FOR: authoring application YAML unrelated to GitHub Actions, Azure Pipelines, GitLab CI, or non-workflow YAML. SCOPE: this skill covers *syntactic/structural* correctness of workflow YAML (quoting, parsing, actionlint); for *semantic and functional* workflow design (what a workflow should do, agentic-workflow behavior), see .github/agents/agentic-workflows.agent.md — the two are complementary. INVOKES: actionlint (downloaded pinned binary) plus git/grep for inspection."
license: MIT
---

# Authoring GitHub Actions Workflows Safely

GitHub Actions workflow files are YAML, but **valid YAML is not the same as a valid workflow**. A workflow can parse cleanly with `yaml.safe_load` (or a casual review) yet still be rejected by GitHub Actions at load time — producing the opaque failure *"This run likely failed because of a workflow file issue"* with **zero jobs started**. This skill teaches the YAML-vs-Actions traps (the `#`-as-comment trap above all), how to quote expression scalars correctly, and how to validate with `actionlint` before merge.

> **Scope: syntactic vs. semantic.** This skill is about the *syntactic and structural* correctness of workflow YAML — quoting, parsing, and `actionlint`-level validity that determines whether GitHub Actions will load and run a file at all. It is **not** about *what* a workflow should do or how an agentic workflow should behave. For *semantic and functional* guidance (designing workflow logic, agentic-workflow patterns, gh-aw authoring), use [`.github/agents/agentic-workflows.agent.md`](../../../.github/agents/agentic-workflows.agent.md). The two are complementary: get the behavior right with the agent, get the YAML right with this skill.

## When to Use

- Editing, adding, or reviewing any file under `.github/workflows/`.
- Writing a `run-name`, `name`, `if`, `env`, `with`, or `run` value that embeds a `${{ }}` expression.
- A workflow run failed with *"This run likely failed because of a workflow file issue"* and **no jobs ran**.
- Eval/CI on `main` suddenly breaks for every run after a workflow edit merged, even though the change "looked fine."
- Deciding whether a YAML scalar needs quoting.

## When Not to Use

- Authoring non-Actions YAML (app config, Kubernetes, Compose, Azure Pipelines, GitLab CI).
- Pure shell/script logic inside an already-valid `run:` block (that is a scripting task, not a workflow-syntax task).

## The #1 Trap: `#` inside an unquoted expression becomes a YAML comment

In YAML, a space followed by `#` starts a **comment**. In an unquoted (plain) scalar, everything from that space-then-`#` to end-of-line is silently discarded:

```yaml
# BAD — the run-name is silently truncated at " #"
run-name: ${{ inputs.pr_number != '' && format('Evaluate PR #{0} @ {1}', inputs.pr_number, inputs.head_sha) || '' }}
```

YAML parses this as `run-name: ${{ inputs.pr_number != '' && format('Evaluate PR` — an **unterminated `${{` expression**. `yaml.safe_load` succeeds (it just sees a truncated string with a trailing comment), so the bug passes naive validation, but GitHub Actions rejects the malformed expression and refuses to start any run.

```yaml
# GOOD — wrap the whole value in double quotes so '#' stays inside the scalar
run-name: "${{ inputs.pr_number != '' && format('Evaluate PR #{0} @ {1}', inputs.pr_number, inputs.head_sha) || '' }}"
```

The inner expression already uses single quotes, so double-quoting the scalar is safe. This is exactly the bug that broke `dotnet/skills` evaluation on `main` (PR #746 → fixed by quoting).

## Other characters that force quoting in a plain scalar

| Character / pattern | Why it breaks | Fix |
|---------------------|---------------|-----|
| space then `#` (space-hash) | Starts a YAML comment; truncates the value | Quote the whole value |
| Leading `*`, `&`, `!`, `?`, `\|`, `>`, `@`, `` ` `` | YAML anchors/aliases/tags/block scalars | Quote the value |
| Leading `{` or `[` | Parsed as flow mapping/sequence (a bare `${{ }}` starts with `$`, which is safe, but `{{` after a leading char is risky) | Quote the value |
| `:` then space (colon-space) inside the value | Parsed as a nested mapping key | Quote the value |
| Leading/trailing spaces that matter | Plain scalars strip them | Quote the value |
| Values that are `true`/`false`/`yes`/`no`/`on`/`off`/numbers but must stay strings | YAML type coercion | Quote the value |

**Rule of thumb:** if a `name`, `run-name`, `if`, `env`, or `with` value contains a `${{ }}` expression *and* any literal `#`, `:`, or leading special character, **wrap the entire scalar in double quotes**.

## Workflow

### Step 1: Identify the changed/authored workflow files

```bash
git diff --name-only origin/main... -- .github/workflows/
```

For each file, scan every line that contains `${{` together with a `#`, a colon-space, or a leading special character.

### Step 2: Quote risky expression scalars

Wrap the full value in double quotes when the value embeds an expression and contains a `#` or other special character (see the table above). Prefer double quotes when the inner expression uses single quotes, and vice-versa. Do **not** escape the `${{ }}` braces — quoting the scalar is enough.

### Step 3: Validate with actionlint (authoritative)

`actionlint` understands the GitHub Actions schema *and* the expression grammar, so it catches exactly this class of bug that plain YAML linters miss. Download a pinned release and run it:

```bash
ACTIONLINT_VERSION=1.7.7
ACTIONLINT_SHA256=023070a287cd8cccd71515fedc843f1985bf96c436b7effaecce67290e7e0757
curl -fsSLo actionlint.tar.gz \
  "https://github.com/rhysd/actionlint/releases/download/v${ACTIONLINT_VERSION}/actionlint_${ACTIONLINT_VERSION}_linux_amd64.tar.gz"
# Verify the download against the pinned checksum before extracting/executing it:
echo "${ACTIONLINT_SHA256}  actionlint.tar.gz" | sha256sum -c -
tar -xzf actionlint.tar.gz actionlint
# Focus on workflow/expression correctness; silence shell/py style noise:
./actionlint -shellcheck= -pyflakes= -color .github/workflows/*.yml
```

On Windows PowerShell, use the `actionlint_<ver>_windows_amd64.zip` asset and `Expand-Archive`.

The truncated-expression bug surfaces as:

```
got unexpected EOF while lexing end of string literal, expecting ''' [expression]
```

A clean exit code `0` means the workflows are structurally valid.

### Step 4: Confirm a YAML-only check is not enough

Do **not** rely on `yaml.safe_load`, `yamllint`, or "it parses" as proof. They accept the truncated-comment form. Only `actionlint` (or pushing and watching GitHub Actions parse it) validates the Actions layer.

### Step 5: Keep the CI gate green

This repository runs `actionlint` automatically (see `.github/workflows/actionlint.yml`) on any PR that touches `.github/workflows/`. Ensure your change passes that check before requesting review. If you add a new workflow, the gate covers it automatically.

## Validation

- [ ] Every `${{ }}` value containing `#`, a colon-space, or a leading special character is wrapped in quotes.
- [ ] `actionlint -shellcheck= -pyflakes= .github/workflows/*.yml` exits `0`.
- [ ] No workflow run reports *"This run likely failed because of a workflow file issue"*.
- [ ] The `actionlint` CI check is green on the PR.

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Unquoted `run-name`/`name` with `#` inside the expression | Wrap the whole value in double quotes |
| Trusting `yaml.safe_load`/`yamllint`/a code review to catch it | Run `actionlint`; YAML-only checks accept the truncated form |
| Escaping `${{` braces to "fix" it | Don't — quote the scalar instead; escaping breaks the expression |
| Using single quotes around a value that contains single quotes | Use double quotes for the outer scalar |
| Adding `actionlint` with shellcheck enabled and drowning in pre-existing shell-style warnings | Run with `-shellcheck= -pyflakes=` to focus on workflow/expression errors |
| Assuming a green YAML lint means the workflow will run | Push and confirm jobs actually start, or rely on the actionlint gate |

## References

- [actionlint](https://github.com/rhysd/actionlint) — static checker for GitHub Actions workflows.
- [GitHub Actions: workflow syntax](https://docs.github.com/actions/using-workflows/workflow-syntax-for-github-actions)
- [YAML 1.2 spec — comments](https://yaml.org/spec/1.2.2/#66-comments)
- Repository skill-authoring guide: [`.agents/skills/create-skill/SKILL.md`](../create-skill/SKILL.md)
