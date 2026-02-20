import { describe, it, expect } from "vitest";
import { analyzeSkill, formatProfileLine, formatDiagnosisHints } from "../src/skill-profile.js";
import type { SkillInfo } from "../src/types.js";

function makeSkill(content: string, name = "test-skill"): SkillInfo {
  return {
    name,
    description: "Test skill",
    path: "/tmp/test-skill",
    skillMdPath: "/tmp/test-skill/SKILL.md",
    skillMdContent: content,
    evalPath: null,
    evalConfig: null,
  };
}

describe("analyzeSkill", () => {
  it("detects frontmatter", () => {
    const skill = makeSkill("---\nname: foo\n---\n# Hello\nSome content");
    const profile = analyzeSkill(skill);
    expect(profile.hasFrontmatter).toBe(true);
  });

  it("detects missing frontmatter", () => {
    const skill = makeSkill("# Hello\nSome content");
    const profile = analyzeSkill(skill);
    expect(profile.hasFrontmatter).toBe(false);
    expect(profile.warnings).toContainEqual(
      expect.stringContaining("frontmatter")
    );
  });

  it("counts sections and code blocks", () => {
    const content = [
      "---\nname: foo\n---",
      "# Title",
      "## Section 1",
      "```bash\necho hello\n```",
      "## Section 2",
      "```python\nprint('hi')\n```",
      "```js\nconsole.log('x')\n```",
    ].join("\n");
    const profile = analyzeSkill(makeSkill(content));
    expect(profile.sectionCount).toBe(3);
    expect(profile.codeBlockCount).toBe(3);
  });

  it("counts numbered steps", () => {
    const content = "---\nname: foo\n---\n# Steps\n1. First\n2. Second\n3. Third\n";
    const profile = analyzeSkill(makeSkill(content));
    expect(profile.numberedStepCount).toBe(3);
  });

  it("classifies compact skills", () => {
    const content = "---\nname: foo\n---\n# Short\nBrief.";
    const profile = analyzeSkill(makeSkill(content));
    expect(profile.complexityTier).toBe("compact");
  });

  it("classifies comprehensive skills and warns", () => {
    // >5000 tokens = >20000 chars
    const content = "---\nname: foo\n---\n# Big\n" + "x".repeat(25000);
    const profile = analyzeSkill(makeSkill(content));
    expect(profile.complexityTier).toBe("comprehensive");
    expect(profile.warnings).toContainEqual(
      expect.stringContaining("comprehensive")
    );
  });

  it("detects 'when to use' sections", () => {
    const content = "---\nname: foo\n---\n# My Skill\n## When to Use\nUse when...\n## When Not to Use\nDon't use when...";
    const profile = analyzeSkill(makeSkill(content));
    expect(profile.hasWhenToUse).toBe(true);
    expect(profile.hasWhenNotToUse).toBe(true);
  });

  it("warns when no code blocks present", () => {
    const content = "---\nname: foo\n---\n# Title\nJust text, no code.";
    const profile = analyzeSkill(makeSkill(content));
    expect(profile.warnings).toContainEqual(
      expect.stringContaining("code blocks")
    );
  });

  it("produces no warnings for a well-structured skill", () => {
    const content = [
      "---\nname: good-skill\ndescription: A good skill\n---",
      "# Good Skill",
      "## When to Use",
      "Use when you need to do X.",
      "## Steps",
      "1. First step",
      "2. Second step",
      "3. Third step",
      "```bash",
      "echo hello",
      "```",
      // Pad to ~1500 tokens (6000 chars)
      "Detailed explanation. ".repeat(250),
    ].join("\n");
    const profile = analyzeSkill(makeSkill(content));
    expect(profile.complexityTier).toBe("detailed");
    expect(profile.warnings).toHaveLength(0);
  });
});

describe("formatProfileLine", () => {
  it("shows tier indicator", () => {
    const content = "---\nname: foo\n---\n# Title\n```js\nx\n```\n1. Step\n" + "x".repeat(4000);
    const profile = analyzeSkill(makeSkill(content, "my-skill"));
    const line = formatProfileLine(profile);
    expect(line).toContain("my-skill");
    expect(line).toContain("detailed");
    expect(line).toContain("✓");
  });
});

describe("formatDiagnosisHints", () => {
  it("returns empty for skills with no warnings", () => {
    const content = [
      "---\nname: foo\n---",
      "# Title",
      "1. Step",
      "```bash\necho\n```",
      "x".repeat(4000),
    ].join("\n");
    const profile = analyzeSkill(makeSkill(content));
    expect(formatDiagnosisHints(profile)).toHaveLength(0);
  });

  it("returns hints for skills with warnings", () => {
    const profile = analyzeSkill(makeSkill("tiny"));
    const hints = formatDiagnosisHints(profile);
    expect(hints.length).toBeGreaterThan(1);
    expect(hints[0]).toContain("Possible causes");
  });
});
