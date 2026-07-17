// locate.mjs — skill <-> eval discovery and parsing.
//
// Repo conventions (dotnet/skills):
//   skill:  plugins/<plugin>/skills/<name>/SKILL.md
//   agent:  plugins/<plugin>/agents/<name>.agent.md
//   eval:   tests/<plugin>/<name>/eval.yaml          (native skill-validator format)
//           tests/<plugin>/<name>/eval.vally.yaml     (vally-native adapted twin)
//           tests/<plugin>/agent.<name>/...           (evals for an agent)
//   fixtures: tests/<plugin>/<name>/fixtures/**       (optional)
//
// Discovery is layered and loud on ambiguity: we resolve by convention and
// attach a `notes[]` array to each unit describing anything unexpected (no eval,
// no skill doc, multiple eval files) so callers can surface it instead of
// silently dropping a unit.

import { readFileSync, existsSync, readdirSync, statSync } from "node:fs";
import { join, relative, sep, basename, dirname } from "node:path";
import yaml from "js-yaml";

const EVAL_FILENAMES = ["eval.yaml", "eval.vally.yaml", "eval.yml"];

function toPosix(p) {
  return p.split(sep).join("/");
}

function readText(path) {
  try {
    return readFileSync(path, "utf-8");
  } catch {
    return null;
  }
}

function isDir(path) {
  try {
    return statSync(path).isDirectory();
  } catch {
    return false;
  }
}

function listDirs(path) {
  try {
    return readdirSync(path, { withFileTypes: true })
      .filter((d) => d.isDirectory())
      .map((d) => d.name);
  } catch {
    return [];
  }
}

/** Recursively list files under a directory, returning repo-relative posix paths. */
function walkFiles(root, dir, acc = []) {
  let entries;
  try {
    entries = readdirSync(dir, { withFileTypes: true });
  } catch {
    return acc;
  }
  for (const e of entries) {
    const full = join(dir, e.name);
    if (e.isDirectory()) walkFiles(root, full, acc);
    else if (e.isFile()) acc.push(toPosix(relative(root, full)));
  }
  return acc;
}

/**
 * Parse a native eval.yaml (skill-validator schema) or eval.vally.yaml into a
 * normalized shape:
 *   { format: "native"|"vally"|"unknown", scenarios: [{ name, prompt,
 *     expectActivation, assertionTypes: string[], assertions: object[],
 *     rubric: string[], setupFiles: [{path, hasSource, hasContent, content}] }],
 *     parseError }
 */
export function parseEvalFile(path) {
  const text = readText(path);
  if (text == null) return { format: "unknown", scenarios: [], parseError: "unreadable", text: "" };
  let doc;
  try {
    doc = yaml.load(text);
  } catch (err) {
    return { format: "unknown", scenarios: [], parseError: String(err?.message ?? err), text };
  }
  if (!doc || typeof doc !== "object") {
    return { format: "unknown", scenarios: [], parseError: "empty or non-object document", text };
  }

  // Native skill-validator format: top-level `scenarios:`.
  if (Array.isArray(doc.scenarios)) {
    const scenarios = doc.scenarios.map((s) => normalizeNativeScenario(s));
    return { format: "native", scenarios, parseError: null, text };
  }
  // Vally-native format: top-level `stimuli:` with per-stimulus `graders:`.
  if (Array.isArray(doc.stimuli)) {
    const scenarios = doc.stimuli.map((s) => normalizeVallyStimulus(s));
    return { format: "vally", scenarios, parseError: null, text };
  }
  return { format: "unknown", scenarios: [], parseError: "no scenarios/stimuli key", text };
}

function normalizeNativeScenario(s) {
  const assertions = Array.isArray(s?.assertions) ? s.assertions : [];
  const setup = s?.setup ?? {};
  const files = Array.isArray(setup.files) ? setup.files : [];
  return {
    name: s?.name ?? "",
    prompt: typeof s?.prompt === "string" ? s.prompt : "",
    expectActivation: s?.expect_activation !== false, // default true unless explicitly false
    hasExpectActivationFalse: s?.expect_activation === false,
    assertionTypes: assertions.map((a) => a?.type).filter(Boolean),
    assertions,
    rubric: Array.isArray(s?.rubric) ? s.rubric : [],
    copyTestFiles: setup.copy_test_files === true,
    setupCommands: Array.isArray(setup.commands) ? setup.commands : [],
    setupFiles: files.map((f) => ({
      path: f?.path ?? "",
      source: f?.source ?? null,
      hasSource: typeof f?.source === "string",
      hasContent: typeof f?.content === "string",
      content: typeof f?.content === "string" ? f.content : null,
    })),
  };
}

