#!/usr/bin/env node

/**
 * gen-experiment — append a `plugin` variant to the canonical dotnet-skills
 * experiment for one plugin.
 *
 * Why this exists: Vally's `environment.skills` has no glob/enumeration, and
 * every entry must be a directory containing its own SKILL.md. So "load the
 * whole plugin" (the dashboard's Plugin role) can't be written statically the
 * way `baseline`/`skilled` are. Because each CI eval leg is scoped to a single
 * plugin, we can enumerate that plugin's skill directories once and emit a
 * per-leg experiment file that adds a third `plugin` variant.
 *
 * The base file's baseline + skilled variants (and all other settings) are
 * copied verbatim; only a `plugin:` variant block is appended under `variants:`
 * (which is the last top-level key in the base file, so an end-of-file append
 * is correct and leaves the rest untouched).
 *
 * Usage:
 *   node gen-experiment.mjs --plugin <name> \
 *     [--base dotnet-skills.experiment.yaml] [--content-root .] [--out <file>]
 *
 * Emits the generated file path on stdout (last line) for shell capture.
 */

import { readFileSync, writeFileSync, readdirSync, existsSync, statSync } from "node:fs";
import { join, resolve } from "node:path";
import { parseArgs } from "node:util";

const { values: opts } = parseArgs({
  options: {
    plugin: { type: "string" },
    base: { type: "string", default: "dotnet-skills.experiment.yaml" },
    "content-root": { type: "string", default: "." },
    out: { type: "string" },
    "variant-name": { type: "string", default: "plugin" },
    help: { type: "boolean", default: false },
  },
  strict: true,
});

if (opts.help || !opts.plugin) {
  console.log(`Usage:
  node gen-experiment.mjs --plugin <name> [--base <file>] [--content-root <dir>] [--out <file>]

Appends a '<variant-name>' (default: plugin) variant to the base experiment that
loads every skill directory in plugins/<plugin>/skills that has a SKILL.md.

Options:
  --plugin <name>         Plugin to build the plugin variant for (required).
  --base <file>           Base experiment YAML (default: dotnet-skills.experiment.yaml).
  --content-root <dir>    Root containing plugins/ (default: .).
  --out <file>            Output path (default: <base without .yaml>.<plugin>.plugin.yaml).
  --variant-name <name>   Name for the injected variant (default: plugin).
  --help                  Show this help`);
  process.exit(opts.help ? 0 : 1);
}

// Reject anything that isn't a single directory name so it can't traverse out
// of plugins/. The caller (CI) already validates, but keep the tool safe standalone.
if (!/^[A-Za-z0-9._-]+$/.test(opts.plugin) || opts.plugin === "." || opts.plugin.includes("..")) {
  console.error(`Error: invalid --plugin "${opts.plugin}" (must be a single directory name, not '.', no '..')`);
  process.exit(1);
}

const contentRoot = resolve(opts["content-root"]);
const skillsDir = join(contentRoot, "plugins", opts.plugin, "skills");

if (!existsSync(skillsDir) || !statSync(skillsDir).isDirectory()) {
  console.error(`Error: no skills directory at ${skillsDir}`);
  process.exit(1);
}

// Enumerate every direct child directory that carries a SKILL.md. This is the
// whole plugin as Vally can load it; agent.* skills are included when present
// because "the plugin" means every skill, not just the ones under test.
const skillDirs = readdirSync(skillsDir, { withFileTypes: true })
  .filter((d) => d.isDirectory())
  .map((d) => d.name)
  .filter((name) => existsSync(join(skillsDir, name, "SKILL.md")))
  .sort();

if (skillDirs.length === 0) {
  console.error(`Error: plugins/${opts.plugin}/skills contains no skill directory with a SKILL.md`);
  process.exit(1);
}

const baseText = readFileSync(resolve(opts.base), "utf-8");
if (!/^variants:/m.test(baseText)) {
  console.error(`Error: base experiment ${opts.base} has no top-level 'variants:' key`);
  process.exit(1);
}
if (new RegExp(`^\\s{2}${opts["variant-name"]}:\\s*$`, "m").test(baseText)) {
  console.error(`Error: base experiment already defines a '${opts["variant-name"]}' variant`);
  process.exit(1);
}

const skillLines = skillDirs
  .map((name) => `        - plugins/${opts.plugin}/skills/${name}`)
  .join("\n");

const variantBlock = [
  `  ${opts["variant-name"]}:`,
  `    environment:`,
  `      skills:`,
  skillLines,
  ``,
].join("\n");

const normalizedBase = baseText.endsWith("\n") ? baseText : baseText + "\n";
const out = resolve(
  opts.out ?? `${resolve(opts.base).replace(/\.ya?ml$/i, "")}.${opts.plugin}.plugin.yaml`,
);
writeFileSync(out, normalizedBase + variantBlock);

console.error(
  `Appended '${opts["variant-name"]}' variant with ${skillDirs.length} skill(s) for plugin '${opts.plugin}'`,
);
console.log(out);
