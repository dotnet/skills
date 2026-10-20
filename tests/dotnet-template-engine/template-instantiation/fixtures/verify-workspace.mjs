import fs from "node:fs";
import path from "node:path";

const config = JSON.parse(process.argv[2] ?? "{}");
const root = process.cwd();
const normalize = value => value.replaceAll("\\", "/").replace(/^\.\/+/, "").replace(/\/+$/, "");

function listFiles(relativeRoot = "", options = {}) {
  const absoluteRoot = path.join(root, relativeRoot);
  if (!fs.existsSync(absoluteRoot)) {
    return [];
  }

  const files = [];
  const visit = relativeDirectory => {
    const absoluteDirectory = path.join(root, relativeDirectory);
    for (const entry of fs.readdirSync(absoluteDirectory, { withFileTypes: true })) {
      const relativePath = normalize(path.join(relativeDirectory, entry.name));
      if (entry.isSymbolicLink()) {
        throw new Error(`Unexpected symbolic link: ${relativePath}`);
      }
      if (entry.isDirectory()) {
        const isHarnessDirectory =
          options.ignoreHarness &&
          (relativePath === ".eval" ||
            relativePath === ".xdg-data" ||
            relativePath === ".local-app-data" ||
            relativePath === "template-instantiation" ||
            relativePath === "dotnet-template-engine");
        const isBuildOutput = options.ignoreBuildOutputs && (entry.name === "bin" || entry.name === "obj");
        if (entry.name !== ".git" && !isHarnessDirectory && !isBuildOutput) {
          visit(relativePath);
        }
      } else if (entry.isFile()) {
        if (!(options.ignoreHarness && relativePath === "global.json")) {
          files.push(relativePath);
        }
      }
    }
  };

  visit(normalize(relativeRoot));
  return files.sort();
}

function fail(message) {
  console.error(message);
  process.exit(1);
}

const files = listFiles("", { ignoreHarness: true, ignoreBuildOutputs: true });
const allowedPaths = new Set((config.allowedPaths ?? []).map(normalize));
const allowedRoots = (config.allowedRoots ?? []).map(normalize);
const isAllowed = file =>
  allowedPaths.has(file) ||
  allowedRoots.some(allowedRoot => file === allowedRoot || file.startsWith(`${allowedRoot}/`));

const unexpectedFiles = files.filter(file => !isAllowed(file));
if (unexpectedFiles.length > 0) {
  fail(`Files were written outside the requested destinations: ${unexpectedFiles.join(", ")}`);
}

for (const requiredFile of config.requiredFiles ?? []) {
  const normalized = normalize(requiredFile);
  if (!fs.existsSync(path.join(root, normalized))) {
    fail(`Required file is missing: ${normalized}`);
  }
}

for (const forbiddenFile of config.forbiddenFiles ?? []) {
  const normalized = normalize(forbiddenFile);
  if (fs.existsSync(path.join(root, normalized))) {
    fail(`Forbidden file exists: ${normalized}`);
  }
}

function compareTrees(actualRoot, baselineRoot) {
  const options = { ignoreBuildOutputs: true };
  const actualFiles = listFiles(actualRoot, options).map(file => normalize(path.relative(actualRoot, file)));
  const baselineFiles = listFiles(baselineRoot, options).map(file => normalize(path.relative(baselineRoot, file)));
  if (JSON.stringify(actualFiles) !== JSON.stringify(baselineFiles)) {
    fail(
      `The existing destination changed its file set. Actual: ${actualFiles.join(", ")}; ` +
      `expected: ${baselineFiles.join(", ")}`
    );
  }

  for (const relativeFile of baselineFiles) {
    const actual = fs.readFileSync(path.join(root, actualRoot, relativeFile));
    const baseline = fs.readFileSync(path.join(root, baselineRoot, relativeFile));
    if (!actual.equals(baseline)) {
      fail(`The existing destination changed: ${normalize(path.join(actualRoot, relativeFile))}`);
    }
  }
}

for (const comparison of config.unchangedTrees ?? []) {
  compareTrees(normalize(comparison.actual), normalize(comparison.baseline));
}

if (config.centralPackages) {
  const projectText = fs.readFileSync(path.join(root, normalize(config.centralPackages.project)), "utf8");
  const propsText = fs.readFileSync(path.join(root, normalize(config.centralPackages.props)), "utf8");
  const packageReferences = [
    ...projectText.matchAll(/<PackageReference\s+Include="([^"]+)"([^>]*)>/g)
  ];
  const centralVersions = new Set(
    [...propsText.matchAll(/<PackageVersion\s+Include="([^"]+)"/g)].map(match => match[1])
  );

  if (packageReferences.length === 0) {
    fail("The generated test project has no PackageReference items.");
  }
  const inlineVersions = packageReferences
    .filter(match => /\bVersion\s*=/.test(match[2]))
    .map(match => match[1]);
  if (inlineVersions.length > 0) {
    fail(`PackageReference items still have inline versions: ${inlineVersions.join(", ")}`);
  }
  const missingCentralVersions = packageReferences
    .map(match => match[1])
    .filter(packageId => !centralVersions.has(packageId));
  if (missingCentralVersions.length > 0) {
    fail(`Central versions are missing for: ${missingCentralVersions.join(", ")}`);
  }
}

console.log(`Verified ${files.length} workspace files and destination boundaries.`);
