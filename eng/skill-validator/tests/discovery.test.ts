import { describe, it, expect } from "vitest";
import { discoverSkills } from "../src/discovery.js";
import { join } from "node:path";

const FIXTURES = join(import.meta.dirname, "fixtures");

describe("discoverSkills", () => {
  it("discovers a single skill directly", async () => {
    const skills = await discoverSkills(join(FIXTURES, "sample-skill"));
    expect(skills).toHaveLength(1);
    expect(skills[0].name).toBe("sample-skill");
    expect(skills[0].description).toContain("greeting");
    expect(skills[0].evalConfig).not.toBeNull();
    expect(skills[0].evalConfig!.scenarios).toHaveLength(2);
  });

  it("discovers skills in a parent directory", async () => {
    const skills = await discoverSkills(FIXTURES);
    expect(skills.length).toBeGreaterThanOrEqual(2);
    const names = skills.map((s) => s.name);
    expect(names).toContain("sample-skill");
    expect(names).toContain("no-eval-skill");
  });

  it("handles a skill with no eval.yaml", async () => {
    const skills = await discoverSkills(join(FIXTURES, "no-eval-skill"));
    expect(skills).toHaveLength(1);
    expect(skills[0].evalConfig).toBeNull();
    expect(skills[0].evalPath).toBeNull();
  });

  it("returns empty for non-skill directory", async () => {
    const skills = await discoverSkills("/tmp");
    expect(skills).toHaveLength(0);
  });
});
