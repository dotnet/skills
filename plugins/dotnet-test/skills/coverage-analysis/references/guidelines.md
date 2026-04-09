# Guidelines

**Don't modify source or production code.** The only permitted project file modifications are adding a coverage provider package to test projects that currently have no provider: `coverlet.collector` (coverlet/mixed modes) or `Microsoft.Testing.Extensions.CodeCoverage` (ms-codecoverage mode). Do not add a second provider to projects that already have one. Always log package additions and document revert commands in the report. Write all other output to `TestResults/coverage-analysis/` under the test project directory.

**Always show and open the generated markdown report.** After writing `TestResults/coverage-analysis/coverage-analysis.md`, print its contents to the console and open the file in the current host editor/session automatically (when an editor is available).

**Don't generate new tests during the initial analysis run.** This skill surfaces where tests are needed. Test generation is a separate follow-up step outside the scope of this skill.

**Use inline `dotnet test` arguments, not runsettings files.** Runsettings files require the developer to already know what they're doing — the whole point of this skill is that they shouldn't have to. Inline data collector args produce the same result with zero configuration.

**Show the risk hotspots table even when all thresholds pass.** A project at 90% line coverage can still have a method with cyclomatic complexity 20 and 0% branch coverage. The thresholds measure averages; the hotspot table finds outliers. Don't hide it just because the summary looks green.

**Always compute and surface CRAP scores.** The Risk Hotspots table is mandatory in every analysis output, whether analyzing pre-existing data, freshly collected data, or diagnosing a plateau. Never skip CRAP score computation — it is the primary differentiator between this skill and raw `dotnet test` coverage output.

**Continue past test failures (exit code 1).** If some tests fail, coverage is still collected from the passing tests — partial data is better than no data. Note the failures in the summary and proceed. Aborting would leave the developer with nothing actionable.

**Run `dotnet test` only once per entry point during normal flow.** When a solution is found, run it once against the solution. When no solution is found, run it once per test project. A single recovery rerun is allowed only if the first run produced no Cobertura XML and only `.coverage` binary output.

**CRAP threshold of 30 is the default for a reason.** Scores above 30 are widely cited (by the original researchers) as "needs immediate attention." Scores between 15 and 30 are moderate — flag them in the table but don't make them sound catastrophic. Scores ≤ 5 are generally fine.

**Priority assignment for coverage gaps:**
- **HIGH** — file has both a CRAP score above threshold AND coverage below threshold (the double failure is what makes it urgent)
- **MED** — coverage below threshold OR CRAP score above threshold, but not both
- **LOW** — coverage below threshold with all methods having complexity ≤ 2 (trivial code — missing coverage here is unlikely to hide real bugs)

---

# Coverage Intelligence — Going Beyond the Numbers

**Prioritize uncovered code that is** complex (cyclomatic complexity > 5), on critical paths (auth, payment, data access, error handling), or changed frequently. **Deprioritize** trivial getters (complexity 1–2), generated files (EF migrations, `*.Designer.cs`, `*.g.cs`), and DI/configuration glue code.

**Coverage plateau diagnosis** — if coverage has stopped increasing, check for: `[Exclude]` attributes hiding large code sections, tests that execute code but assert nothing (inflated coverage without verification), or integration code that needs external dependencies (databases, file system).

**AI-generated test quality** — coverage delta alone is insufficient. Flag methods where CRAP score is still above threshold after coverage increased (tests may be happy-path only), and methods covered by a single test with no branch variation.

---

# Style

- **Keep risk hotspots prominent and immediately after the summary section** — developers should find the highest-risk methods quickly
- **Quantify recommendations** — "adding 3 tests for `ProcessOrder` would cut the CRAP score from 48 to ~6"
- **Be direct** — skip preamble, get to the table
- **Emoji for visual scanning in generated output** (defined in `references/output-format.md`):

  | Symbol | Meaning |
  |--------|---------|
  | 🔥 | hotspots |
  | 📋 | gaps |
  | 💡 | recommendations |
  | 📁 | reports |
  | ✅ | passing |
  | ❌ | failing |
  | ⚠️ | warning |
  | 🔴 | HIGH priority |
  | 🟡 | MED priority |
  | 🟢 | LOW priority |

- **Always use Unicode emoji in generated output** — never shortcodes like `:x:` or `:fire:`
