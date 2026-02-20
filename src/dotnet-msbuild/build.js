#!/usr/bin/env node

// Build entry point for the dotnet-msbuild component.
// Validates skills and compiles knowledge bundles.
// Run: node src/dotnet-msbuild/build.js

const fs = require("node:fs");
const path = require("node:path");

const SKILLS_DIR = path.resolve(__dirname, "skills");
const DOMAIN_GATE_PATTERN = /Only activate in MSBuild\/\.NET build contexts/;

// ── Step 1: Validate skills ─────────────────────────────────────────

console.log("=== Validating skills ===\n");

let errors = 0;

const skillDirs = fs.readdirSync(SKILLS_DIR, { withFileTypes: true })
  .filter(d => d.isDirectory() && d.name !== "shared");

for (const dir of skillDirs) {
  const skillFile = path.join(SKILLS_DIR, dir.name, "SKILL.md");
  if (!fs.existsSync(skillFile)) continue;

  const content = fs.readFileSync(skillFile, "utf-8");

  const match = content.match(/^---\s*\n([\s\S]*?)\n---/);
  if (!match) {
    console.error(`❌ ${dir.name}: Missing YAML frontmatter`);
    errors++;
    continue;
  }

  const frontmatter = match[1];
  const descMatch = frontmatter.match(/description:\s*"([^"]*)"/);
  if (!descMatch) {
    console.error(`❌ ${dir.name}: Missing description in frontmatter`);
    errors++;
    continue;
  }

  const description = descMatch[1];
  if (!DOMAIN_GATE_PATTERN.test(description)) {
    console.error(`❌ ${dir.name}: Description missing domain gate. Must include 'Only activate in MSBuild/.NET build contexts (see shared/domain-check.md for signals).'`);
    errors++;
  }
}

if (errors > 0) {
  console.error(`\n${errors} validation error(s) found.`);
  process.exit(1);
} else {
  console.log(`✅ All ${skillDirs.length} skills pass validation.\n`);
}

// ── Step 2: Compile knowledge bundles ────────────────────────────────

console.log("=== Compiling knowledge ===\n");

const KNOWLEDGE_TARGETS = {
  "copilot-extension": {
    outputDir: path.resolve(__dirname, "copilot-extension/src/knowledge"),
    maxChars: 50000,
    knowledgeMap: {
      "build-errors": [
        "common-build-errors",
        "sourcegen-analyzer-failures",
        "nuget-restore-failures",
        "sdk-workload-resolution",
        "multitarget-tfm-issues",
      ],
      performance: [
        "build-perf-baseline",
        "build-perf-diagnostics",
        "incremental-build",
        "build-parallelism",
        "build-caching",
        "eval-performance",
      ],
      "style-guide": [
        "msbuild-style-guide",
        "msbuild-antipatterns",
        "directory-build-organization",
        "check-bin-obj-clash",
        "including-generated-files",
      ],
      modernization: [
        "msbuild-modernization",
        "directory-build-organization",
      ],
    },
  },
  "agentic-workflows": {
    outputDir: path.resolve(__dirname, "agentic-workflows/shared/compiled"),
    maxChars: 40000,
    knowledgeMap: {
      "build-failure-knowledge": [
        "common-build-errors",
        "sourcegen-analyzer-failures",
        "nuget-restore-failures",
        "sdk-workload-resolution",
        "binlog-failure-analysis",
      ],
      "pr-review-knowledge": [
        "msbuild-antipatterns",
        "msbuild-style-guide",
        "msbuild-modernization",
        "directory-build-organization",
        "check-bin-obj-clash",
        "incremental-build",
      ],
      "perf-audit-knowledge": [
        "build-perf-baseline",
        "build-perf-diagnostics",
        "incremental-build",
        "build-parallelism",
        "build-caching",
        "eval-performance",
      ],
    },
  },
};

function readSkill(skillName) {
  const skillPath = path.join(SKILLS_DIR, skillName, "SKILL.md");
  if (!fs.existsSync(skillPath)) {
    console.warn(`  ⚠ Skill not found: ${skillName} (${skillPath})`);
    return null;
  }

  let content = fs.readFileSync(skillPath, "utf-8");

  // Strip YAML frontmatter
  const frontmatterMatch = content.match(/^---\n([\s\S]*?)\n---\n/);
  if (frontmatterMatch) {
    content = content.slice(frontmatterMatch[0].length);
  }

  return content.trim();
}

function compileKnowledgeFile(outputName, skillNames, outputDir, maxChars) {
  const ext = ".lock.md";
  console.log(`  Compiling: ${outputName}${ext}`);

  const sections = [];
  let totalChars = 0;

  const header = `<!-- AUTO-GENERATED — DO NOT EDIT. Regenerate with: node src/dotnet-msbuild/build.js -->\n\n`;
  totalChars += header.length;

  for (const skillName of skillNames) {
    const content = readSkill(skillName);
    if (!content) continue;

    if (totalChars + content.length > maxChars) {
      console.warn(
        `    ⚠ Truncating ${skillName} — would exceed ${maxChars} char limit`
      );
      const remaining = maxChars - totalChars;
      if (remaining > 500) {
        sections.push(
          `## ${skillName}\n\n${content.slice(0, remaining)}\n\n[truncated]`
        );
        totalChars += remaining;
      }
      break;
    }

    sections.push(content);
    totalChars += content.length;
    console.log(
      `    ✓ ${skillName} (${content.length.toLocaleString()} chars)`
    );
  }

  const output = header + sections.join("\n\n---\n\n");
  const outputPath = path.join(outputDir, `${outputName}${ext}`);
  fs.writeFileSync(outputPath, output, "utf-8");
  console.log(
    `    → ${outputName}${ext} (${output.length.toLocaleString()} chars total)`
  );
}

function compileTarget(targetName, config) {
  console.log(`\n📦 Target: ${targetName}`);
  console.log(`   Output: ${config.outputDir}`);

  fs.mkdirSync(config.outputDir, { recursive: true });

  for (const [outputName, skillNames] of Object.entries(config.knowledgeMap)) {
    compileKnowledgeFile(
      outputName,
      skillNames,
      config.outputDir,
      config.maxChars
    );
  }
}

console.log(`Skills source: ${SKILLS_DIR}`);

for (const [name, config] of Object.entries(KNOWLEDGE_TARGETS)) {
  compileTarget(name, config);
}

console.log("\n✅ Build complete.");
