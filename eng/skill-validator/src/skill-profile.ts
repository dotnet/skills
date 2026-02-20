import type { SkillInfo } from "./types.js";

export interface SkillProfile {
  name: string;
  tokenCount: number;
  complexityTier: "compact" | "detailed" | "standard" | "comprehensive";
  sectionCount: number;
  codeBlockCount: number;
  numberedStepCount: number;
  bulletCount: number;
  hasFrontmatter: boolean;
  hasWhenToUse: boolean;
  hasWhenNotToUse: boolean;
  resourceFileCount: number;
  warnings: string[];
}

// Thresholds grounded in SkillsBench paper data:
//   median SKILL.md: 1,569 tokens
//   "detailed" (+18.8pp) and "compact" (+17.1pp) outperform
//   "comprehensive" hurts (–2.9pp)
const TOKEN_SWEET_LOW = 200;
const TOKEN_SWEET_HIGH = 2500;
const TOKEN_WARN_HIGH = 5000;

export function analyzeSkill(skill: SkillInfo): SkillProfile {
  const content = skill.skillMdContent;
  const tokenCount = Math.ceil(content.length / 4);

  // Frontmatter detection
  const hasFrontmatter = /^---\r?\n[\s\S]*?\r?\n---/m.test(content);

  // Strip frontmatter for structural analysis
  const body = content.replace(/^---\r?\n[\s\S]*?\r?\n---\r?\n?/, "");

  const sectionCount = (body.match(/^#{1,4}\s+/gm) || []).length;
  const codeBlockCount = Math.floor((body.match(/```/g) || []).length / 2);
  const numberedStepCount = (body.match(/^\d+\.\s/gm) || []).length;
  const bulletCount = (body.match(/^[-*]\s/gm) || []).length;

  const hasWhenToUse = /^#{1,4}\s+when\s+to\s+use/im.test(body);
  const hasWhenNotToUse = /^#{1,4}\s+when\s+not\s+to\s+use/im.test(body);

  // Complexity tier (based on SkillsBench Table 6 categories)
  let complexityTier: SkillProfile["complexityTier"];
  if (tokenCount < 400) complexityTier = "compact";
  else if (tokenCount <= 2500) complexityTier = "detailed";
  else if (tokenCount <= 5000) complexityTier = "standard";
  else complexityTier = "comprehensive";

  // Resource files: count non-SKILL.md files in the skill directory
  // We don't do async I/O here — just note whether evalConfig references resources
  const resourceFileCount = skill.evalConfig?.scenarios.reduce((count, s) => {
    return count + (s.setup?.files?.length ?? 0);
  }, 0) ?? 0;

  // Generate warnings
  const warnings: string[] = [];

  if (tokenCount > TOKEN_WARN_HIGH) {
    warnings.push(
      `Skill is ${tokenCount.toLocaleString()} tokens — "comprehensive" skills hurt performance by 2.9pp on average. Consider splitting into 2–3 focused skills.`
    );
  } else if (tokenCount > TOKEN_SWEET_HIGH) {
    warnings.push(
      `Skill is ${tokenCount.toLocaleString()} tokens — approaching "comprehensive" range where gains diminish.`
    );
  } else if (tokenCount < TOKEN_SWEET_LOW) {
    warnings.push(
      `Skill is only ${tokenCount} tokens — may be too sparse to provide actionable guidance.`
    );
  }

  if (sectionCount === 0) {
    warnings.push("No section headers — agents navigate structured documents better.");
  }

  if (codeBlockCount === 0) {
    warnings.push("No code blocks — agents perform better with concrete snippets and commands.");
  }

  if (numberedStepCount === 0) {
    warnings.push("No numbered workflow steps — agents follow sequenced procedures more reliably.");
  }

  if (!hasFrontmatter) {
    warnings.push("No YAML frontmatter — agents use name/description for skill discovery.");
  }

  return {
    name: skill.name,
    tokenCount,
    complexityTier,
    sectionCount,
    codeBlockCount,
    numberedStepCount,
    bulletCount,
    hasFrontmatter,
    hasWhenToUse,
    hasWhenNotToUse,
    resourceFileCount,
    warnings,
  };
}

export function formatProfileLine(profile: SkillProfile): string {
  const tier = profile.complexityTier;
  const tierIndicator =
    tier === "detailed" || tier === "compact" ? "✓" :
    tier === "comprehensive" ? "✗" : "~";

  return (
    `📊 ${profile.name}: ${profile.tokenCount.toLocaleString()} tokens (${tier} ${tierIndicator}), ` +
    `${profile.sectionCount} sections, ${profile.codeBlockCount} code blocks`
  );
}

export function formatProfileWarnings(profile: SkillProfile): string[] {
  return profile.warnings.map((w) => `   ⚠  ${w}`);
}

export function formatDiagnosisHints(profile: SkillProfile): string[] {
  if (profile.warnings.length === 0) return [];
  return [
    "Possible causes from skill analysis:",
    ...profile.warnings.map((w) => `  • ${w}`),
  ];
}