function normalizeVallyStimulus(s) {
  const graders = Array.isArray(s?.graders) ? s.graders : [];
  return {
    name: s?.name ?? "",
    prompt: typeof s?.prompt === "string" ? s.prompt : "",
    expectActivation: true,
    hasExpectActivationFalse: false,
    // Map vally grader `type`s onto the native assertion vocabulary so downstream
    // graders can reason uniformly.
    assertionTypes: graders.map((g) => vallyGraderToAssertionType(g?.type)).filter(Boolean),
    assertions: graders,
    rubric: Array.isArray(s?.rubric) ? s.rubric : [],
    copyTestFiles: false,
    setupCommands: [],
    setupFiles: [],
  };
}

function vallyGraderToAssertionType(type) {
  switch (type) {
    case "output-contains":
      return "output_contains";
    case "output-matches":
      return "output_matches";
    case "file-contains":
      return "file_contains";
    case "command":
    case "run-command":
      return "run_command_and_assert";
    default:
      return type; // "prompt", etc.
  }
}

// Assertion types that verify a concrete RESULT (checklist B: hard gate).
const HARD_GATE_TYPES = new Set([
  "run_command_and_assert",
  "file_contains",
  "file_not_contains",
  "file_exists",
  "file_not_exists",
  "exit_success",
]);

export function isHardGate(assertionType) {
  return HARD_GATE_TYPES.has(assertionType);
}

/**
 * Resolve the skill/agent doc that owns an eval directory.
 * Returns { skillDoc, skillDir, kind } or nulls if not found.
 */
function resolveSkillDoc(repoRoot, plugin, evalName) {
  // Agent evals live under tests/<plugin>/agent.<name>/ and map to
  // plugins/<plugin>/agents/<name>.agent.md.
  if (evalName.startsWith("agent.")) {
    const agentName = evalName.slice("agent.".length);
    const doc = join(repoRoot, "plugins", plugin, "agents", `${agentName}.agent.md`);
    if (existsSync(doc)) return { skillDoc: doc, skillDir: dirname(doc), kind: "agent" };
    return { skillDoc: null, skillDir: null, kind: "agent" };
  }
  const skillDir = join(repoRoot, "plugins", plugin, "skills", evalName);
  const doc = join(skillDir, "SKILL.md");
  if (existsSync(doc)) return { skillDoc: doc, skillDir, kind: "skill" };
  return { skillDoc: null, skillDir: isDir(skillDir) ? skillDir : null, kind: "skill" };
}

/** Build a unit from an eval directory (tests/<plugin>/<name>). */
export function unitFromEvalDir(repoRoot, evalDirAbs) {
  const rel = toPosix(relative(repoRoot, evalDirAbs)); // tests/<plugin>/<name>
  const parts = rel.split("/");
  const plugin = parts[1] ?? "";
  const name = parts[2] ?? basename(evalDirAbs);
  const notes = [];

  const evalFiles = [];
  for (const fn of EVAL_FILENAMES) {
    const p = join(evalDirAbs, fn);
    if (existsSync(p)) {
      const parsed = parseEvalFile(p);
      evalFiles.push({ path: toPosix(relative(repoRoot, p)), abs: p, parsed });
    }
  }
  if (evalFiles.length === 0) notes.push(`no eval file found in ${rel}`);

  const { skillDoc, skillDir, kind } = resolveSkillDoc(repoRoot, plugin, name);
  if (!skillDoc) notes.push(`no skill/agent doc resolved for ${plugin}/${name}`);

  const fixturesAbs = join(evalDirAbs, "fixtures");
  const fixtures = isDir(fixturesAbs) ? walkFiles(repoRoot, fixturesAbs) : [];

  return {
    repoRoot,
    plugin,
    name,
    kind,
    evalDir: rel,
    evalDirAbs,
    evalFiles,
    skillDoc: skillDoc ? toPosix(relative(repoRoot, skillDoc)) : null,
    skillDocAbs: skillDoc,
    skillDocText: skillDoc ? readText(skillDoc) : null,
    skillDir: skillDir ? toPosix(relative(repoRoot, skillDir)) : null,
    fixtures,
    notes,
  };
}

