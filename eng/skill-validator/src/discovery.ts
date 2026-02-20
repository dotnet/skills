import { readdir, readFile, stat } from "node:fs/promises";
import { join, basename } from "node:path";
import { parse as parseYaml } from "yaml";
import { parseEvalConfig } from "./eval-schema.js";
import type { SkillInfo, EvalConfig } from "./types.js";

function parseFrontmatter(content: string): {
  metadata: Record<string, string>;
  body: string;
} {
  const match = content.match(/^---\r?\n([\s\S]*?)\r?\n---\r?\n([\s\S]*)$/);
  if (!match) {
    return { metadata: {}, body: content };
  }
  const metadata = parseYaml(match[1]) as Record<string, string>;
  return { metadata, body: match[2] };
}

async function isDirectory(path: string): Promise<boolean> {
  try {
    return (await stat(path)).isDirectory();
  } catch {
    return false;
  }
}

async function fileExists(path: string): Promise<boolean> {
  try {
    await stat(path);
    return true;
  } catch {
    return false;
  }
}

async function discoverSkillAt(dirPath: string, testsDir?: string): Promise<SkillInfo | null> {
  const skillMdPath = join(dirPath, "SKILL.md");
  if (!(await fileExists(skillMdPath))) return null;

  const skillMdContent = await readFile(skillMdPath, "utf-8");
  const { metadata } = parseFrontmatter(skillMdContent);

  const name = metadata.name || basename(dirPath);
  const description = metadata.description || "";

  let evalPath: string | null = null;
  let evalConfig: EvalConfig | null = null;

  const evalFilePath = testsDir
    ? join(testsDir, basename(dirPath), "eval.yaml")
    : join(dirPath, "tests", "eval.yaml");
  if (await fileExists(evalFilePath)) {
    evalPath = evalFilePath;
    const evalContent = await readFile(evalFilePath, "utf-8");
    const parsed = parseYaml(evalContent);
    evalConfig = parseEvalConfig(parsed);
  }

  return {
    name,
    description,
    path: dirPath,
    skillMdPath,
    skillMdContent,
    evalPath,
    evalConfig,
  };
}

export async function discoverSkills(targetPath: string, testsDir?: string): Promise<SkillInfo[]> {
  const skills: SkillInfo[] = [];

  // Check if the target itself is a skill
  const directSkill = await discoverSkillAt(targetPath, testsDir);
  if (directSkill) {
    skills.push(directSkill);
    return skills;
  }

  // Otherwise, scan subdirectories (one level deep)
  if (!(await isDirectory(targetPath))) return skills;

  const entries = await readdir(targetPath, { withFileTypes: true });
  for (const entry of entries) {
    if (!entry.isDirectory() || entry.name.startsWith(".")) continue;
    const skill = await discoverSkillAt(join(targetPath, entry.name), testsDir);
    if (skill) skills.push(skill);
  }

  return skills;
}
