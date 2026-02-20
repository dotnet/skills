import chalk from "chalk";
import { writeFile, mkdir } from "node:fs/promises";
import { join, dirname } from "node:path";
import type { SkillVerdict, ReporterSpec, ScenarioComparison } from "./types.js";

export async function reportResults(
  verdicts: SkillVerdict[],
  reporters: ReporterSpec[],
  verbose: boolean
): Promise<void> {
  for (const reporter of reporters) {
    switch (reporter.type) {
      case "console":
        reportConsole(verdicts, verbose);
        break;
      case "json":
        await reportJson(verdicts, reporter.outputPath);
        break;
      case "junit":
        await reportJunit(verdicts, reporter.outputPath);
        break;
    }
  }
}

export async function saveRunResults(
  verdicts: SkillVerdict[],
  resultsDir: string,
  model?: string,
  judgeModel?: string
): Promise<string> {
  const timestamp = new Date().toISOString().replace(/[:.]/g, "-");
  const runDir = join(resultsDir, `run-${timestamp}`);
  await mkdir(runDir, { recursive: true });

  // Save full results JSON with metadata
  const output = {
    model: model ?? "unknown",
    judgeModel: judgeModel ?? model ?? "unknown",
    timestamp: new Date().toISOString(),
    verdicts,
  };
  await writeFile(
    join(runDir, "results.json"),
    JSON.stringify(output, null, 2),
    "utf-8"
  );

  // Save per-skill detail files
  for (const verdict of verdicts) {
    const skillDir = join(runDir, verdict.skillName);
    await mkdir(skillDir, { recursive: true });

    await writeFile(
      join(skillDir, "verdict.json"),
      JSON.stringify(verdict, null, 2),
      "utf-8"
    );

    for (const scenario of verdict.scenarios) {
      const scenarioSlug = scenario.scenarioName
        .toLowerCase()
        .replace(/[^a-z0-9]+/g, "-");

      // Save full judge output for both runs
      const judgeReport = [
        `# Judge Report: ${scenario.scenarioName}`,
        "",
        `## Baseline Judge`,
        `Overall Score: ${scenario.baseline.judgeResult.overallScore}/5`,
        `Reasoning: ${scenario.baseline.judgeResult.overallReasoning}`,
        "",
        ...scenario.baseline.judgeResult.rubricScores.map(
          (s) => `- **${s.criterion}**: ${s.score}/5 — ${s.reasoning}`
        ),
        "",
        `## With-Skill Judge`,
        `Overall Score: ${scenario.withSkill.judgeResult.overallScore}/5`,
        `Reasoning: ${scenario.withSkill.judgeResult.overallReasoning}`,
        "",
        ...scenario.withSkill.judgeResult.rubricScores.map(
          (s) => `- **${s.criterion}**: ${s.score}/5 — ${s.reasoning}`
        ),
        "",
        `## Baseline Agent Output`,
        "```",
        scenario.baseline.metrics.agentOutput || "(no output)",
        "```",
        "",
        `## With-Skill Agent Output`,
        "```",
        scenario.withSkill.metrics.agentOutput || "(no output)",
        "```",
      ].join("\n");

      await writeFile(
        join(skillDir, `${scenarioSlug}.md`),
        judgeReport,
        "utf-8"
      );
    }
  }

  return runDir;
}