/** Enumerate every eval directory under tests/. */
export function findEvalDirs(repoRoot) {
  const testsRoot = join(repoRoot, "tests");
  const result = [];
  for (const plugin of listDirs(testsRoot)) {
    const pluginDir = join(testsRoot, plugin);
    for (const name of listDirs(pluginDir)) {
      const evalDir = join(pluginDir, name);
      if (EVAL_FILENAMES.some((fn) => existsSync(join(evalDir, fn)))) {
        result.push(evalDir);
      }
    }
  }
  return result;
}

/** All units in the repo. */
export function allUnits(repoRoot) {
  return findEvalDirs(repoRoot).map((d) => unitFromEvalDir(repoRoot, d));
}

/**
 * Map a set of changed repo-relative files to the units they touch, walking
 * both directions (eval/fixture -> unit, and skill/agent doc -> unit).
 */
export function unitsFromChangedFiles(repoRoot, changedFiles) {
  const evalDirKeys = new Set();
  const addByPluginName = (plugin, name) => {
    if (!plugin || !name) return;
    const evalDir = join(repoRoot, "tests", plugin, name);
    if (EVAL_FILENAMES.some((fn) => existsSync(join(evalDir, fn)))) {
      evalDirKeys.add(toPosix(relative(repoRoot, evalDir)));
    }
  };

  for (const raw of changedFiles) {
    const f = toPosix(raw).replace(/^\.\//, "");
    const parts = f.split("/");
    if (parts[0] === "tests" && parts.length >= 3) {
      // tests/<plugin>/<name>/...  (covers eval files and fixtures)
      addByPluginName(parts[1], parts[2]);
    } else if (parts[0] === "plugins" && parts[2] === "skills" && parts.length >= 4) {
      // plugins/<plugin>/skills/<name>/...
      addByPluginName(parts[1], parts[3]);
    } else if (parts[0] === "plugins" && parts[2] === "agents" && parts.length >= 4) {
      // plugins/<plugin>/agents/<name>.agent.md -> tests/<plugin>/agent.<name>
      const agentFile = parts[3];
      const m = /^(.*)\.agent\.md$/.exec(agentFile);
      if (m) addByPluginName(parts[1], `agent.${m[1]}`);
    }
  }

  return [...evalDirKeys]
    .sort()
    .map((rel) => unitFromEvalDir(repoRoot, join(repoRoot, ...rel.split("/"))));
}

/**
 * Build units from Vally skill directories (GraderInput.stimulus.environment.skills).
 * Each entry is a skill dir like plugins/<plugin>/skills/<name>; we recover the
 * plugin/name and delegate to the eval-dir resolver.
 */
export function unitsFromSkillDirs(repoRoot, skillDirs) {
  const units = [];
  for (const sd of skillDirs) {
    const rel = toPosix(relative(repoRoot, sd)).replace(/^\.\//, "");
    const parts = rel.split("/");
    let plugin;
    let name;
    if (parts[0] === "plugins" && parts[2] === "skills") {
      plugin = parts[1];
      name = parts[3];
    } else if (parts[0] === "plugins" && parts[2] === "agents") {
      plugin = parts[1];
      const m = /^(.*)\.agent\.md$/.exec(parts[3] ?? "");
      name = m ? `agent.${m[1]}` : parts[3];
    } else {
      // Fall back to the last two path segments.
      plugin = parts[parts.length - 2];
      name = parts[parts.length - 1];
    }
    const evalDir = join(repoRoot, "tests", plugin, name);
    units.push(unitFromEvalDir(repoRoot, evalDir));
  }
  return units;
}

/** Read a repo file's text (repo-relative posix path). */
export function readRepoFile(repoRoot, relPath) {
  return readText(join(repoRoot, ...relPath.split("/")));
}
