# Contributing

Thanks for your interest in contributing. We expect to accept external contributions, but the bar for merging is intentionally high.

This repository contains shared building blocks for coding agents:

- Skills: reusable, task focused instruction packs
- Agents: role based configurations that bundle tool expectations and skill selection

Because these artifacts can affect many users and workflows, we prioritize correctness, clarity, and long term maintainability over speed.

## Code ownership

Every plugin, skill, agent, and agentic workflow must have designated owners in the `.github/CODEOWNERS` file. When you add a new skill, agent, or workflow, add a matching CODEOWNERS entry. Ownership must be either:

- **Two or more FTE GitHub aliases** (e.g., `@user1 @user2`), or
- **A GitHub team alias** (e.g., `@dotnet/my-team`)

This ensures that every contribution area has accountable reviewers and that PRs are automatically routed to the right people.

## Repository layout

```text
plugins/
  <plugin>/
    plugin.json
    skills/
      <skill-name>/
        SKILL.md
        scripts/
        references/
        assets/
    agents/
      <agent-name>.agent.md
tests/
  <plugin>/
    <skill-name>/
      eval.yaml
      <fixture files>
agentic-workflows/
  <plugin>/
```

Every plugin must have a plugin.json file in the plugin root that is linked to from the marketplace.json file.

### Plugin organization

Skills are grouped into domain-specific plugins. When proposing a new skill, place it in the plugin that best matches its domain:

| Plugin | Domain |
|--------|--------|
| `dotnet` | Common everyday C#/.NET coding tasks useful to all .NET developers |
| `dotnet-upgrade` | Migrating and upgrading .NET projects across framework versions, language features, and compatibility targets |
| `dotnet-diag` | Performance investigations, debugging, and incident analysis |
| `dotnet-data` | Data access and Entity Framework |
| `dotnet-msbuild` | MSBuild and project system |

If your skill does not fit any existing plugin, consider creating a new one. The following plugin names are reserved for future use and are good candidates for new skills in those areas:

- `dotnet-aspnet` — ASP.NET
- `dotnet-wpf` — WPF
- `dotnet-winforms` — Windows Forms (WinForms)
- `dotnet-maui` — .NET MAUI

To create a new plugin:

