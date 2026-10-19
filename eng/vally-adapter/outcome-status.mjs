import { readFileSync, readdirSync } from "node:fs";
import { join, resolve } from "node:path";
import { pathToFileURL } from "node:url";

function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8"));
}

function resultFiles(root) {
  return readdirSync(root, { withFileTypes: true }).flatMap((entry) => {
    if (entry.name === "_agent-timeout-retry") return [];
    const path = join(root, entry.name);
    return entry.isDirectory() ? resultFiles(path)
      : entry.isFile() && entry.name === "results.json" ? [path] : [];
  });
}

export function outcomeStatus(root, entries) {
  if (!Array.isArray(entries) || entries.length === 0
    || entries.some((entry) => typeof entry?.name !== "string"
      || !/^[A-Za-z0-9._-]+$/.test(entry.name))
    || new Set(entries.map((entry) => entry.name)).size !== entries.length) {
    throw new Error("Missing, malformed or duplicate evaluation matrix entries");
  }
  const expectedArtifacts = entries.map((entry) => `vally-results-${entry.name}`).sort();
  const actualArtifacts = readdirSync(root).sort();
  if (JSON.stringify(expectedArtifacts) !== JSON.stringify(actualArtifacts)) {
    throw new Error("Missing or unexpected evaluation matrix artifacts");
  }
  const verdicts = [];
  const identities = new Set();
  let summaryInvalid = 0;
  for (const artifact of expectedArtifacts) {
    const dir = join(root, artifact);
    const summary = readJson(join(dir, "adapter-summary.json"));
    const files = resultFiles(dir);
    if (summary.schemaVersion !== 1 || summary.expectedManifestProvided !== true
      || !Number.isSafeInteger(summary.expectedEvalCount) || summary.expectedEvalCount < 1
      || summary.expectedEvalCount !== summary.observedEvalCount
      || summary.expectedEvalCount !== summary.writtenResultCount
      || summary.writtenResultCount !== files.length
      || summary.missingEvalCount !== 0 || summary.unexpectedEvalCount !== 0
      || !Number.isSafeInteger(summary.invalidEvalCount)
      || summary.invalidEvalCount < 0 || summary.invalidEvalCount > files.length
      || summary.measurementInvalidEvalCount !== 0) {
      throw new Error(`Incomplete evaluation accounting in ${artifact}`);
    }
    summaryInvalid += summary.invalidEvalCount;
    for (const path of files) {
      const data = readJson(path);
      if (data.schemaVersion !== 5 || !Array.isArray(data.verdicts)
        || data.verdicts.length !== 1) {
        throw new Error(`Malformed evaluation result: ${path}`);
      }
      const verdict = data.verdicts[0];
      const identity = [verdict.skillPath, verdict.skillName, verdict.model ?? data.model];
      if (identity.some((part) => typeof part !== "string" || !part)
        || identities.has(JSON.stringify(identity))) {
        throw new Error(`Missing or duplicate evaluation identity: ${path}`);
      }
      identities.add(JSON.stringify(identity));
      if (!["VALID_PASS", "VALID_NO_CHANGE", "VALID_REGRESSION", "INVALID_INCONCLUSIVE"]
        .includes(verdict.state)) {
        throw new Error(`Unknown evaluation verdict: ${path}`);
      }
      if (typeof verdict.conclusive !== "boolean" || typeof verdict.underpowered !== "boolean") {
        throw new Error(`Missing or malformed evaluation validity: ${path}`);
      }
      verdicts.push(verdict);
    }
  }
  const regressions = verdicts.filter((verdict) => verdict.state === "VALID_REGRESSION").length;
  const activationFailures = verdicts.filter((verdict) =>
    verdict.activationContract?.evaluated !== false
    && verdict.activationContract?.passed === false).length;
  const invalid = verdicts.filter((verdict) =>
    verdict.state === "INVALID_INCONCLUSIVE" || verdict.conclusive === false
    || verdict.underpowered === true).length;
  if (summaryInvalid !== verdicts.filter((verdict) =>
    verdict.state === "INVALID_INCONCLUSIVE").length) {
    throw new Error("Evaluation summary and invalid verdict accounting disagree");
  }
  if (regressions || activationFailures) {
    return { state: "failure",
      description: `Evaluation: ${regressions} objective regressions, ${activationFailures} activation failures, ${invalid} invalid` };
  }
  if (invalid) {
    return { state: "error", description: `Evaluation inconclusive: ${invalid} invalid or underpowered results` };
  }
  const unchanged = verdicts.filter((verdict) => verdict.state === "VALID_NO_CHANGE").length;
  return { state: "success", description: unchanged
    ? `Evaluation complete: ${unchanged} unproven; no objective/activation regressions`
    : "Evaluation passed: all measured results improved" };
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try {
    console.log(JSON.stringify(outcomeStatus(process.argv[2],
      JSON.parse(process.env.EXPECTED_ENTRIES ?? "null"))));
  } catch (error) {
    console.error(`Evaluation outcome accounting failed: ${error.message}`);
    process.exitCode = 1;
  }
}