function reportConsole(verdicts: SkillVerdict[], verbose: boolean): void {
  console.log("\n" + chalk.bold("═══ Skill Validation Results ═══") + "\n");

  for (const verdict of verdicts) {
    const icon = verdict.passed ? chalk.green("✓") : chalk.red("✗");
    const name = chalk.bold(verdict.skillName);
    const score = formatScore(verdict.overallImprovementScore);

    // Build score line with optional CI
    let scoreLine = `${icon} ${name}  ${score}`;
    if (verdict.confidenceInterval) {
      const ci = verdict.confidenceInterval;
      const ciStr = `[${formatPct(ci.low)}, ${formatPct(ci.high)}]`;
      const sigStr = verdict.isSignificant
        ? chalk.green("significant")
        : chalk.yellow("not significant");
      scoreLine += `  ${chalk.dim(ciStr)} ${sigStr}`;
    }
    if (verdict.normalizedGain !== undefined) {
      scoreLine += `  ${chalk.dim(`(g=${formatPct(verdict.normalizedGain)})`)}`;
    }
    console.log(scoreLine);
    console.log(`  ${chalk.dim(verdict.reason)}`);

    // Show profile warnings as diagnosis when skill fails
    if (!verdict.passed && verdict.profileWarnings && verdict.profileWarnings.length > 0) {
      console.log();
      console.log(`  ${chalk.yellow("Possible causes from skill analysis:")}`);
      for (const warning of verdict.profileWarnings) {
        console.log(`    ${chalk.dim("•")} ${chalk.dim(warning)}`);
      }
    }
    if (verdict.scenarios.length > 0) {
      console.log();
      for (const scenario of verdict.scenarios) {
        reportScenarioDetail(scenario, verbose);
      }
    }

    console.log();
  }

  // Summary
  const passed = verdicts.filter((v) => v.passed).length;
  const total = verdicts.length;
  const summaryColor = passed === total ? chalk.green : chalk.red;
  console.log(
    summaryColor(`${passed}/${total} skills passed validation`)
  );
  console.log();
}