1. Add `plugins/<plugin-name>/plugin.json` and a `skills/` directory beneath it.
2. Add a matching entry in both `.github/plugin/marketplace.json` and `.claude-plugin/marketplace.json`. The `.claude-plugin/marketplace.json` file must remain an exact copy of `.github/plugin/marketplace.json`, so any change to one file (adding, removing, or editing a plugin entry) must be applied to the other in the same way.
3. Add a CODEOWNERS entry for the new plugin and its tests (see [Code ownership](#code-ownership)).
4. Add the plugin to the **What's Included** table in the root `README.md`.
5. Create a `tests/<plugin-name>/` directory for skill tests.

See existing plugins for the expected format.

## Before you start

- Search existing issues and pull requests to avoid duplicates.
- Start with an issue before you submit a pull request for a new skill, a new agent, or any non trivial change. This helps us align on scope and avoids wasted work.
- Small fixes like typos, broken links, or clearly isolated corrections can go straight to a pull request.
- Keep changes small and focused. One skill or one agent per pull request is a good default.

## What we look for

We are most likely to accept contributions that are:

- Narrow in scope and easy to review
- Clearly motivated by a real use case
- Tool conscious and explicit about assumptions
- Verifiable with concrete validation steps
- Written to be durable across repo changes

We are less likely to accept contributions that:

- Add broad frameworks, meta tooling, or large reorganizations
- Duplicate guidance that already exists in another skill
- Encode private environment details, credentials, or company specific secrets
- Depend on proprietary tools or access that most contributors will not have
- Skills that make use of third party tools will be evaluated on a case by case basis. Acceptance of such skills will depend on our evaluation of the provenance and maturity of any such tools.

## Proposing a new skill

A skill should be self-contained and:

- Clearly state **what it does** and **when to use it**.
- Specify required inputs (repo context, environment, access needs).
- Prefer concrete checklists and verification steps over vague guidance.

Create a new folder under a plugin's `skills/` directory:

```text
plugins/<plugin>/skills/<skill-name>/SKILL.md
```

A skill should answer three questions up front:

1. What outcome does the skill produce
2. When should an agent use it
3. How does the agent validate success

### Skill naming

Use short, kebab-case names that mirror how developers naturally phrase the task, prioritizing keyword overlap over grammar — e.g., add-aspnet-auth, configure-jwt-auth, setup-identity-server. Optionally using gerund style (verb-ing) is acceptable as well - e.g., configuring-caching.

Optimize for intent matching: lead with the action verb users actually say (add, configure, setup, deploy) followed the outcome the skill is aiming to assist.

The `SKILL.md` is required to have front-matter at a minimum:

Create the file with required YAML frontmatter:

```yaml
---
name: <skill-name>
description: <description of what the skill does, when to use it, and when not to use it>
---
```

> **Tip:** The `description` field is used by the agent runtime to decide whether to load the full skill.
> Include **when to use** and **when not to use** guidance directly in the description so the agent can
> select or skip skills without reading the entire `SKILL.md`. This avoids unnecessary token usage.
> See [`thread-abort-migration/SKILL.md`](plugins/dotnet-upgrade/skills/thread-abort-migration/SKILL.md) for a good example.

### Recommended `SKILL.md` sections

- **Purpose**: one paragraph describing the outcome.
- **When to use** / **When not to use** (put the essentials in the frontmatter `description`; expand here only if more detail is needed).
- **Inputs**: what the agent needs (files, commands, permissions).
- **Workflow**: numbered steps with checkpoints.
- **Validation**: how to confirm the result (tests, linters, manual checks).
- **Common pitfalls**: known traps and how to avoid them.

### Skill checklist

Include a `SKILL.md` that covers:

- Purpose and non goals
- When to use and when not to use (summarized in the frontmatter `description`; body section for extended detail)
- Inputs and prerequisites
- Step by step workflow with checkpoints
- Validation steps that can be run or observed
- Failure modes and recovery guidance

Also:

- Avoid duplicating text across multiple skills. Prefer referencing shared patterns.
- Do not include content copied from other repositories. If you are inspired by existing work, rewrite in your own words and adapt it to our conventions.

## Proposing a new agent

An agent definition should be opinionated but bounded:

- Describe the **role** (e.g., "WinForms Expert", "Security Reviewer", "Docs Maintainer").
- Define boundaries (what the agent should not do).
- List the skills it expects to use and how it chooses among them.

Add an agent file under a plugin's `agents/` directory:

```text
plugins/<plugin>/agents/<agent-name>.agent.md
```

### Agent checklist

Include documentation that explains:

- Role and intended tasks
- Boundaries and safety constraints
- Tooling assumptions
- How the agent chooses which skills to apply
- What a good completion looks like, including validation expectations

## Testing and validation

Skills and agents are documentation driven, but we still treat them as production assets.

- Every change should include a validation section that a reviewer can follow.
- If your change references commands, keep them cross platform when practical. If not, state the supported environment.
- If your change depends on external services, document how a reviewer can validate without privileged access, or explain why validation is not possible.

### Writing skill tests

Each skill should have an `eval.yaml` file that defines test scenarios. Tests live under the repo root `tests/` directory, matching the plugin and skill name:

```text
tests/<plugin>/<skill-name>/eval.yaml
```

A minimal eval file:

```yaml
scenarios:
  - name: "Describe what the agent should do"
    prompt: "The prompt sent to the agent"
    assertions:
      - type: "output_contains"
        value: "expected text in agent output"
    rubric:
      - "The agent correctly identified the issue"
      - "The agent suggested a concrete fix"
    timeout: 120
```

#### Test fixture files

If a scenario requires files in the agent's working directory (e.g. `.csproj`, `.sln`, `.cs` files), place them alongside `eval.yaml` and opt into auto-copy:

```text
tests/<plugin>/<skill-name>/
  eval.yaml
  MyProject.csproj
  Program.cs
```

```yaml
scenarios:
  - name: "Diagnose build failure"
    prompt: "Why does this project fail to build?"
    setup:
      copy_test_files: true    # copies MyProject.csproj, Program.cs into work dir
    assertions:
      - type: "output_matches"
        pattern: "CS\\d{4}"
```

You can also create files inline or reference files from the skill directory:

```yaml
setup:
  files:
    - path: "input.txt"
      content: "inline file content"
    - path: "data.csv"
      source: "fixtures/sample-data.csv"  # relative to skill directory
```

See the [skill-validator README](eng/skill-validator/README.md) for the full list of assertion types, constraints, and rubric options.

### Running tests locally

Prerequisites: .NET 10 SDK or later and `gh auth login`.

```bash
# Run tests for a single plugin
dotnet run --project eng/skill-validator/src/SkillValidator.csproj --tests-dir tests/dotnet-msbuild plugins/dotnet-msbuild/skills

# Run tests for a single skill (pass the skill directory directly)
dotnet run --project eng/skill-validator/src/SkillValidator.csproj --tests-dir tests/dotnet-msbuild plugins/dotnet-msbuild/skills/common-build-errors

# Fewer runs for faster iteration (default is 5)
dotnet run --project eng/skill-validator/src/SkillValidator.csproj --runs 3 --tests-dir tests/dotnet-msbuild plugins/dotnet-msbuild/skills

# Use a specific model
dotnet run --project eng/skill-validator/src/SkillValidator.csproj --model claude-opus-4.6 --tests-dir tests/dotnet-msbuild plugins/dotnet-msbuild/skills

# Run with verbose logging
dotnet run --project eng/skill-validator/src/SkillValidator.csproj --tests-dir tests/dotnet-msbuild plugins/dotnet-msbuild/skills --verbose
```

> [!WARNING]  
> If you share the results in a Pull Request, make sure to have `--runs` configured to at least 3 but better 5 for reliable results.

### CI evaluation

Tests run automatically on pull requests that modify files under `plugins/`. The evaluation workflow discovers changed plugins and runs the skill-validator for each one. Results are posted as a PR comment and uploaded as build artifacts.

## Writing style

- Be concise and specific.
- Prefer numbered steps for workflows.
- Prefer checklists for requirements.
- Define terminology the first time it appears.
- Avoid excessive formatting and avoid clever wording that could be misread by an agent.

## Security and safety

- Do not include secrets, tokens, or internal URLs.
- If you discover a security issue, do not open a public issue with sensitive details. Use the repository or organization security reporting process instead.

## Review process

Maintainers may request changes for:

- Clarity and unambiguous instructions
- Reduced scope
- More explicit validation
- Compatibility with multiple agent runtimes
- Consistency with existing conventions

We may close pull requests that are out of scope or too large to review. If that happens, we are happy to suggest a smaller path forward.

## Licensing and provenance

Only submit content that you have the right to contribute.

- Do not include copyrighted text from other projects.
- You may be asked to confirm that your contribution is original or appropriately licensed.

## Getting help

If you are unsure where a change belongs or how to structure a skill or agent, open an issue describing:

- The user problem
- The proposed outcome
- A small example of the desired behavior

If you're not sure whether something belongs under `skills/` or `agents/`, a good rule of thumb is:

- Put **reusable task playbooks** in `skills/`.
- Put **role + operating model** in `agents/`.

## Quality bar

Skills and agents in this repo should be:

- **Actionable**: the agent can follow them without guesswork.
- **Minimal**: no extra features or scope creep; focus on the task.
- **Verifiable**: always include a way to validate success.
- **Tool-conscious**: don't assume capabilities that might not exist in every runtime.

## Skill-Validator & Evaluation workflow

Changes to `eng/skill-validator` or the `.github/workflows/evaluation*.yml` workflows must be made from a branch in the `dotnet/skills` repository (i.e., not from a fork). This is a security measure.
For pull requests from forks, the evaluation workflow (triggered via `/evaluate`) always uses the workflow YAML from the default branch of `dotnet/skills` and builds the validator from that default-branch checkout, so any changes to these files in the forked PR will be ignored during evaluation.
