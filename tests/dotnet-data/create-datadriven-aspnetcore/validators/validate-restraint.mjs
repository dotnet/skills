#!/usr/bin/env node

import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import { fileURLToPath } from "node:url";

const [contract, rootArg] = process.argv.slice(2);
const contracts = new Set(["razor-static", "blazor-static"]);
if (!contracts.has(contract) || !rootArg) {
  console.error("Usage: node validate-restraint.mjs <razor-static|blazor-static> <root>");
  process.exit(2);
}

const root = path.resolve(rootArg);
const files = [];
const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const manifestPath = path.join(scriptDirectory, "static-fixture-baseline.json");
const ignoredDirectories = new Set(["bin", "obj", ".git", "node_modules"]);

let manifest;
try {
  manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
} catch (error) {
  console.error(`Could not load static fixture baseline ${manifestPath}: ${error.message}`);
  process.exit(2);
}

const baseline = manifest?.contracts?.[contract];
if (manifest?.schemaVersion !== 1 || manifest?.algorithm !== "sha256" || !Array.isArray(baseline?.files)) {
  console.error(`Static fixture baseline does not define ${contract} with schema version 1 and SHA-256 files`);
  process.exit(2);
}

function visit(directory) {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    if (entry.isDirectory() && ignoredDirectories.has(entry.name)) continue;
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) visit(full);
    else if (entry.isFile()) files.push({
      rel: path.relative(root, full).replaceAll("\\", "/"),
      bytes: fs.readFileSync(full),
      text: fs.readFileSync(full, "utf8"),
    });
  }
}

visit(root);
const failures = [];
const scopeRules = {
  "razor-static": {
    navigationBaseline: "Pages/Shared/_Layout.cshtml",
    allowedNew: new Set(["Pages/Terms.cshtml", "Pages/Terms.cshtml.cs"]),
  },
  "blazor-static": {
    navigationBaseline: "Components/Layout/NavMenu.razor",
    allowedNew: new Set([
      "Components/Pages/Reports.razor",
      "Components/Pages/Reports.razor.css",
    ]),
  },
};

function sha256(bytes) {
  return crypto.createHash("sha256").update(bytes).digest("hex");
}

function decodeUtf8(bytes) {
  try {
    return new TextDecoder("utf-8", { fatal: true }).decode(bytes);
  } catch {
    return null;
  }
}

function insertionIsInsideNavigation(before, after) {
  const beforeLower = before.toLowerCase();
  const afterLower = after.toLowerCase();
  return beforeLower.lastIndexOf("<nav") > beforeLower.lastIndexOf("</nav>") &&
    afterLower.includes("</nav>") &&
    />\s*$/.test(before) &&
    /^\s*</.test(after);
}

function hasOnlyTags(text, allowedTags) {
  const tags = [...text.matchAll(/<\/?\s*([a-z][\w:-]*)\b[^>]*>/gi)];
  return tags.length > 0 && tags.every((match) => allowedTags.has(match[1].toLowerCase()));
}

function hasForbiddenNavigationContent(text) {
  return /<!--|-->|@\*|\*@|<%|%>|\b(?:on[a-z]+|style)\s*=|(?:javascript|data)\s*:/i.test(text);
}

