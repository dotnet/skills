// grader-base.mjs — adapt a repo-local `review(unit) -> Finding[]` core into the
// Vally Grader contract, so the exact same logic powers both the standalone
// per-PR runner (review.mjs) and `vally eval --grader-plugin ./eng/vally-graders`.

import { unitsFromSkillDirs } from "./locate.mjs";
import { finding, formatFinding, isBlocking, verdict, SEVERITY } from "./report.mjs";

export { finding, SEVERITY };

/**
 * @param {object} spec
 * @param {object} spec.metadata  Vally GraderMetadata (name, determinism, ...)
 * @param {(unit: object) => object[]} spec.review  core check: unit -> Finding[]
 * @param {string} [spec.dimension]  default dimension id for findings
 */
export function makeStaticGrader(spec) {
  const { metadata, review } = spec;
  return {
    metadata,
    review, // exposed for the standalone runner + tests
    async grade(input) {
      const env = input?.stimulus?.environment;
      const skillDirs = env?.skills ?? [];
      const cwd = input?.config?.cwd ?? process.cwd();
      if (!skillDirs.length) {
        throw new Error(`${metadata.name} grader requires at least one skill in stimulus.environment.skills`);
      }
      const units = unitsFromSkillDirs(cwd, skillDirs);
      const allFindings = [];
      const details = [];
      for (const unit of units) {
        const findings = review(unit);
        allFindings.push(...findings);
        const blocking = findings.filter((f) => isBlocking(f.severity));
        details.push({
          name: `${metadata.name}:${unit.plugin}/${unit.name}`,
          kind: "code",
          passed: blocking.length === 0,
          score: blocking.length === 0 ? 1 : 0,
          label: verdict(findings),
          evidence:
            findings.length === 0
              ? "no findings"
              : findings.map(formatFinding).join("\n"),
        });
      }
      const blocking = allFindings.filter((f) => isBlocking(f.severity));
      const passed = blocking.length === 0;
      return {
        name: metadata.name,
        kind: "code",
        passed,
        score: passed ? 1 : 0,
        label: verdict(allFindings),
        evidence: passed
          ? `${metadata.name}: no blocking findings across ${units.length} unit(s).`
          : `${metadata.name}: ${blocking.length} blocking finding(s).\n${blocking
              .map(formatFinding)
              .join("\n")}`,
        details,
        metadata: { findings: allFindings },
      };
    },
  };
}
