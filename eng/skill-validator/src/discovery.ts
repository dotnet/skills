import { readdir, readFile, stat } from "node:fs/promises";
import { join, basename, dirname, resolve } from "node:path";
import { parse as parseYaml } from "yaml";
import { parseEvalConfig } from "./eval-schema.js";
import type { SkillInfo, EvalConfig, MCPServerDef } from "./types.js";

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

/**
 * Walk up from a skill directory to find the nearest plugin.json and
 * extract its mcpServers map (if any).
 */
async function findPluginMcpServers(
  skillDir: string,
  maxLevels = 2
): Promise<Record<string, MCPServerDef> | undefined> {
  let dir = resolve(skillDir);
  for (let i = 0; i < maxLevels; i++) {
    const candidate = join(dir, "plugin.json");
    if (await fileExists(candidate)) {
      try {
        const raw = JSON.parse(await readFile(candidate, "utf-8"));
        if (raw.mcpServers && typeof raw.mcpServers === "object") {
          return raw.mcpServers as Record<string, MCPServerDef>;
        }
      } catch {
        // malformed plugin.json — skip
      }
      return undefined;
    }
    const parent = dirname(dir);
    if (parent === dir) break; // filesystem root
    dir = parent;
  }
  return undefined;
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

  const mcpServers = await findPluginMcpServers(dirPath);

  return {
    name,
    description,
    path: dirPath,
    skillMdPath,
    skillMdContent,
    evalPath,
    evalConfig,
    mcpServers,
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
  const promises = entries
    .filter((entry) => entry.isDirectory() && !entry.name.startsWith("."))
    .map((entry) => discoverSkillAt(join(targetPath, entry.name), testsDir));

  const results = await Promise.all(promises);
  for (const skill of results) {
    if (skill) skills.push(skill);
  }

  return skills;
}
