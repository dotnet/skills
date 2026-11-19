#!/usr/bin/env node

import { spawn, spawnSync } from "node:child_process";
import { randomUUID } from "node:crypto";
import { copyFileSync, existsSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { splitVallyCommand } from "../vally-adapter/adapt.mjs";

const toolsDir = dirname(fileURLToPath(import.meta.url));
const adapterDir = resolve(toolsDir, "../vally-adapter");
const defaultRoot = resolve(toolsDir, "../..");
const namePattern = /^[A-Za-z0-9][A-Za-z0-9._-]*$/;
const signalExitCodes = { SIGHUP: 129, SIGINT: 130, SIGTERM: 143 };

function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8"));
}

function interruptedError(signal) {
  const error = new Error(`Interrupted by ${signal}; no further evaluations will run.`);
  error.interrupted = true;
  error.signal = signal;
  return error;
}

function terminate(child, signal) {
  if (!child.pid) return;
  if (process.platform === "win32") {
    if (child.exitCode !== null || child.signalCode !== null) return;
    const result = spawnSync("taskkill", ["/PID", String(child.pid), "/T", "/F"], { encoding: "utf8" });
    if (result.error || result.status !== 0) {
      console.error(`Could not terminate evaluation process tree ${child.pid}: ${result.error?.message ?? result.stderr}`);
    }
  } else {
    // Signal the detached group: workers can outlive the group leader.
    try {
      process.kill(-child.pid, signal);
    } catch (error) {
      if (error.code !== "ESRCH") throw error;
    }
  }
}

// Signal handlers stay installed for the whole invocation, including in-process
// phases between child processes. A repeated signal force-kills the active
// process tree and removes generated experiments before exiting immediately.
function createSession() {
  const session = { signal: undefined, active: new Set(), files: new Set() };
  const handlers = Object.keys(signalExitCodes).map(name => [name, () => {
    if (session.signal) {
      for (const entry of session.active) entry.force();
      for (const file of session.files) rmSync(file, { force: true });
      process.exit(signalExitCodes[name]);
    }
    session.signal = name;
    console.error(`\n${name} received: stopping the active evaluation step (repeat to force exit).`);
    for (const entry of session.active) entry.stop();
  }]);
  for (const [name, handler] of handlers) process.on(name, handler);
  session.check = () => {
    if (session.signal) throw interruptedError(session.signal);
  };
  session.dispose = () => {
    for (const [name, handler] of handlers) process.removeListener(name, handler);
  };
  return session;
}

function run(label, bin, args, { root, env, session }, timeoutMinutes) {
  session.check();
  return new Promise((resolveRun, reject) => {
    const child = spawn(bin, args, {
      cwd: root, env, stdio: "inherit", detached: process.platform !== "win32",
    });
    let reason;
    let escalation;
    const entry = {
      stop() {
        reason ??= "stopped";
        terminate(child, "SIGTERM");
        escalation ??= setTimeout(() => terminate(child, "SIGKILL"), 10_000);
      },
      force: () => terminate(child, "SIGKILL"),
    };
    session.active.add(entry);
    const timer = timeoutMinutes ? setTimeout(() => {
      reason = "timeout";
      entry.stop();
    }, timeoutMinutes * 60_000) : undefined;
    function cleanup() {
      clearTimeout(timer);
      clearTimeout(escalation);
      session.active.delete(entry);
    }
    child.once("error", error => { cleanup(); reject(error); });
    child.once("close", (status, signal) => {
      if (reason) terminate(child, "SIGKILL");
      cleanup();
      if (session.signal) {
        reject(interruptedError(session.signal));
      } else if (reason === "timeout") {
        reject(new Error(`${label} timed out after ${timeoutMinutes} minutes; its process tree was stopped.`));
      } else if (signal) {
        reject(new Error(`${label} was terminated by ${signal}.`));
      } else {
        resolveRun(status);
      }
    });
  });
}

async function requireSuccess(label, bin, args, context, timeoutMinutes) {
  const status = await run(label, bin, args, context, timeoutMinutes);
  if (status !== 0) throw new Error(`${label} exited ${status}`);
}

function hasGraderError(result) {
  return result?.status === "error" || (result?.details?.some(hasGraderError) ?? false);
}