function reportScenarioDetail(
  scenario: ScenarioComparison,
  verbose: boolean
): void {
  const icon =
    scenario.improvementScore >= 0 ? chalk.green("↑") : chalk.red("↓");
  console.log(
    `    ${icon} ${scenario.scenarioName}  ${formatScore(scenario.improvementScore)}`
  );

  const b = scenario.baseline.metrics;
  const s = scenario.withSkill.metrics;
  const bd = scenario.breakdown;

  const bRubric = avgRubricScore(scenario.baseline.judgeResult.rubricScores);
  const sRubric = avgRubricScore(scenario.withSkill.judgeResult.rubricScores);

  // [label, improvementValue, absoluteStr, lowerIsBetter]
  const metrics: [string, number, string, boolean][] = [
    ["Tokens", bd.tokenReduction, `${b.tokenEstimate} → ${s.tokenEstimate}`, true],
    ["Tool calls", bd.toolCallReduction, `${b.toolCallCount} → ${s.toolCallCount}`, true],
    ["Task completion", bd.taskCompletionImprovement, `${fmtBool(b.taskCompleted)} → ${fmtBool(s.taskCompleted)}`, false],
    ["Time", bd.timeReduction, `${fmtMs(b.wallTimeMs)} → ${fmtMs(s.wallTimeMs)}`, true],
    ["Quality (rubric)", bd.qualityImprovement, `${bRubric.toFixed(1)}/5 → ${sRubric.toFixed(1)}/5`, false],
    ["Quality (overall)", bd.overallJudgmentImprovement, `${scenario.baseline.judgeResult.overallScore.toFixed(1)}/5 → ${scenario.withSkill.judgeResult.overallScore.toFixed(1)}/5`, false],
    ["Errors", bd.errorReduction, `${b.errorCount} → ${s.errorCount}`, true],
  ];

  for (const [label, value, absolute, lowerIsBetter] of metrics) {
    // Green = good, Red = bad (based on improvement direction)
    const color =
      value > 0 ? chalk.green : value < 0 ? chalk.red : chalk.dim;
    // For "lower is better" metrics, show the actual change (negative = went down = good)
    const displayValue = lowerIsBetter ? -value : value;
    console.log(
      `      ${chalk.dim(label.padEnd(20))} ${color(formatDelta(displayValue).padEnd(10))} ${chalk.dim(absolute)}`
    );
  }

  // Full judge output
  console.log();

  const bj = scenario.baseline.judgeResult;
  const sj = scenario.withSkill.judgeResult;
  const scoreDelta = sj.overallScore - bj.overallScore;
  const deltaStr = scoreDelta > 0 ? chalk.green(`+${scoreDelta.toFixed(1)}`) :
    scoreDelta < 0 ? chalk.red(scoreDelta.toFixed(1)) : chalk.dim("±0");

  console.log(`      ${chalk.bold("Overall:")} ${bj.overallScore.toFixed(1)} → ${sj.overallScore.toFixed(1)} (${deltaStr})`);
  console.log();

  // Baseline judge
  console.log(`      ${chalk.cyan("─── Baseline Judge")} ${chalk.cyan.bold(`${bj.overallScore.toFixed(1)}/5`)} ${chalk.cyan("───")}`);
  console.log(`      ${chalk.dim(wrapText(bj.overallReasoning, 6))}`);
  if (bj.rubricScores.length > 0) {
    console.log();
    for (const rs of bj.rubricScores) {
      const scoreColor = rs.score >= 4 ? chalk.green : rs.score >= 3 ? chalk.yellow : chalk.red;
      console.log(`        ${scoreColor.bold(`${rs.score}/5`)}  ${chalk.white.bold(wrapText(rs.criterion, 14))}`);
      if (rs.reasoning) {
        console.log(`              ${chalk.dim(wrapText(rs.reasoning, 14))}`);
      }
    }
  }

  console.log();

  // With-skill judge
  console.log(`      ${chalk.magenta("─── With-Skill Judge")} ${chalk.magenta.bold(`${sj.overallScore.toFixed(1)}/5`)} ${chalk.magenta("───")}`);
  console.log(`      ${chalk.dim(wrapText(sj.overallReasoning, 6))}`);
  if (sj.rubricScores.length > 0) {
    console.log();
    for (const rs of sj.rubricScores) {
      const scoreColor = rs.score >= 4 ? chalk.green : rs.score >= 3 ? chalk.yellow : chalk.red;
      // Find matching baseline rubric score
      const baselineRs = bj.rubricScores.find(
        (b) => b.criterion.toLowerCase() === rs.criterion.toLowerCase()
      );
      const comparison = baselineRs
        ? chalk.dim(` (was ${baselineRs.score}/5)`)
        : "";
      console.log(`        ${scoreColor.bold(`${rs.score}/5`)}${comparison}  ${chalk.white.bold(wrapText(rs.criterion, 14))}`);
      if (rs.reasoning) {
        console.log(`              ${chalk.dim(wrapText(rs.reasoning, 14))}`);
      }
    }
  }
  console.log();

  // Pairwise judge results
  if (scenario.pairwiseResult) {
    const pw = scenario.pairwiseResult;
    const consistencyIcon = pw.positionSwapConsistent
      ? chalk.green("✓ consistent")
      : chalk.yellow("⚠ inconsistent");
    const winnerColor = pw.overallWinner === "skill" ? chalk.green : pw.overallWinner === "baseline" ? chalk.red : chalk.dim;
    console.log(`      ${chalk.bold("─── Pairwise Comparison")} ${consistencyIcon} ${chalk.bold("───")}`);
    console.log(`      Winner: ${winnerColor(pw.overallWinner)} (${pw.overallMagnitude})`);
    console.log(`      ${chalk.dim(wrapText(pw.overallReasoning, 6))}`);
    if (pw.rubricResults.length > 0) {
      console.log();
      for (const pr of pw.rubricResults) {
        const prColor = pr.winner === "skill" ? chalk.green : pr.winner === "baseline" ? chalk.red : chalk.dim;
        console.log(`        ${prColor.bold(pr.winner.padEnd(8))} (${pr.magnitude})  ${chalk.white.bold(wrapText(pr.criterion, 14))}`);
        if (pr.reasoning) {
          console.log(`              ${chalk.dim(wrapText(pr.reasoning, 14))}`);
        }
      }
    }
    console.log();
  }

  if (verbose) {
    console.log();
    console.log(`      ${chalk.dim("Baseline output:")}`);
    console.log(indentBlock(scenario.baseline.metrics.agentOutput || "(no output)", 8));
    console.log(`      ${chalk.dim("With-skill output:")}`);
    console.log(indentBlock(scenario.withSkill.metrics.agentOutput || "(no output)", 8));
  }
}

