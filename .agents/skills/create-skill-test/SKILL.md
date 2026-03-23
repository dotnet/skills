---
name: create-skill-test
description: Scaffolds eval.yaml test files for agent skills in the dotnet/skills repository. Use when creating skill tests, writing evaluation scenarios, defining assertions and rubrics, or setting up test fixture files. Handles eval.yaml generation, fixture organization, and overfitting avoidance. Do not use for running or debugging existing tests nor for skills authoring.
---

# Create Skill Test

This skill helps you scaffold evaluation tests (`eval.yaml`) for agent skills, ensuring they conform to the dotnet/skills repository conventions, pass the skill-validator checks, and avoid common overfitting pitfalls.

## When to Use

- Creating a new `eval.yaml` test file for a skill
- Adding scenarios to an existing eval file
- Setting up test fixture files alongside eval definitions
- Reviewing whether rubric items and assertions risk overfitting

## When Not to Use

- Running or debugging existing tests (use the skill-validator directly)
- Modifying the skill-validator tool itself
- Creating or editing SKILL.md files (use the `create-skill` skill)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Skill name | Yes | The skill being tested (must match a skill under `plugins/<plugin>/skills/`) |
| Plugin name | Yes | The plugin the skill belongs to (e.g., `dotnet-msbuild`) |
| Skill content | Recommended | The SKILL.md content to understand what the skill teaches |
| Scenario descriptions | Recommended | What situations the agent should be tested on |

## Workflow

### Step 1: Locate the target and determine the test directory

Tests live at:

```
# For skills:
tests/<plugin>/<skill-name>/eval.yaml

# For agents (agent. prefix convention):
tests/<plugin>/agent.<agent-name>/eval.yaml
```

For skills, verify the skill exists at `plugins/<plugin>/skills/<skill-name>/SKILL.md`. For agents, verify the agent exists at `plugins/<plugin>/agents/<agent-name>.agent.md`. Read the target content to understand what it does -- this is critical for writing non-overfitted rubric items.

### Step 2: Create the test directory and eval.yaml

Create the directory and file:

```
# For skills:
tests/<plugin>/<skill-name>/
+-- eval.yaml

# For agents:
tests/<plugin>/agent.<agent-name>/
+-- eval.yaml
```

The `agent.` prefix disambiguates agent test directories from skill test directories that might share the same name.

### Step 3: Write scenarios

Each scenario needs a `name`, `prompt`, at least one `assertion`, and a `rubric`. Use this structure:

```yaml
scenarios:
  - name: "Descriptive scenario name"
    prompt: "Natural language task description as a developer would phrase it"
    setup:
      copy_test_files: true          # OR use inline files
    assertions:
      - type: "output_contains"
        value: "expected text"
    rubric:
      - "The agent correctly identified the root cause"
      - "The agent suggested a concrete, actionable fix"
    timeout: 120
```

#### Scenario guidelines

- **Name**: Describe *what* is being tested, not *how* (e.g., "Diagnose missing package reference" not "Test binlog replay and error extraction").
- **Prompt**: Write as a natural developer request. Never mention the skill name or instruct the agent to "use a skill." Neutral prompts prevent prompt overfitting.
- **Timeout**: Default is 120 seconds. Use 300-600 for scenarios requiring builds, benchmarks, or multi-step operations.

### Step 4: Configure setup

Choose one of three setup strategies:

#### Option A: Copy test files (recommended for complex fixtures)

Place fixture files alongside `eval.yaml` and enable auto-copy:

```yaml
setup:
  copy_test_files: true
```

All files in the directory (except `eval.yaml`) are copied into the agent's working directory.

#### Option B: Inline files (good for small, self-contained scenarios)

```yaml
setup:
  files:
    - path: "MyProject/MyProject.csproj"
      content: |
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
        </Project>
    - path: "MyProject/Program.cs"
      content: |
        Console.WriteLine("Hello");
```

#### Option C: Reference fixture files from a subdirectory

```yaml
setup:
  files:
    - path: "TestProject.csproj"
      source: "fixtures/scenario-a/TestProject.csproj"
```

Use this when multiple scenarios share a `fixtures/` directory with separate subdirectories.

