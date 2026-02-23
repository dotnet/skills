import { describe, it, expect } from "vitest";
import { extractSkillActivation } from "../src/metrics.js";
import type { AgentEvent } from "../src/types.js";

function makeEvent(type: string, data: Record<string, unknown> = {}): AgentEvent {
  return { type, timestamp: Date.now(), data };
}

describe("extractSkillActivation", () => {
  it("detects activation from skill session events", () => {
    const events: AgentEvent[] = [
      makeEvent("skill.loaded", { skillName: "my-skill" }),
      makeEvent("assistant.message", { content: "hello" }),
      makeEvent("tool.execution_start", { toolName: "bash" }),
    ];

    const result = extractSkillActivation(events, { bash: 1 });

    expect(result.activated).toBe(true);
    expect(result.detectedSkills).toEqual(["my-skill"]);
    expect(result.skillEventCount).toBe(1);
    expect(result.extraTools).toEqual([]);
  });

  it("detects activation from instruction events", () => {
    const events: AgentEvent[] = [
      makeEvent("instruction.attached", { name: "build-helper" }),
      makeEvent("tool.execution_start", { toolName: "read" }),
    ];

    const result = extractSkillActivation(events, { read: 1 });

    expect(result.activated).toBe(true);
    expect(result.detectedSkills).toEqual(["build-helper"]);
    expect(result.skillEventCount).toBe(1);
  });

  it("detects activation from extra tools not in baseline", () => {
    const events: AgentEvent[] = [
      makeEvent("tool.execution_start", { toolName: "bash" }),
      makeEvent("tool.execution_start", { toolName: "msbuild_analyze" }),
      makeEvent("assistant.message", { content: "done" }),
    ];

    const result = extractSkillActivation(events, { bash: 3 });

    expect(result.activated).toBe(true);
    expect(result.detectedSkills).toEqual([]);
    expect(result.extraTools).toEqual(["msbuild_analyze"]);
    expect(result.skillEventCount).toBe(0);
  });

  it("reports not activated when no skill events and no extra tools", () => {
    const events: AgentEvent[] = [
      makeEvent("tool.execution_start", { toolName: "bash" }),
      makeEvent("assistant.message", { content: "done" }),
    ];

    const result = extractSkillActivation(events, { bash: 1 });

    expect(result.activated).toBe(false);
    expect(result.detectedSkills).toEqual([]);
    expect(result.extraTools).toEqual([]);
    expect(result.skillEventCount).toBe(0);
  });

  it("handles empty events array", () => {
    const result = extractSkillActivation([], {});

    expect(result.activated).toBe(false);
    expect(result.detectedSkills).toEqual([]);
    expect(result.extraTools).toEqual([]);
    expect(result.skillEventCount).toBe(0);
  });

  it("handles empty baseline tool breakdown", () => {
    const events: AgentEvent[] = [
      makeEvent("tool.execution_start", { toolName: "bash" }),
    ];

    const result = extractSkillActivation(events, {});

    expect(result.activated).toBe(true);
    expect(result.extraTools).toEqual(["bash"]);
  });

  it("deduplicates detected skill names", () => {
    const events: AgentEvent[] = [
      makeEvent("skill.loaded", { skillName: "my-skill" }),
      makeEvent("skill.activated", { skillName: "my-skill" }),
      makeEvent("skill.loaded", { skillName: "other-skill" }),
    ];

    const result = extractSkillActivation(events, {});

    expect(result.detectedSkills).toEqual(["my-skill", "other-skill"]);
    expect(result.skillEventCount).toBe(3);
  });

  it("handles missing skill name in events gracefully", () => {
    const events: AgentEvent[] = [
      makeEvent("skill.loaded", {}),
      makeEvent("skill.loaded", { skillName: "" }),
    ];

    const result = extractSkillActivation(events, {});

    expect(result.activated).toBe(true);
    expect(result.detectedSkills).toEqual([]);
    expect(result.skillEventCount).toBe(2);
  });

  it("combines both heuristics - skill events and extra tools", () => {
    const events: AgentEvent[] = [
      makeEvent("skill.loaded", { skillName: "build-cache" }),
      makeEvent("tool.execution_start", { toolName: "bash" }),
      makeEvent("tool.execution_start", { toolName: "msbuild_diag" }),
    ];

    const result = extractSkillActivation(events, { bash: 2 });

    expect(result.activated).toBe(true);
    expect(result.detectedSkills).toEqual(["build-cache"]);
    expect(result.extraTools).toEqual(["msbuild_diag"]);
    expect(result.skillEventCount).toBe(1);
  });

  it("does not count non-skill events as skill events", () => {
    const events: AgentEvent[] = [
      makeEvent("assistant.message", { content: "I used a skill" }),
      makeEvent("session.idle", {}),
      makeEvent("tool.execution_start", { toolName: "bash" }),
      makeEvent("session.error", { message: "failed" }),
    ];

    const result = extractSkillActivation(events, { bash: 1 });

    expect(result.activated).toBe(false);
    expect(result.skillEventCount).toBe(0);
  });
});
