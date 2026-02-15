
# convert-to-cpm Test Scenarios

Each subfolder contains a test scenario with a `README.md` describing the setup and prompt, a `without-skill.md` showing typical agent output without the skill, and a `with-skill.md` showing expected output with the skill loaded.

The skill is considered successful if the output looks like `without-skill.md` without the skill loaded, and like `with-skill.md` with the skill loaded. If the output looks like `with-skill.md` without the skill, the skill is considered ineffective. If the output looks like `without-skill.md` with the skill loaded, the skill is considered incorrect.

## Simple

Straightforward conversions where all package versions are explicit literals, there are no version conflicts, and no MSBuild properties are involved. The agent should be able to complete the conversion without asking the user any questions beyond confirming the scope.

- **[simple-single-project](simple-single-project/)** — One project with a few inline-versioned PackageReference items
- **[simple-solution](simple-solution/)** — A solution with three projects, all packages at the same versions
- **[simple-packages-config](simple-packages-config/)** — A project using `packages.config` instead of `PackageReference` (agent should decline conversion)

## Moderate

Conversions that involve one complicating factor requiring user input. The agent should identify the issue, present options, and ask the user to decide before proceeding.

- **[moderate-version-conflicts](moderate-version-conflicts/)** — Same package appears at different versions across projects
- **[moderate-msbuild-properties](moderate-msbuild-properties/)** — Package versions defined via MSBuild properties in Directory.Build.props

## Advanced

Conversions with multiple overlapping complexities. The agent must audit several issues, present them clearly, and handle each one with separate user interactions. These scenarios test the agent's ability to maintain a coherent conversion plan across interrelated concerns.

- **[advanced-multi-complexity](advanced-multi-complexity/)** — Version conflicts, MSBuild properties, conditional PackageReference items, and references in shared .props files all in one repository

## Generating test solutions

Each scenario's `README.md` describes the file structure and project contents. The `src/` directories that contain buildable .NET solutions are git-ignored and must be generated locally. Use the following prompt to create them:

> Read each scenario README under tests/convert-to-cpm/ and generate a buildable .NET solution into a `src/` folder within each scenario directory, matching the described project structure and package references. Verify each solution restores and builds successfully.

## Evaluating the skill

After generating the test solutions, evaluate the skill by running it against each scenario. Use the following prompt:

> Load the convert-to-cpm skill and evaluate it against each test scenario under tests/convert-to-cpm/. For each scenario, use the `src/` directory as the working context and the scenario's `README.md` input prompt. Compare the agent's output against the `with-skill.md` (expected behavior) and `without-skill.md` (baseline behavior without the skill). Report any discrepancies in workflow steps, package versions, user interactions, or comparison table format.