#### Setup commands (optional)

Run shell commands before the agent starts (e.g., to build a project and generate artifacts):

```yaml
setup:
  copy_test_files: true
  commands:
    - "dotnet build -bl:build.binlog"
```

#### Scenario dependencies (optional)

Some agents route to specific skills, or some skills depend on sibling agents. In the **isolated** run, only the target is loaded — so the scenario must declare its dependencies using `additional_required_skills` and/or `additional_required_agents`:

```yaml
setup:
  copy_test_files: true
  additional_required_skills:
    - binlog-failure-analysis    # loaded in isolated run alongside the target
  additional_required_agents:
    - build-perf                 # registered in isolated run alongside the target
```

- Names are resolved from the same plugin's `skills/` or `agents/` directory.
- These only affect the **isolated** run. The **plugin** run already loads everything; the **baseline** loads nothing.
- Different scenarios of the same target can declare different dependencies (per-scenario granularity).
- If a declared name cannot be resolved, the validator fails with an error.

### Step 5: Write assertions

Assertions are hard pass/fail checks. Use them for objective, binary-verifiable criteria.

| Type | Required fields | Description |
|------|----------------|-------------|
| `output_contains` | `value` | Agent output contains text (case-insensitive) |
| `output_not_contains` | `value` | Agent output must NOT contain text |
| `output_matches` | `pattern` | Agent output matches regex |
| `output_not_matches` | `pattern` | Agent output does NOT match regex |
| `file_exists` | `path` | File matching glob exists in work dir |
| `file_not_exists` | `path` | No file matching glob exists |
| `file_contains` | `path`, `value` | File at glob path contains text |
| `file_not_contains` | `path`, `value` | File at glob path does NOT contain text |
| `exit_success` | -- | Agent produced non-empty output |

#### Assertion guidelines

- Prefer **broad** assertions that multiple valid approaches would satisfy.
- Avoid **narrow** assertions that gate on a specific syntax or flag the LLM already knows.
- Use `output_matches` with regex alternation for flexible matching: `"(root cause|primary error|underlying issue)"`.
- Use `file_contains` / `file_not_contains` to verify the agent modified files correctly.
- Use `output_not_contains` and `file_not_exists` to verify the agent avoided incorrect actions.

### Step 6: Write rubric items

Rubric items are evaluated by an LLM judge using pairwise comparison (baseline vs. skill-enhanced). Quality metrics (rubric-based at 40% weight plus overall judgment at 30%) together dominate the composite improvement score.

#### The three rubric classifications (and how to stay in "outcome")

The overfitting judge classifies each rubric item:

| Classification | Description | Goal |
|---------------|-------------|------|
| **outcome** | Tests whether the agent reached a correct result. Describes WHAT, not HOW. | Target this |
| **technique** | Tests whether the agent used a skill-specific procedure. | Minimize |
| **vocabulary** | Tests whether the agent used specific terminology from the skill. | Avoid |

#### Rubric writing rules

1. **Test outcomes, not methods.** Write "Identified the root cause of the build failure" -- not "Replayed the binlog using `dotnet build /flp`."
2. **Allow alternative approaches.** If multiple valid solutions exist, the rubric item should accept any of them.
3. **Never reference the skill by name** or use phrasing copied directly from the SKILL.md.
4. **Don't test pre-existing LLM knowledge.** If the LLM already knows something (common APIs, standard syntax, basic escaping), testing for it adds no signal.
5. **Test findings, not diagnostic steps.** Write "Correctly determined that the root cause is a missing PackageReference" -- not "Used `dotnet restore` to check package resolution."
6. **Each item should be independently evaluable.** Avoid compound items that test multiple things.

#### Examples

**Well-designed (outcome-focused):**
```yaml
rubric:
  - "Correctly identified the missing NuGet package as the root cause of the build failure"
  - "Recognized that downstream project failures were cascading from the root cause, not independent errors"
  - "Suggested a concrete fix that would resolve the root cause"
```