// Vally exits 1 for any execution, grader or cleanup error. Treat that exit as
// explained only when the shared retry recovered every executor error and the
// merged evidence contains no remaining trial or grader error in any arm.
function executorErrorsRecovered(runDir, retrySummaryPath) {
  const summary = readJson(retrySummaryPath);
  if (!(summary.recoveredSlotCount > 0) || summary.unresolvedSlotCount !== 0 || summary.skippedReason) {
    return false;
  }
  for (const entry of readdirSync(runDir, { withFileTypes: true })) {
    const path = join(runDir, entry.name, "results.jsonl");
    if (!entry.isDirectory() || !existsSync(path)) continue;
    for (const line of readFileSync(path, "utf8").split("\n")) {
      if (!line.trim()) continue;
      const record = JSON.parse(line);
      if (record?.type === "trial-result"
        && (record.status === "error" || (record.grade && hasGraderError(record.grade)))) {
        return false;
      }
    }
  }
  return true;
}

function reconcile(outputRoot, expected, summaryPath) {
  const summary = readJson(summaryPath);
  if (summary.expectedManifestProvided !== true
    || summary.expectedEvalCount !== expected.length
    || summary.observedEvalCount !== expected.length
    || summary.writtenResultCount !== expected.length
    || summary.missingEvalCount !== 0
    || summary.unexpectedEvalCount !== 0
    || summary.measurementInvalidEvalCount !== 0) {
    throw new Error(`Result accounting failed: ${JSON.stringify(summary)}`);
  }
  return expected.map(({ plugin, skill }) => {
    const result = readJson(join(outputRoot, plugin, skill, "results.json"));
    if (result.verdicts?.length !== 1) {
      throw new Error(`Expected one verdict for ${plugin}/${skill}`);
    }
    return result.verdicts[0];
  });
}

export async function runSkillEvals({
  root = defaultRoot, args = process.argv.slice(2), env = process.env,
} = {}) {
  if (args.length === 1 && ["--help", "-h"].includes(args[0])) {
    console.log(`Usage: ./eng/run-skill-evals.sh [plugin [skill]]

Requires Node.js 22.14+ and npm ci --prefix eng/evaluation-tools.
Environment: WORKERS (default 5), EXPERIMENT_FILE, RESULTS_DIR, GITHUB_TOKEN.
VALLY may override the pinned CLI explicitly; overrides are not CI-parity runs.
Raw evidence and per-plugin accounting are retained under RESULTS_DIR/_experiment.
Invalid/incomplete evidence or an unexplained harness exit fails; measured skill
outcomes and underpowered evals are informational, as in CI.`);
    return 0;
  }
  const [major, minor] = process.versions.node.split(".").map(Number);
  if (major < 22 || (major === 22 && minor < 14)) {
    throw new Error("Node.js 22.14 or newer is required by the pinned Vally toolchain.");
  }
  if (args.length > 2 || args.some(name => !namePattern.test(name) || name.includes(".."))) {
    throw new Error("Expected [plugin [skill]] as single directory names (no traversal or globs).");
  }
  const [plugin, skill] = args;
  if (skill?.startsWith("agent.") || plugin === "agentic-workflows") {
    throw new Error("Custom-agent and workflow evals require the native skill-validator evaluate lane.");
  }
  const workers = env.WORKERS ?? "5";
  if (!/^[1-9]\d*$/.test(workers) || !Number.isSafeInteger(Number(workers))) {
    throw new Error("WORKERS must be a positive integer.");
  }
  const session = createSession();
  try {
    return await execute({ root, env, plugin, skill, workers, session });
  } finally {
    session.dispose();
  }
}

