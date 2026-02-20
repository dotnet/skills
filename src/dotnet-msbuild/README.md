# dotnet-msbuild

Comprehensive MSBuild and .NET build skills: failure diagnosis, performance optimization, code quality, and modernization.

## 🔧 Skills

### Build Failure Troubleshooting

| Skill | Description |
|-------|-------------|
| [`common-build-errors`](skills/common-build-errors/) | Catalog of CS, MSB, NU, NETSDK errors with root causes and step-by-step fixes |
| [`nuget-restore-failures`](skills/nuget-restore-failures/) | NuGet restore diagnosis: feed auth, version conflicts, source mapping, lock files |
| [`sdk-workload-resolution`](skills/sdk-workload-resolution/) | SDK and workload resolution: global.json, roll-forward policies, workload management |
| [`multitarget-tfm-issues`](skills/multitarget-tfm-issues/) | TFM compatibility, multi-targeting setup, conditional compilation, RID issues |
| [`sourcegen-analyzer-failures`](skills/sourcegen-analyzer-failures/) | Source generator crashes (CS8785), analyzer exceptions (AD0001), debugging techniques |
| [`binlog-failure-analysis`](skills/binlog-failure-analysis/) | Binary log analysis for deep build failure diagnosis |
| [`binlog-generation`](skills/binlog-generation/) | Binary log generation conventions |

### Build Performance Optimization

| Skill | Description |
|-------|-------------|
| [`build-perf-baseline`](skills/build-perf-baseline/) | Performance baseline methodology: cold/warm/no-op measurement, MSBuild Server, graph builds, artifacts output |
| [`build-perf-diagnostics`](skills/build-perf-diagnostics/) | Performance bottleneck identification using binlog analysis |
| [`incremental-build`](skills/incremental-build/) | Incremental build optimization: Inputs/Outputs, FileWrites, up-to-date checks |
| [`build-parallelism`](skills/build-parallelism/) | Parallelism tuning: /maxcpucount, graph build, project dependency optimization |
| [`build-caching`](skills/build-caching/) | Build caching: NuGet cache, VBCSCompiler, deterministic builds, CI/CD strategies |
| [`eval-performance`](skills/eval-performance/) | Evaluation performance: glob optimization, import chain analysis |

### Code Quality & Modernization

| Skill | Description |
|-------|-------------|
| [`msbuild-style-guide`](skills/msbuild-style-guide/) | Best practices for idiomatic MSBuild: naming, conditions, targets, property functions |
| [`msbuild-antipatterns`](skills/msbuild-antipatterns/) | Anti-pattern catalog with detection rules, severity, and BAD→GOOD fixes |
| [`msbuild-modernization`](skills/msbuild-modernization/) | Legacy to SDK-style project migration with before/after examples |
| [`directory-build-organization`](skills/directory-build-organization/) | Directory.Build.props/targets/rsp organization and central package management |
| [`check-bin-obj-clash`](skills/check-bin-obj-clash/) | Output path conflict detection for multi-targeting and multi-project builds |
| [`including-generated-files`](skills/including-generated-files/) | Including build-generated files in MSBuild's build process |

### Other

| Skill | Description |
|-------|-------------|
| [`multithreaded-task-migration`](skills/multithreaded-task-migration/) | Thread-safe MSBuild task migration guide |

## 🤖 Agents

| Agent | Description |
|-------|-------------|
| [`msbuild`](agents/msbuild.agent.md) | General MSBuild expert — triages problems and routes to specialized skills/agents |
| [`build-perf`](agents/build-perf.agent.md) | Build performance analyst — runs builds, analyzes binlogs, suggests optimizations |
| [`msbuild-code-review`](agents/msbuild-code-review.agent.md) | Project file reviewer — scans .csproj/.props/.targets for anti-patterns and improvements |

## 📦 Distribution Templates

Ready-to-use templates that can be copied directly into your repository without installing a plugin:

| Template | Description |
|----------|-------------|
| [Agent Instructions](AGENTS.md) | Copy to repo root as `AGENTS.md` for cross-agent MSBuild guidance |
| [Prompt Files](prompts/) | Copy to `.github/prompts/` for VS Code Copilot Chat workflows |

## ⚡ Agentic Workflows

| Workflow | Description |
|----------|-------------|
| [`build-failure-analysis`](agentic-workflows/build-failure-analysis.md) | Analyzes build failures and posts diagnostic comments on PRs |
| [`msbuild-pr-review`](agentic-workflows/msbuild-pr-review.md) | Reviews MSBuild project file changes for best practices |
| [`build-perf-audit`](agentic-workflows/build-perf-audit.md) | Runs build performance audit and creates issues for regressions |

## 🧩 Copilot Extension

| Component | Description |
|-----------|-------------|
| [Copilot Extension](copilot-extension/) | Deployable `@msbuild` Copilot Extension for GitHub.com, VS Code, and Visual Studio |