**Overfitted (vocabulary/technique):**
```yaml
rubric:
  - "Replayed the binary log using 'dotnet build /flp:v=diag'"      # technique: gates on specific command
  - "Measured cold, warm, and no-op build scenarios"                  # vocabulary: uses skill's labels
  - "Used the --clreventlevel flag with dotnet trace collect"         # vocabulary: gates on specific flag
```

### Step 7: Add optional constraints

```yaml
expect_tools: ["bash"]           # Agent must use these tools
reject_tools: ["create_file"]    # Agent must NOT use these tools
max_turns: 10                    # Maximum agent iterations
max_tokens: 5000                 # Maximum token budget
```

Use constraints sparingly -- only when the scenario specifically requires or forbids certain agent behaviors.

### Step 8: Add non-activation scenarios with `expect_activation: false`

Many skills have clear boundaries -- situations where the skill should recognize it does not apply and decline gracefully. Test these boundaries using `expect_activation: false`.

#### How `expect_activation: false` works

When a scenario has `expect_activation: false`:

1. **All three runs still execute** (baseline, skilled-isolated, skilled-plugin) and assertions are evaluated on each. The flag does not change which runs are performed.
2. **Activation verdict is inverted** -- if the skill is not activated for this prompt, the evaluator reports it as `[Info] not activated (expected)` instead of treating it as a failure.
3. **The scenario is excluded from the noise test** -- the multi-skill activation test only runs positive (`expect_activation: true`) scenarios.

#### When to use non-activation scenarios

Add `expect_activation: false` scenarios when the skill has explicit "When Not to Use" boundaries. Common patterns:

| Pattern | Example |
|---------|---------|
| **Wrong input format** | Skill handles Android tombstones; scenario provides an iOS crash log |
| **Out-of-scope request** | Skill collects dumps; scenario asks to *analyze* a dump |
| **Incompatible project type** | Skill converts PackageReference to CPM; scenario has packages.config |
| **Wrong framework version** | Skill migrates .NET 8 to 9; scenario provides a .NET 8 app and asks for .NET 10 migration |
| **Prerequisite not met** | Skill requires a specific file format that isn't present |

#### Example: Wrong input format

```yaml
- name: "Reject iOS crash log as wrong format"
  prompt: "I have a crash log file at crashlog_ios.txt from a crashed app. Please symbolicate the .NET runtime frames."
  expect_activation: false
  setup:
    copy_test_files: true
  assertions:
    - type: "output_matches"
      pattern: "(iOS|Apple|not.*(Android|tombstone)|wrong.*(format|type))"
  rubric:
    - "Recognized that this is an iOS crash log, not an Android tombstone"
    - "Did NOT attempt to apply the Android tombstone symbolication workflow"
    - "Explained that iOS crash logs require a different symbolication process"
```

#### Example: Out-of-scope request

```yaml
- name: "Decline dump analysis request"
  prompt: |
    I already have a .dmp crash dump file from my .NET app. Can you help
    me analyze it to find the root cause of the crash?
  expect_activation: false
  assertions:
    - type: "output_matches"
      pattern: "(out of scope|not cover|does not|cannot|only.*collect)"
  rubric:
    - "Clearly states that dump analysis is out of scope for this skill"
    - "Does not attempt to open or analyze the dump file"
    - "Does not install analysis tools like dotnet-dump analyze, lldb, or windbg"
  timeout: 30
```

#### Example: Incompatible project type

```yaml
- name: "Decline CPM conversion for packages.config project"
  prompt: "Convert my simple-packages-config/LegacyApp project to Central Package Management."
  expect_activation: false
  setup:
    copy_test_files: true
  assertions:
    - type: "output_contains"
      value: "packages.config"
    - type: "file_not_exists"
      path: "simple-packages-config/Directory.Packages.props"
  rubric:
    - "Detected the project uses packages.config instead of PackageReference format"
    - "Informed the user that CPM requires PackageReference and cannot be applied to packages.config projects"
    - "Suggested migrating from packages.config to PackageReference first"
    - "Did not attempt to create Directory.Packages.props or modify any project files"
```

#### Rubric guidelines for non-activation scenarios

Non-activation rubric items typically verify three things:

1. **Recognition** -- The agent identified *why* the skill doesn't apply.
2. **Restraint** -- The agent did NOT attempt the skill's workflow (no file modifications, no tool installs).
3. **Redirection** -- The agent suggested the correct alternative approach or next step.