async function execute({ root, env, plugin, skill, workers, session }) {
  let vally;
  try {
    const { dependencies } = readJson(join(toolsDir, "package.json"));
    for (const [name, version] of Object.entries(dependencies)) {
      const installed = readJson(join(toolsDir, "node_modules", name, "package.json"));
      if (installed.version !== version) throw new Error(`${name}: expected ${version}, installed ${installed.version}`);
    }
    vally = await import("@microsoft/vally");
  } catch (error) {
    throw new Error(`Cannot load the pinned evaluation tools (${error.message}). Run npm ci --prefix eng/evaluation-tools.`, { cause: error });
  }
  const base = resolve(root, env.EXPERIMENT_FILE ?? "dotnet-skills.experiment.yaml");
  // The generator and adapter use repository-relative identities. Reject other
  // layouts rather than silently running paths relative to a different root.
  if (dirname(base) !== resolve(root)) {
    throw new Error("EXPERIMENT_FILE must be in the repository root, beside tests/ and plugins/.");
  }
  const project = await vally.ProjectContext.load(root);
  const resolveOptions = {
    environments: project.config.environments,
    suiteParams: project.config.params,
    evalFilters: plugin ? [`tests/${plugin}/${skill ?? "**"}/eval.yaml`] : [],
  };
  const planned = await vally.resolveExperiment(base, resolveOptions);
  if (planned.baseline !== "baseline"
    || planned.variantNames.length !== 2
    || !planned.variantNames.includes("skilled")) {
    throw new Error("The base experiment must declare only baseline and skilled variants.");
  }
  const groups = new Map();
  const workflowPackages = [];
  for (const plan of planned.plans.filter(item => item.variant === "skilled")) {
    const path = plan.relativeEvalFile;
    // A full run's glob also matches workflow packages, which have no skill
    // under plugins/ and run in the native lane, as in CI's skill manifest.
    if (!plugin && path.startsWith("tests/agentic-workflows/")) {
      workflowPackages.push(path);
      continue;
    }
    const match = /^tests\/([^/]+)\/([^/]+)\/eval\.yaml$/.exec(path);
    if (!match || match[2].startsWith("agent.") || match[1] === "agentic-workflows") {
      throw new Error(`Not a skill eval: ${path}`);
    }
    const [, pluginName, skillName] = match;
    if (![pluginName, skillName].every(name => namePattern.test(name) && !name.includes(".."))) {
      throw new Error(`Invalid skill eval identity: ${path}`);
    }
    const defaults = plan.effectiveSpec.defaults ?? {};
    if (!defaults.model || !defaults.judge_model) {
      throw new Error(`Set explicit model and judge_model defaults for ${path}.`);
    }
    const group = groups.get(pluginName) ?? [];
    group.push({ path, plugin: pluginName, skill: skillName, model: defaults.model, judge: defaults.judge_model });
    groups.set(pluginName, group);
  }
  if (groups.size === 0) throw new Error("No skill evals selected.");
  if (workflowPackages.length > 0) {
    console.log(`Skipping ${workflowPackages.length} workflow-package eval(s); they run in the native skill-validator lane.`);
  }
  for (const [name, group] of groups) {
    if (group.some(item => item.model !== group[0].model || item.judge !== group[0].judge)) {
      throw new Error(`Conflicting model/judge defaults in ${name}; select one skill or set experiment overrides.`);
    }
  }

  const childEnv = { ...env };
  if (!childEnv.GITHUB_TOKEN) {
    childEnv.GITHUB_TOKEN = childEnv.COPILOT_GITHUB_TOKEN || childEnv.GH_TOKEN;
    if (!childEnv.GITHUB_TOKEN) {
      const auth = spawnSync("gh", ["auth", "token"], { env: childEnv, encoding: "utf8" });
      if (auth.error || auth.status !== 0 || !auth.stdout.trim()) {
        throw new Error("Set GITHUB_TOKEN with Copilot access or authenticate with gh auth login.");
      }
      childEnv.GITHUB_TOKEN = auth.stdout.trim();
    }
  }
  const context = { root, env: childEnv, session };
  const command = env.VALLY ?? `"${process.execPath}" "${join(toolsDir, "vally.mjs")}"`;
  if (env.VALLY) console.warn("VALLY override: this run does not verify the pinned CI executable.");
  const { bin, prefix } = splitVallyCommand(command);
  // Planning is in-process; stop before touching prior results if interrupted.
  session.check();
  const outputRoot = resolve(root, env.RESULTS_DIR ?? "eval-results");
  const invocation = join(outputRoot, "_experiment", `local-${randomUUID()}`);
  mkdirSync(invocation, { recursive: true });

  let exitCode = 0;
  const verdicts = [];
  for (const [name, expected] of groups) {
    // Plugins skipped by an interrupt keep their prior verdicts.
    session.check();
    // Remove only this plugin's selected verdict files before it starts. Never
    // recursively delete its directory (it may hold unrelated or contributor-owned files).
    for (const item of expected) rmSync(join(outputRoot, item.plugin, item.skill, "results.json"), { force: true });
    const groupDir = join(invocation, name);
    mkdirSync(groupDir, { recursive: true });
    const manifest = join(groupDir, "expected-evals.json");
    writeFileSync(manifest, JSON.stringify(expected.map(item => item.path), null, 2));
    const generated = join(root, `_experiment.${name}.${randomUUID()}.yaml`);
    session.files.add(generated);
    let failure = 0;
    try {
      await requireSuccess("Plugin experiment generation", process.execPath, [
        join(adapterDir, "gen-experiment.mjs"), "--plugin", name,
        "--base", base, "--content-root", root, "--out", generated,
      ], context);
      // Resolve the generated file with the production loader before launching
      // any trials, including validation of the injected plugin surface.
      await vally.resolveExperiment(generated, {
        ...resolveOptions, evalFilters: expected.map(item => item.path),
      });
      copyFileSync(generated, join(groupDir, "experiment.yaml"));
      console.log(`Running ${name}: ${expected.length} eval(s), workers=${workers}`);
      const rawRoot = join(groupDir, "runs");
      mkdirSync(rawRoot);
      const status = await run("Vally experiment", bin, [
        ...prefix, "experiment", "run", generated,
        ...expected.flatMap(item => ["--eval-filter", item.path]),
        "--output-dir", rawRoot, "--workers", workers,
      ], context, 155);
      failure = status;
      const runs = readdirSync(rawRoot, { withFileTypes: true }).filter(entry => entry.isDirectory());
      if (runs.length !== 1) throw new Error(`Expected one fresh run directory, found ${runs.length}.`);
      const runDir = join(rawRoot, runs[0].name);
      const retrySummary = join(groupDir, "executor-retry-summary.json");
      const retryStatus = await run("Executor retry", process.execPath, [
        join(adapterDir, "retry-executor-timeouts.mjs"),
        "--experiment-file", generated, "--experiment-dir", runDir,
        "--retry-output-dir", join(groupDir, "retries"),
        "--summary", retrySummary,
        "--vally", command, "--workers", workers,
        "--max-groups", "3", "--max-attempts-per-group", "2",
      ], context, 45);
      if (retryStatus !== 0) {
        console.error(`Executor retry exited ${retryStatus}; transient executor errors may remain unrecovered.`);
        failure ||= retryStatus;
      } else if (status === 1 && executorErrorsRecovered(runDir, retrySummary)) {
        console.warn("Experiment exited 1 only for executor errors that the shared retry recovered; result accounting decides the outcome.");
        failure = 0;
      }
      if (status !== 0 && failure) {
        console.error(`Experiment exited ${status}; preserving and adapting partial evidence, not reporting success.`);
      }
      // Never reconcile a summary left by an earlier group or invocation.
      const adapterSummary = join(outputRoot, "adapter-summary.json");
      rmSync(adapterSummary, { force: true });
      await requireSuccess("Result adapter", process.execPath, [
        join(adapterDir, "adapt.mjs"), "--experiment-dir", runDir,
        "--output-root", outputRoot, "--repo-root", root,
        "--expected-evals", manifest, "--vally", command,
        "--model", expected[0].model, "--judge-model", expected[0].judge,
      ], context, 60);
      if (!existsSync(adapterSummary)) throw new Error("Result adapter did not write adapter-summary.json.");
      const summaryPath = join(groupDir, "adapter-summary.json");
      copyFileSync(adapterSummary, summaryPath);
      verdicts.push(...reconcile(outputRoot, expected, summaryPath));
    } catch (error) {
      if (error.interrupted) throw error;
      console.error(`${name}: ${error.message}`);
      failure ||= 1;
    } finally {
      exitCode ||= failure;
      rmSync(generated, { force: true });
      session.files.delete(generated);
    }
  }
  session.check();
  console.log(`\nReconciled ${verdicts.length} of ${[...groups.values()].flat().length} selected eval(s).`);
  for (const state of [...new Set(verdicts.map(verdict => verdict.state))]) {
    console.log(`  ${state}: ${verdicts.filter(verdict => verdict.state === state).length}`);
  }
  console.log(`Results: ${outputRoot}\nRaw evidence and accounting: ${invocation}`);
  return exitCode;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    process.exitCode = await runSkillEvals();
  } catch (error) {
    console.error(error.message);
    process.exitCode = signalExitCodes[error.signal] ?? 1;
  }
}