function avgRubricScore(
  scores: { score: number }[]
): number {
  if (scores.length === 0) return 0;
  return scores.reduce((sum, s) => sum + s.score, 0) / scores.length;
}

function fmtBool(v: boolean): string {
  return v ? "✓" : "✗";
}

function fmtMs(ms: number): string {
  if (ms < 1000) return `${Math.round(ms)}ms`;
  return `${(ms / 1000).toFixed(1)}s`;
}

function wrapText(text: string, indent: number): string {
  if (!text) return chalk.dim("(no reasoning)");
  const prefix = " ".repeat(indent);
  // Wrap at ~100 chars per line
  const words = text.split(/\s+/);
  const lines: string[] = [];
  let current = "";
  for (const word of words) {
    if (current.length + word.length + 1 > 100) {
      lines.push(current);
      current = word;
    } else {
      current = current ? `${current} ${word}` : word;
    }
  }
  if (current) lines.push(current);
  return lines.map((l, i) => (i === 0 ? l : `${prefix}${l}`)).join("\n");
}

function indentBlock(text: string, spaces: number): string {
  const prefix = " ".repeat(spaces);
  return text
    .split("\n")
    .map((l) => `${prefix}${l}`)
    .join("\n");
}

function formatScore(score: number): string {
  const pct = (score * 100).toFixed(1) + "%";
  if (score > 0) return chalk.green(`+${pct}`);
  if (score < 0) return chalk.red(pct);
  return chalk.dim(pct);
}

function formatPct(value: number): string {
  const pct = (value * 100).toFixed(1) + "%";
  if (value > 0) return `+${pct}`;
  return pct;
}

function formatDelta(value: number): string {
  const pct = (value * 100).toFixed(1) + "%";
  if (value > 0) return `+${pct}`;
  if (value < 0) return pct;
  return "0.0%";
}

function truncate(s: string, max: number): string {
  return s.length > max ? s.slice(0, max - 3) + "..." : s;
}

async function reportJson(
  verdicts: SkillVerdict[],
  outputPath?: string
): Promise<void> {
  const json = JSON.stringify(verdicts, null, 2);
  if (outputPath) {
    await mkdir(dirname(outputPath), { recursive: true });
    await writeFile(outputPath, json, "utf-8");
    console.log(`JSON results written to ${outputPath}`);
  } else {
    console.log(json);
  }
}

async function reportJunit(
  verdicts: SkillVerdict[],
  outputPath?: string
): Promise<void> {
  const testcases = verdicts.flatMap((verdict) => {
    if (verdict.scenarios.length === 0) {
      const status = verdict.passed ? "" : `<failure message="${escapeXml(verdict.reason)}" />`;
      return `    <testcase name="${escapeXml(verdict.skillName)}" classname="skill-validator">${status}</testcase>`;
    }
    return verdict.scenarios.map((scenario) => {
      const name = `${verdict.skillName} / ${scenario.scenarioName}`;
      const status =
        scenario.improvementScore >= 0
          ? ""
          : `<failure message="Improvement score: ${(scenario.improvementScore * 100).toFixed(1)}%" />`;
      return `    <testcase name="${escapeXml(name)}" classname="skill-validator">${status}</testcase>`;
    });
  });

  const xml = `<?xml version="1.0" encoding="UTF-8"?>
<testsuites>
  <testsuite name="skill-validator" tests="${testcases.length}">
${testcases.join("\n")}
  </testsuite>
</testsuites>
`;

  if (outputPath) {
    await writeFile(outputPath, xml, "utf-8");
    console.log(`JUnit results written to ${outputPath}`);
  } else {
    console.log(xml);
  }
}

function escapeXml(s: string): string {
  return s
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&apos;");
}