### Step 9: Validate the eval.yaml

Run the static validator:

```bash
dotnet run --project eng/skill-validator/src/SkillValidator.csproj -- check --plugin ./plugins/<plugin>
```

Then run evaluation (at least 3 runs for reliable results):

```bash
# For skills:
dotnet run --project eng/skill-validator/src/SkillValidator.csproj -- evaluate \
  --runs 3 \
  --tests-dir tests/<plugin> \
  plugins/<plugin>/skills/<skill-name>

# For agents:
dotnet run --project eng/skill-validator/src/SkillValidator.csproj -- evaluate \
  --runs 3 \
  --tests-dir tests/<plugin> \
  plugins/<plugin>/agents/<agent-name>.agent.md
```

## eval.yaml Template

```yaml
scenarios:
  - name: "<Describe what the agent should accomplish>"
    prompt: "<Natural developer request -- do not mention the skill>"
    setup:
      copy_test_files: true
    assertions:
      - type: "output_contains"
        value: "<key term that a correct response must include>"
      - type: "exit_success"
    rubric:
      - "<Outcome: what the agent should have identified or produced>"
      - "<Outcome: what fix or recommendation the agent should have given>"
      - "<Outcome: what incorrect approach the agent should have avoided>"
    timeout: 120

  - name: "<Describe situation where the skill should NOT apply>"
    prompt: "<Request that superficially matches the skill but falls outside its scope>"
    expect_activation: false
    setup:
      copy_test_files: true
    assertions:
      - type: "output_matches"
        pattern: "<pattern matching the agent's explanation of why it cannot help>"
      - type: "file_not_exists"
        path: "<file the skill would create if it incorrectly activated>"
    rubric:
      - "<Recognition: agent identified why the skill does not apply>"
      - "<Restraint: agent did not attempt the skill's workflow>"
      - "<Redirection: agent suggested the correct alternative>"
    timeout: 120
```

## Validation Checklist

After creating a test, verify:

- [ ] Test directory matches `tests/<plugin>/<skill-name>/` for skills or `tests/<plugin>/agent.<agent-name>/` for agents
- [ ] Target exists at `plugins/<plugin>/skills/<skill-name>/SKILL.md` (skill) or `plugins/<plugin>/agents/<agent-name>.agent.md` (agent)
- [ ] Every scenario has `name`, `prompt`, at least one assertion, and rubric items
- [ ] Prompts are written as natural developer requests (no skill/agent name references)
- [ ] Assertions are broad enough that multiple valid approaches pass
- [ ] Rubric items test outcomes, not specific techniques or vocabulary
- [ ] Fixture files are present when `copy_test_files: true` is used
- [ ] `source` paths in setup files point to existing fixture files
- [ ] `additional_required_skills`/`additional_required_agents` names exist in the same plugin
- [ ] Timeouts are reasonable for the scenario complexity
- [ ] Non-activation scenarios use `expect_activation: false` and verify recognition, restraint, and redirection
- [ ] `dotnet run --project eng/skill-validator/src/SkillValidator.csproj -- check` passes

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Prompt mentions the skill by name | Rewrite as a natural developer request describing the problem |
| Prompt mentions the agent by name | Same as above — agent name in prompts biases the baseline |
| Rubric tests a specific diagnostic command | Rewrite to test the finding or outcome that command produces |
| Assertion gates on syntax the LLM already knows | Use a broader pattern or test the result instead |
| All rubric items test the same aspect | Diversify: test identification, fix quality, and error avoidance |
| Missing fixture files for `copy_test_files` | Add the required project/source files alongside eval.yaml |
| Timeout too short for builds | Use 300-600s for scenarios that compile or run benchmarks |
| Single scenario covers the entire skill | Break into focused scenarios testing different aspects |
| Compound rubric items testing multiple things | Split into separate, independently-evaluable items |
| No non-activation scenarios for skill with clear boundaries | Add `expect_activation: false` scenarios for each "When Not to Use" case |
| Agent test missing `additional_required_skills` | If the agent routes to specific skills, declare them so the isolated run loads them |
