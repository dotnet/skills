import { describe, it, expect } from "vitest";
import { buildSessionConfig, checkPermission } from "../src/runner.js";
import type { SkillInfo } from "../src/types.js";

describe("buildSessionConfig", () => {
  const mockSkill: SkillInfo = {
    name: "test-skill",
    description: "A test skill",
    path: "/home/user/skills/test-skill",
    skillMdPath: "/home/user/skills/test-skill/SKILL.md",
    skillMdContent: "# Test",
    evalPath: null,
    evalConfig: null,
  };

  it("sets skillDirectories to parent of skill path", () => {
    const config = buildSessionConfig(mockSkill, "gpt-4.1", "/tmp/work");
    expect(config.skillDirectories).toEqual(["/home/user/skills"]);
  });

  it("sets workingDirectory to the workDir", () => {
    const config = buildSessionConfig(mockSkill, "gpt-4.1", "/tmp/work");
    expect(config.workingDirectory).toBe("/tmp/work");
  });

  it("sets empty skillDirectories when no skill", () => {
    const config = buildSessionConfig(null, "gpt-4.1", "/tmp/work");
    expect(config.skillDirectories).toEqual([]);
  });

  it("passes the model through", () => {
    const config = buildSessionConfig(mockSkill, "claude-opus-4.6", "/tmp/work");
    expect(config.model).toBe("claude-opus-4.6");
  });

  it("disables infinite sessions", () => {
    const config = buildSessionConfig(mockSkill, "gpt-4.1", "/tmp/work");
    expect(config.infiniteSessions).toEqual({ enabled: false });
  });
});

describe("checkPermission", () => {
  it("approves paths inside workDir", () => {
    const result = checkPermission({ path: "/tmp/work/file.txt" }, "/tmp/work");
    expect(result).toEqual({ kind: "approved" });
  });

  it("approves paths inside skillPath", () => {
    const result = checkPermission(
      { path: "/home/user/skills/test-skill/SKILL.md" },
      "/tmp/work",
      "/home/user/skills/test-skill"
    );
    expect(result).toEqual({ kind: "approved" });
  });

  it("denies paths outside allowed directories", () => {
    const result = checkPermission({ path: "/etc/passwd" }, "/tmp/work");
    expect(result).toEqual({ kind: "denied-by-rules" });
  });

  it("approves requests with no path", () => {
    const result = checkPermission({}, "/tmp/work");
    expect(result).toEqual({ kind: "approved" });
  });

  it("denies paths outside workDir when no skillPath", () => {
    const result = checkPermission({ path: "/home/user/other" }, "/tmp/work");
    expect(result).toEqual({ kind: "denied-by-rules" });
  });

  it("denies paths with a shared prefix but different directory", () => {
    const result = checkPermission({ path: "/tmp/work-attacker/evil.sh" }, "/tmp/work");
    expect(result).toEqual({ kind: "denied-by-rules" });
  });

  it("checks command field when path is absent", () => {
    const result = checkPermission({ command: "/etc/shadow" }, "/tmp/work");
    expect(result).toEqual({ kind: "denied-by-rules" });
  });
});