function validatesRazorNavigationInsertion(text) {
  const trimmed = text.trim();
  if (hasForbiddenNavigationContent(trimmed)) return false;
  if (!hasOnlyTags(trimmed, new Set(["li", "a", "span"]))) return false;
  if (!/^(?:<a\b[^>]*>[\s\S]*<\/a>|<li\b[^>]*>\s*<a\b[^>]*>[\s\S]*<\/a>\s*<\/li>)$/i.test(trimmed)) {
    return false;
  }
  const anchors = [...trimmed.matchAll(/<a\b([^>]*)>([\s\S]*?)<\/a>/gi)];
  if (anchors.length !== 1) return false;
  const [, attributes] = anchors[0];
  const linksToTerms =
    /\basp-page\s*=\s*(["'])\/Terms\1/i.test(attributes) ||
    /\bhref\s*=\s*(["'])(?:~?\/)?Terms\/?\1/i.test(attributes);
  const visibleText = trimmed.replace(/<[^>]+>/g, " ").replace(/\s+/g, " ").trim();
  return linksToTerms && visibleText === "Terms";
}

function validatesBlazorNavigationInsertion(text) {
  const trimmed = text.trim();
  if (hasForbiddenNavigationContent(trimmed)) return false;
  if (!hasOnlyTags(trimmed, new Set(["div", "navlink", "span"]))) return false;
  if (!/^(?:<NavLink\b[^>]*>[\s\S]*<\/NavLink>|<div\b[^>]*>\s*<NavLink\b[^>]*>[\s\S]*<\/NavLink>\s*<\/div>)$/i.test(trimmed)) {
    return false;
  }
  const links = [...trimmed.matchAll(/<NavLink\b([^>]*)>([\s\S]*?)<\/NavLink>/gi)];
  if (links.length !== 1) return false;
  const [, attributes] = links[0];
  const linksToReports = /\bhref\s*=\s*(["'])\/?reports\/?\1/i.test(attributes);
  const visibleText = trimmed.replace(/<[^>]+>/g, " ").replace(/\s+/g, " ").trim();
  return linksToReports && visibleText === "Reports";
}

function validateNavigationFile(entry, actual, contract) {
  if (typeof entry.contentBase64 !== "string") {
    failures.push(`static fixture baseline is missing navigation content: ${entry.path}`);
    return;
  }
  const baselineBytes = Buffer.from(entry.contentBase64, "base64");
  if (baselineBytes.toString("base64") !== entry.contentBase64 || sha256(baselineBytes) !== entry.sha256) {
    failures.push(`static fixture baseline has invalid navigation content: ${entry.path}`);
    return;
  }
  if (actual.bytes.equals(baselineBytes)) return;
  if (actual.bytes.length <= baselineBytes.length) {
    failures.push(`baseline navigation content was deleted or modified: ${entry.path}`);
    return;
  }

  const insertionLength = actual.bytes.length - baselineBytes.length;
  const validatesInsertion = contract === "razor-static"
    ? validatesRazorNavigationInsertion
    : validatesBlazorNavigationInsertion;
  for (let index = 0; index <= baselineBytes.length; index += 1) {
    if (!actual.bytes.subarray(0, index).equals(baselineBytes.subarray(0, index))) continue;
    if (!actual.bytes.subarray(index + insertionLength).equals(baselineBytes.subarray(index))) continue;

    const before = decodeUtf8(baselineBytes.subarray(0, index));
    const after = decodeUtf8(baselineBytes.subarray(index));
    const insertion = decodeUtf8(actual.bytes.subarray(index, index + insertionLength));
    if (
      before !== null &&
      after !== null &&
      insertion !== null &&
      insertionIsInsideNavigation(before, after) &&
      validatesInsertion(insertion)
    ) {
      return;
    }
  }

  failures.push(`baseline navigation allows only one ${contract === "razor-static" ? "Terms" : "Reports"} entry insertion: ${entry.path}`);
}

function validateFileScope() {
  const rules = scopeRules[contract];
  const actualByPath = new Map(files.map((file) => [file.rel, file]));
  const baselineByPath = new Map();

  for (const entry of baseline.files) {
    if (typeof entry?.path !== "string" || !/^[a-f0-9]{64}$/.test(entry?.sha256 ?? "")) {
      failures.push("static fixture baseline contains an invalid file entry");
      continue;
    }
    if (baselineByPath.has(entry.path)) {
      failures.push(`static fixture baseline contains a duplicate path: ${entry.path}`);
      continue;
    }
    baselineByPath.set(entry.path, entry);
    const actual = actualByPath.get(entry.path);
    if (!actual) {
      failures.push(`baseline file is missing (deleted or renamed): ${entry.path}`);
    } else if (entry.path === rules.navigationBaseline) {
      validateNavigationFile(entry, actual, contract);
    } else if (sha256(actual.bytes) !== entry.sha256) {
      failures.push(`baseline file changed outside allowed navigation: ${entry.path}`);
    }
  }

  for (const file of files) {
    if (!baselineByPath.has(file.rel) && !rules.allowedNew.has(file.rel)) {
      failures.push(`unrelated added file is forbidden: ${file.rel}`);
    }
  }
}

validateFileScope();
const joined = files.map((file) => file.text).join("\n");
const project = files.find((file) => file.rel.endsWith(".csproj"))?.text ?? "";
const program = files.find((file) => file.rel === "Program.cs")?.text ?? "";

function forbidPath(pattern, description) {
  if (files.some((file) => pattern.test(file.rel))) failures.push(description);
}

function forbidText(text, pattern, description) {
  if (pattern.test(text)) failures.push(description);
}

function requireArtifact(pattern, description) {
  const file = files.find((candidate) => pattern.test(candidate.rel));
  if (!file) failures.push(`${description} is missing`);
  else if (!file.text.trim()) failures.push(`${description} is empty`);
  return file?.text ?? "";
}

function activeRazor(text) {
  return text
    .replace(/@\*[\s\S]*?\*@/g, "")
    .replace(/<!--[\s\S]*?-->/g, "");
}

function hasEntityPrefixedRazorPage(relativePath) {
  const match = relativePath.match(/(?:^|\/)Pages\/(?:.*\/)?([^/]+)\.cshtml(?:\.cs)?$/);
  if (!match) return false;
  const prefix = match[1].match(/^(Products?|Categor(?:y|ies))/i)?.[0];
  if (!prefix) return false;
  const suffix = match[1].slice(prefix.length);
  return suffix === "" ||
    /^(?:Index|Details|Create|Edit|Delete)$/i.test(suffix) ||
    /^[A-Z0-9_.-]/.test(suffix);
}

function hasEntityPrefixedBlazorComponent(relativePath) {
  const match = relativePath.match(/(?:^|\/)Components\/Pages\/(?:.*\/)?([^/]+)\.razor(?:\.cs)?$/);
  if (!match) return false;
  const prefix = match[1].match(/^(Employees?|Departments?)/i)?.[0];
  if (!prefix) return false;
  const suffix = match[1].slice(prefix.length);
  return suffix === "" ||
    /^(?:Index|Details|Create|Edit|Delete)$/i.test(suffix) ||
    /^[A-Z0-9_.-]/.test(suffix);
}

forbidPath(/(?:^|\/)Migrations(?:\/|$)/i, "migrations are forbidden");
forbidPath(/(?:^|\/)[^/]*DbContext[^/]*\.cs$/i, "DbContext source is forbidden");
forbidPath(/(?:^|\/)(?:Controllers|Endpoints)(?:\/|$)/i, "controller or endpoint scaffolding is forbidden");
forbidPath(/\.http$/i, "HTTP workflow files are forbidden");
forbidText(project, /EntityFrameworkCore|Microsoft\.Data\.Sqlite/i, "data-access packages are forbidden");
forbidText(joined, /\b(?:DbContext|DbSet)\b|AddDbContext(?:Factory)?\s*<|UseSqlite\s*\(|EnsureCreated\s*\(/, "data-access code is forbidden");

if (contract === "razor-static") {
  forbidPath(
    /(?:^|\/)Pages\/(?:[^/]+\/)*(?:Products?|Categor(?:y|ies))(?:\/|$)/i,
    "Product or Category CRUD pages are forbidden",
  );
  if (files.some((file) => hasEntityPrefixedRazorPage(file.rel))) {
    failures.push("Product or Category entity-prefixed Razor Page files are forbidden");
  }
  const terms = activeRazor(requireArtifact(
    /(?:^|\/)Pages\/Terms\.cshtml$/,
    "Pages/Terms.cshtml",
  ));
  const termsModel = requireArtifact(
    /(?:^|\/)Pages\/Terms\.cshtml\.cs$/,
    "Pages/Terms.cshtml.cs",
  );
  if (!/^\s*@page(?:\s|$)/m.test(terms)) failures.push("Terms.cshtml must contain an active @page directive");
  if (!/^\s*@model\s+(?:global::)?MyApp\.Pages\.TermsModel\s*$/m.test(terms)) {
    failures.push("Terms.cshtml must reference MyApp.Pages.TermsModel");
  }
  if (!/\bclass\s+TermsModel\b[^:{]*:\s*(?:(?:global::)?Microsoft\.AspNetCore\.Mvc\.RazorPages\.)?PageModel\b/.test(termsModel)) {
    failures.push("Terms.cshtml.cs must define TermsModel as a PageModel");
  }
} else {
  forbidPath(
    /(?:^|\/)Components\/Pages\/(?:[^/]+\/)*(?:Employees?|Departments?)(?:\/|$)/i,
    "Employee or Department CRUD components are forbidden",
  );
  if (files.some((file) => hasEntityPrefixedBlazorComponent(file.rel))) {
    failures.push("Employee or Department entity-prefixed Blazor component files are forbidden");
  }
  const reports = activeRazor(requireArtifact(
    /(?:^|\/)Components\/Pages\/Reports\.razor$/,
    "Components/Pages/Reports.razor",
  ));
  const navigation = activeRazor(requireArtifact(
    /(?:^|\/)Components\/Layout\/NavMenu\.razor$/,
    "Components/Layout/NavMenu.razor",
  ));
  if (!/^\s*@page\s+["']\/reports["']\s*$/m.test(reports)) {
    failures.push('Reports.razor must contain an active @page "/reports" directive');
  }
  if (!/<PageTitle>\s*Reports\s*<\/PageTitle>/i.test(reports)) {
    failures.push("Reports.razor must define a Reports PageTitle");
  }
  if (!/<h1\b[^>]*>\s*Reports\s*<\/h1>/i.test(reports)) {
    failures.push("Reports.razor must contain a Reports heading");
  }
  const reportsContent = reports
    .replace(/^\s*@page[^\r\n]*$/gm, "")
    .replace(/<PageTitle>[\s\S]*?<\/PageTitle>/gi, "")
    .replace(/<h1\b[^>]*>[\s\S]*?<\/h1>/gi, "")
    .trim();
  if (!reportsContent) failures.push("Reports.razor must contain page content");
  if (!/<NavLink\b[^>]*\bhref\s*=\s*["']\/?reports["'][^>]*>/i.test(navigation)) {
    failures.push("NavMenu.razor must link to /reports");
  }
  forbidText(reports, /@rendermode\b/i, "the static Reports component must not add a render mode");
  forbidText(program, /AddInteractiveWebAssemblyComponents|AddInteractiveWebAssemblyRenderMode|AddInteractiveAutoRenderMode/i, "new render modes are forbidden");
}

if (failures.length > 0) {
  for (const failure of failures) console.error(failure);
  process.exit(1);
}

console.log(`PASS ${contract}: static-page restraint`);
