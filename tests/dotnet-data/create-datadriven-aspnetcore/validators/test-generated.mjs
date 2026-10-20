#!/usr/bin/env node

import fs from "node:fs";
import path from "node:path";
import os from "node:os";
import crypto from "node:crypto";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const scenarioDirectory = path.dirname(scriptDirectory);
const validator = path.join(scriptDirectory, "validate-generated.mjs");
const restraintValidator = path.join(scriptDirectory, "validate-restraint.mjs");
const preferredBase = "Q:\\copilot\\26bbc64f-ad82-47bf-a29d-42ec7aead779\\validator-tests";
const fallbackBase = path.join(os.tmpdir(), "dotnet-skills-create-datadriven-validator-tests");

function usableBase(candidate) {
  try {
    fs.mkdirSync(candidate, { recursive: true });
    fs.accessSync(candidate, fs.constants.W_OK);
    return true;
  } catch {
    return false;
  }
}

const base = process.platform === "win32" && usableBase(preferredBase) ? preferredBase : fallbackBase;
const scenarioRelativeBase = path.relative(scenarioDirectory, base);
if (
  !path.isAbsolute(base) ||
  (scenarioRelativeBase !== ".." &&
    !scenarioRelativeBase.startsWith(`..${path.sep}`) &&
    !path.isAbsolute(scenarioRelativeBase))
) {
  throw new Error(`Validator workspace must be outside the tracked scenario: ${base}`);
}
fs.mkdirSync(base, { recursive: true });
const runRoot = path.join(base, `run-${Date.now()}-${process.pid}`);
fs.mkdirSync(runRoot, { recursive: true });

const cases = [
  { contract: "razor-pages", fixture: "razor-pages-crud", patch: "add-razor-pages-crud.patch" },
  { contract: "todo-api", fixture: "minimal-api", patch: "add-todo-minimal-api-crud.patch" },
  { contract: "mvc", fixture: "mvc-existing-dbcontext", patch: "add-mvc-book-author-crud.patch" },
  { contract: "parent-api", fixture: "minimal-api-parent-fk", patch: "add-parent-child-minimal-api-crud.patch" },
  { contract: "blazor", fixture: "blazor-crud", patch: "add-blazor-employee-department-crud.patch" },
];
const restraintCases = [
  { contract: "razor-static", fixture: "razor-pages-crud", patch: "add-terms-page.patch" },
  { contract: "blazor-static", fixture: "blazor-crud", patch: "add-reports-component.patch" },
];
const scopeCases = [
  { contract: "complete-api-scope", fixture: "already-complete" },
  { contract: "csv-dormancy-scope", fixture: "non-web", patch: "add-csv-report-writer.patch" },
];

const failures = [];
const goldenRoots = new Map();

function cleanupArtifacts() {
  fs.rmSync(runRoot, { recursive: true, force: true });
  try {
    fs.rmdirSync(base);
  } catch {
  }
}

function copySource(source, destination) {
  fs.cpSync(source, destination, {
    recursive: true,
    filter(current) {
      const relative = path.relative(source, current);
      return !relative.split(path.sep).some((segment) => segment === "bin" || segment === "obj" || segment === ".git");
    },
  });
}

function run(command, args, options = {}) {
  return spawnSync(command, args, {
    encoding: "utf8",
    windowsHide: true,
    ...options,
  });
}

function applyPatch(work, patch) {
  const initialized = run("git", ["init", "--quiet"], { cwd: work });
  if (initialized.status !== 0) return initialized;
  return run("git", ["-c", "core.autocrlf=false", "apply", "--whitespace=nowarn", patch], { cwd: work });
}

function validatorResult(contract, root) {
  return run(process.execPath, [validator, contract, root]);
}

function restraintValidatorResult(contract, root) {
  return run(process.execPath, [restraintValidator, contract, root]);
}

function reportPass(contract, name) {
  console.log(`PASS ${contract}: ${name}`);
}

function reportUnexpected(contract, name, result, expectation) {
  const details = [result.stdout, result.stderr].filter(Boolean).join("\n").trim();
  failures.push(`${contract}: ${name} (expected ${expectation}, exit ${result.status})${details ? `\n${details}` : ""}`);
  console.error(`FAIL ${contract}: ${name}`);
}

function expectPass(contract, root, name) {
  const result = validatorResult(contract, root);
  if (result.status === 0) reportPass(contract, name);
  else reportUnexpected(contract, name, result, "pass");
}

function expectFailure(contract, root, name) {
  const result = validatorResult(contract, root);
  if (result.status !== 0) reportPass(contract, `${name} rejected`);
  else reportUnexpected(contract, name, result, "failure");
}

function materializeGolden(item) {
  const work = path.join(runRoot, `golden-${item.contract}`);
  const fixtureRoot = path.join(work, "fixtures", item.fixture);
  fs.mkdirSync(path.dirname(fixtureRoot), { recursive: true });
  copySource(path.join(scenarioDirectory, "fixtures", item.fixture), fixtureRoot);
  const patch = path.join(scenarioDirectory, "references", item.patch);
  const applied = applyPatch(work, patch);
  if (applied.status !== 0) {
    throw new Error(`Could not apply ${item.patch}:\n${applied.stderr || applied.stdout}`);
  }
  goldenRoots.set(item.contract, fixtureRoot);
  expectPass(item.contract, fixtureRoot, "golden patch");
}

function materializeRestraintGolden(item) {
  const work = path.join(runRoot, `golden-${item.contract}`);
  const fixtureRoot = path.join(work, "fixtures", item.fixture);
  fs.mkdirSync(path.dirname(fixtureRoot), { recursive: true });
  copySource(path.join(scenarioDirectory, "fixtures", item.fixture), fixtureRoot);
  const patch = path.join(scenarioDirectory, "references", item.patch);
  const applied = applyPatch(work, patch);
  if (applied.status !== 0) throw new Error(`Could not apply ${item.patch}:\n${applied.stderr || applied.stdout}`);
  goldenRoots.set(item.contract, fixtureRoot);
  const result = restraintValidatorResult(item.contract, fixtureRoot);
  if (result.status === 0) reportPass(item.contract, "golden patch");
  else reportUnexpected(item.contract, "golden patch", result, "pass");
}

function materializeScopeGolden(item) {
  const work = path.join(runRoot, `golden-${item.contract}`);
  const fixtureRoot = path.join(work, "fixtures", item.fixture);
  fs.mkdirSync(path.dirname(fixtureRoot), { recursive: true });
  copySource(path.join(scenarioDirectory, "fixtures", item.fixture), fixtureRoot);
  if (item.patch) {
    const patch = path.join(scenarioDirectory, "references", item.patch);
    const applied = applyPatch(work, patch);
    if (applied.status !== 0) throw new Error(`Could not apply ${item.patch}:\n${applied.stderr || applied.stdout}`);
  }
  goldenRoots.set(item.contract, fixtureRoot);
  expectScopePass(item.contract, fixtureRoot, "golden file set");
}

function stagedFiles(root) {
  const result = [];
  const visit = (directory) => {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
      if (entry.isDirectory() && [".git", "bin", "obj"].includes(entry.name)) continue;
      const full = path.join(directory, entry.name);
      if (entry.isDirectory()) visit(full);
      else if (entry.isFile()) result.push(path.relative(root, full).replaceAll("\\", "/"));
    }
  };
  visit(root);
  return result.sort();
}

function digest(root, relativeFile) {
  return crypto.createHash("sha256")
    .update(fs.readFileSync(path.join(root, ...relativeFile.split("/"))))
    .digest("hex");
}

function scopeIsValid(contract, root) {
  const files = stagedFiles(root);
  if (contract === "complete-api-scope") {
    return JSON.stringify(files) === JSON.stringify([
      "CompleteTodoApi.csproj",
      "CompleteTodoEndpoints.cs",
    ]) &&
      digest(root, "CompleteTodoEndpoints.cs") === "d40d2d1e48974e6396fdbff3c8046bea8ce5f424834f1fe7786b09545b581d87" &&
      digest(root, "CompleteTodoApi.csproj") === "85504278f61edf39ab8321c61f47b8532cdcc0efec2a29f7b4a42086fbe63c93";
  }
  return JSON.stringify(files) === JSON.stringify([
    "CsvReportWriter.cs",
    "Program.cs",
    "Utility.csproj",
    "Verifier/Program.cs",
    "Verifier/Verifier.csproj",
  ]) &&
    digest(root, "Program.cs") === "62dcf26c0762a4876649255e87a370d622bb754fe04891d68642e10a303f04d6" &&
    digest(root, "Utility.csproj") === "b922ca987189c1031e4ccc3285c48e45c7ea3fb563b7a560a587250936416f43" &&
    digest(root, "Verifier/Program.cs") === "10bbb30e45decc1ba0e3bc3b63bc25424956eb7a5fcf8e1d30bf87e2fc673d83" &&
    digest(root, "Verifier/Verifier.csproj") === "4de9357b55256ab43e871f5b619456ad0b25af8dd7a9cfacbd943ac470a787b4";
}

function expectScopePass(contract, root, name) {
  if (scopeIsValid(contract, root)) reportPass(contract, name);
  else failures.push(`${contract}: ${name} (expected exact staged file set)`);
}

function scopeMutation(contract, name, mutate) {
  try {
    const root = cloneForMutation(contract, name);
    mutate(root);
    if (!scopeIsValid(contract, root)) reportPass(contract, `${name} rejected`);
    else failures.push(`${contract}: ${name} (expected failure)`);
  } catch (error) {
    failures.push(`${contract}: ${name} mutation failed: ${error.message}`);
    console.error(`FAIL ${contract}: ${name} mutation setup`);
  }
}

function scopeMutations() {
  scopeMutation("complete-api-scope", "duplicate endpoint source", (root) =>
    write(
      root,
      "DuplicateTodoEndpoints.cs",
      "public static class DuplicateTodoEndpoints { public static void MapDuplicateTodoEndpoints() { } }\n",
    ));
  scopeMutation("complete-api-scope", "added endpoint helper", (root) =>
    write(
      root,
      "TodoEndpointValidationHelper.cs",
      "public static class TodoEndpointValidationHelper { public static bool IsValid(string title) => !string.IsNullOrWhiteSpace(title); }\n",
    ));
  scopeMutation("complete-api-scope", "added endpoint configuration", (root) =>
    write(root, "appsettings.json", "{\"TodoEndpoints\":{\"Enabled\":true}}\n"));

  scopeMutation("csv-dormancy-scope", "non-EF AppDbContext", (root) =>
    write(root, "AppDbContext.cs", "namespace Utility; public sealed class AppDbContext { }\n"));
  scopeMutation("csv-dormancy-scope", "controller-like source", (root) =>
    write(
      root,
      "ReportsController.cs",
      "namespace Utility; public sealed class ReportsController { public string Export() => \"reports\"; }\n",
    ));
  scopeMutation("csv-dormancy-scope", "unrelated helper", (root) =>
    write(
      root,
      "ReportFileHelper.cs",
      "namespace Utility; public static class ReportFileHelper { public static string Extension => \".csv\"; }\n",
    ));
}

function restraintMutation(contract, name, mutate) {
  try {
    const root = cloneForMutation(contract, name);
    mutate(root);
    const result = restraintValidatorResult(contract, root);
    if (result.status !== 0) reportPass(contract, `${name} rejected`);
    else reportUnexpected(contract, name, result, "failure");
  } catch (error) {
    failures.push(`${contract}: ${name} mutation failed: ${error.message}`);
    console.error(`FAIL ${contract}: ${name} mutation setup`);
  }
}

function restraintPositiveMutation(contract, name, mutate) {
  try {
    const root = cloneForMutation(contract, name);
    mutate(root);
    const result = restraintValidatorResult(contract, root);
    if (result.status === 0) reportPass(contract, name);
    else reportUnexpected(contract, name, result, "pass");
  } catch (error) {
    failures.push(`${contract}: ${name} mutation failed: ${error.message}`);
    console.error(`FAIL ${contract}: ${name} mutation setup`);
  }
}

function restraintMutations() {
  restraintMutation("razor-static", "unrelated Privacy page edit", (root) =>
    replace(root, "Pages/Privacy.cshtml", "<h1>", '<h1 class="display-4">', 1));
  restraintMutation("razor-static", "unrelated model edit", (root) =>
    replace(root, "Models/Product.cs", "public class Product", "public sealed class Product", 1));
  restraintMutation("razor-static", "unrelated configuration edit", (root) =>
    replace(root, "appsettings.json", "{", '{\n  "StaticPagesEnabled": true,', 1));
  restraintMutation("razor-static", "deleted baseline file", (root) =>
    fs.rmSync(path.join(root, "Pages", "Index.cshtml")));
  restraintMutation("razor-static", "unrelated helper addition", (root) =>
    write(root, "Helpers/StaticPageHelper.cs", "namespace MyApp.Helpers; public static class StaticPageHelper { }\n"));
  restraintPositiveMutation("razor-static", "allowed navigation-only edit", (root) =>
    replace(
      root,
      "Pages/Shared/_Layout.cshtml",
      '                        <li class="nav-item">\n                            <a class="nav-link text-dark" asp-area="" asp-page="/Privacy">Privacy</a>\n                        </li>',
      '                        <li class="nav-item">\n                            <a class="nav-link text-dark" asp-area="" asp-page="/Privacy">Privacy</a>\n                        </li>\n                        <li class="nav-item">\n                            <a class="nav-link text-dark" asp-area="" asp-page="/Terms">Terms</a>\n                        </li>',
      1,
    ));
  restraintMutation("razor-static", "full layout replacement", (root) =>
    write(
      root,
      "Pages/Shared/_Layout.cshtml",
      '<nav><a class="nav-link" asp-page="/Terms">Terms</a></nav>\n<main>@RenderBody()</main>\n',
    ));
  restraintMutation("razor-static", "existing Home navigation removed", (root) =>
    replace(
      root,
      "Pages/Shared/_Layout.cshtml",
      '                        <li class="nav-item">\n                            <a class="nav-link text-dark" asp-area="" asp-page="/Index">Home</a>\n                        </li>\n',
      "",
      1,
    ));
  restraintMutation("razor-static", "existing Privacy navigation removed", (root) =>
    replace(
      root,
      "Pages/Shared/_Layout.cshtml",
      '                        <li class="nav-item">\n                            <a class="nav-link text-dark" asp-area="" asp-page="/Privacy">Privacy</a>\n                        </li>\n',
      "",
      1,
    ));
  restraintMutation("razor-static", "unrelated navigation insertion", (root) =>
    replace(
      root,
      "Pages/Shared/_Layout.cshtml",
      '                        <li class="nav-item">\n                            <a class="nav-link text-dark" asp-area="" asp-page="/Privacy">Privacy</a>\n                        </li>',
      '                        <li class="nav-item">\n                            <a class="nav-link text-dark" asp-area="" asp-page="/Privacy">Privacy</a>\n                        </li>\n                        <li class="nav-item">\n                            <a class="nav-link text-dark" href="/About">About</a>\n                        </li>',
      1,
    ));
  restraintMutation("razor-static", "script style and form navigation insertion", (root) =>
    replace(
      root,
      "Pages/Shared/_Layout.cshtml",
      "                    </ul>",
      '                        <li><a asp-page="/Terms">Terms</a></li>\n                        <script>void 0</script><style>.nav{display:none}</style><form></form>\n                    </ul>',
      1,
    ));
  restraintMutation("razor-static", "non-EF AppDbContext over-scaffolding", (root) =>
    write(root, "Data/AppDbContext.cs", "namespace MyApp.Data; public sealed class AppDbContext { }\n"));
  restraintMutation("razor-static", "compilable Product and Category CRUD pages", (root) => {
    write(root, "Pages/Products/Index.cshtml", '@page\n<h1>Products</h1>\n');
    write(root, "Pages/Categories/Index.cshtml", '@page\n<h1>Categories</h1>\n');
  });
  restraintMutation("razor-static", "singular Product and Category CRUD directories", (root) => {
    write(root, "Pages/Product/Index.cshtml", '@page\n<h1>Product</h1>\n');
    write(root, "Pages/Category/Index.cshtml", '@page\n<h1>Category</h1>\n');
  });
  restraintMutation("razor-static", "nested Product and Category CRUD directories", (root) => {
    write(root, "Pages/Admin/Products/Index.cshtml", '@page\n<h1>Products</h1>\n');
    write(root, "Pages/Management/Categories/Edit.cshtml", '@page\n<h1>Edit category</h1>\n');
  });
  restraintMutation("razor-static", "flat entity-prefixed Razor pages", (root) => {
    write(root, "Pages/ProductIndex.cshtml", '@page\n<h1>Product</h1>\n');
    write(root, "Pages/CategoryEdit.cshtml.cs",
      "using Microsoft.AspNetCore.Mvc.RazorPages; public class CategoryEditModel : PageModel { }\n");
  });
  restraintMutation("razor-static", "nested entity-prefixed Razor pages", (root) => {
    write(root, "Pages/Admin/ProductDetails.cshtml", '@page\n<h1>Product</h1>\n');
    write(root, "Pages/Admin/CategoryEdit.cshtml", '@page\n<h1>Category</h1>\n');
  });
  restraintMutation("razor-static", "plural flat entity-prefixed Razor pages", (root) => {
    write(root, "Pages/ProductsIndex.cshtml", '@page\n<h1>Products</h1>\n');
    write(root, "Pages/CategoriesEdit.cshtml.cs",
      "using Microsoft.AspNetCore.Mvc.RazorPages; public class CategoriesEditModel : PageModel { }\n");
  });
  restraintMutation("razor-static", "Terms page missing route", (root) =>
    replace(root, "Pages/Terms.cshtml", "@page", "@* @page *@", 1));
  restraintMutation("razor-static", "empty Terms page", (root) =>
    write(root, "Pages/Terms.cshtml", ""));
  restraintMutation("razor-static", "Terms page references wrong model", (root) =>
    replace(root, "Pages/Terms.cshtml", "MyApp.Pages.TermsModel", "MyApp.Pages.MissingTermsModel", 1));
  restraintMutation("razor-static", "Terms backing file is not a PageModel", (root) =>
    replace(root, "Pages/Terms.cshtml.cs", ": PageModel", "", 1));
  restraintMutation("blazor-static", "unrelated Home component edit", (root) =>
    replace(root, "Components/Pages/Home.razor", "<h1>", '<h1 class="display-4">', 1));
  restraintMutation("blazor-static", "unrelated model edit", (root) =>
    replace(root, "Models/Employee.cs", "public class Employee", "public sealed class Employee", 1));
  restraintMutation("blazor-static", "unrelated configuration edit", (root) =>
    replace(root, "appsettings.json", "{", '{\n  "ReportsEnabled": true,', 1));
  restraintMutation("blazor-static", "unrelated CSS edit", (root) =>
    replace(root, "wwwroot/app.css", "html, body", "html, body, #app", 1));
  restraintMutation("blazor-static", "deleted baseline file", (root) =>
    fs.rmSync(path.join(root, "Components", "Pages", "Counter.razor")));
  restraintMutation("blazor-static", "unrelated helper addition", (root) =>
    write(root, "Helpers/StaticPageHelper.cs", "namespace BlazorApp.Helpers; public static class StaticPageHelper { }\n"));
  restraintPositiveMutation("blazor-static", "allowed navigation-only edit", (root) =>
    replace(
      root,
      "Components/Layout/NavMenu.razor",
      '<NavLink class="nav-link" href="reports">',
      '<NavLink class="nav-link" href="reports" title="Reports">',
      1,
    ));
  restraintMutation("blazor-static", "full menu replacement", (root) =>
    write(
      root,
      "Components/Layout/NavMenu.razor",
      '<nav><NavLink class="nav-link" href="reports">Reports</NavLink></nav>\n',
    ));
  restraintMutation("blazor-static", "existing Home navigation removed", (root) =>
    replace(
      root,
      "Components/Layout/NavMenu.razor",
      '        <div class="nav-item px-3">\n            <NavLink class="nav-link" href="" Match="NavLinkMatch.All">\n                <span class="bi bi-house-door-fill-nav-menu" aria-hidden="true"></span> Home\n            </NavLink>\n        </div>\n\n',
      "",
      1,
    ));
  restraintMutation("blazor-static", "existing Counter navigation removed", (root) =>
    replace(
      root,
      "Components/Layout/NavMenu.razor",
      '        <div class="nav-item px-3">\n            <NavLink class="nav-link" href="counter">\n                <span class="bi bi-plus-square-fill-nav-menu" aria-hidden="true"></span> Counter\n            </NavLink>\n        </div>\n\n',
      "",
      1,
    ));
  restraintMutation("blazor-static", "existing Weather navigation removed", (root) =>
    replace(
      root,
      "Components/Layout/NavMenu.razor",
      '        <div class="nav-item px-3">\n            <NavLink class="nav-link" href="weather">\n                <span class="bi bi-list-nested-nav-menu" aria-hidden="true"></span> Weather\n            </NavLink>\n        </div>',
      "",
      1,
    ));
  restraintMutation("blazor-static", "unrelated navigation insertion", (root) =>
    replace(
      root,
      "Components/Layout/NavMenu.razor",
      "    </nav>",
      '        <div class="nav-item px-3">\n            <NavLink class="nav-link" href="about">About</NavLink>\n        </div>\n    </nav>',
      1,
    ));
  restraintMutation("blazor-static", "non-EF AppDbContext over-scaffolding", (root) =>
    write(root, "Data/AppDbContext.cs", "namespace BlazorApp.Data; public sealed class AppDbContext { }\n"));
  restraintMutation("blazor-static", "compilable Employee and Department CRUD components", (root) => {
    write(root, "Components/Pages/Employees.razor", '@page "/employees"\n<h1>Employees</h1>\n');
    write(root, "Components/Pages/Departments.razor", '@page "/departments"\n<h1>Departments</h1>\n');
  });
  restraintMutation("blazor-static", "singular Employee and Department CRUD directories", (root) => {
    write(root, "Components/Pages/Employee/Index.razor", '@page "/employees"\n<h1>Employees</h1>\n');
    write(root, "Components/Pages/Department/Index.razor", '@page "/departments"\n<h1>Departments</h1>\n');
  });
  restraintMutation("blazor-static", "nested Employee and Department CRUD directories", (root) => {
    write(root, "Components/Pages/Admin/Employees/Index.razor", '@page "/employees"\n<h1>Employees</h1>\n');
    write(root, "Components/Pages/Management/Departments/Edit.razor", '@page "/departments/edit"\n<h1>Edit department</h1>\n');
  });
  restraintMutation("blazor-static", "flat entity-prefixed Blazor components", (root) => {
    write(root, "Components/Pages/EmployeeIndex.razor", '@page "/employees"\n<h1>Employees</h1>\n');
    write(root, "Components/Pages/DepartmentsEdit.razor", '@page "/departments/edit"\n<h1>Edit department</h1>\n');
  });
  restraintMutation("blazor-static", "nested entity-prefixed Blazor components", (root) => {
    write(root, "Components/Pages/Admin/EmployeesDetails.razor", '@page "/employees/details"\n<h1>Employee details</h1>\n');
    write(root, "Components/Pages/Admin/DepartmentDelete.razor", '@page "/departments/delete"\n<h1>Delete department</h1>\n');
  });
  restraintMutation("blazor-static", "Reports adds an unnecessary render mode", (root) =>
    write(root, "Components/Pages/Reports.razor", '@page "/reports"\n@rendermode InteractiveServer\n<h1>Reports</h1>\n'));
  restraintMutation("blazor-static", "Reports page missing route", (root) =>
    replace(root, "Components/Pages/Reports.razor", '@page "/reports"', '@* @page "/reports" *@', 1));
  restraintMutation("blazor-static", "empty Reports page", (root) =>
    write(root, "Components/Pages/Reports.razor", ""));
  restraintMutation("blazor-static", "Reports page missing PageTitle", (root) =>
    replace(root, "Components/Pages/Reports.razor", "<PageTitle>Reports</PageTitle>", "", 1));
  restraintMutation("blazor-static", "Reports page missing heading", (root) =>
    replace(root, "Components/Pages/Reports.razor", "<h1>Reports</h1>", "", 1));
  restraintMutation("blazor-static", "Reports page missing content", (root) =>
    replace(root, "Components/Pages/Reports.razor", "<p>Select a report to continue.</p>", "", 1));
  restraintMutation("blazor-static", "Reports navigation removed", (root) =>
    replace(
      root,
      "Components/Layout/NavMenu.razor",
      /\s*<div class="nav-item px-3">\s*<NavLink class="nav-link" href="reports">[\s\S]*?<\/NavLink>\s*<\/div>/,
      "",
      1,
    ));
  restraintMutation("blazor-static", "interactive registrations moved into comments", (root) => {
    replace(
      root,
      "Program.cs",
      "builder.Services.AddRazorComponents()\n    .AddInteractiveServerComponents();",
      "builder.Services.AddRazorComponents();\n// .AddInteractiveServerComponents();",
    );
    replace(
      root,
      "Program.cs",
      "app.MapRazorComponents<App>()\n    .AddInteractiveServerRenderMode();",
      "app.MapRazorComponents<App>();\n// .AddInteractiveServerRenderMode();",
    );
  });

}

function cloneForMutation(contract, name, source = goldenRoots.get(contract)) {
  const destination = path.join(runRoot, "mutations", `${contract}-${name.replaceAll(/[^a-z0-9-]+/gi, "-")}`);
  fs.mkdirSync(path.dirname(destination), { recursive: true });
  copySource(source, destination);
  return destination;
}

function read(relativeRoot, relativeFile) {
  return fs.readFileSync(path.join(relativeRoot, ...relativeFile.split("/")), "utf8");
}

function write(relativeRoot, relativeFile, text) {
  const target = path.join(relativeRoot, ...relativeFile.split("/"));
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.writeFileSync(target, text);
}

function replace(relativeRoot, relativeFile, search, replacement, count = Infinity) {
  const original = read(relativeRoot, relativeFile);
  let changed = 0;
  let updated;
  if (search instanceof RegExp) {
    updated = original.replace(search, (...args) => {
      if (changed >= count) return args[0];
      changed += 1;
      return typeof replacement === "function" ? replacement(...args) : replacement;
    });
  } else {
    updated = original;
    while (changed < count) {
      const index = updated.indexOf(search);
      if (index < 0) break;
      updated = updated.slice(0, index) + replacement + updated.slice(index + search.length);
      changed += 1;
    }
  }
  if (changed === 0) throw new Error(`Mutation did not match ${relativeFile}: ${search}`);
  write(relativeRoot, relativeFile, updated);
}

function insertBeforeAppRun(root, source) {
  replace(root, "Program.cs", "app.Run();", `${source}\n\napp.Run();`, 1);
}

function removeMigrationCreateTable(root, relativeFile, tableName) {
  const source = read(root, relativeFile);
  for (const match of source.matchAll(/migrationBuilder\.CreateTable\s*\(/g)) {
    const openParen = source.indexOf("(", match.index);
    const closeParen = balancedEnd(source, openParen, "(", ")");
    if (closeParen < 0) continue;
    const call = source.slice(openParen + 1, closeParen);
    if (!new RegExp(`\\bname\\s*:\\s*"${tableName}"`).test(call)) continue;
    const lineStart = source.lastIndexOf("\n", match.index) + 1;
    const semicolon = source.indexOf(";", closeParen);
    if (semicolon < 0) break;
    write(root, relativeFile, source.slice(0, lineStart) + source.slice(semicolon + 1));
    return;
  }
  throw new Error(`Could not find CreateTable for ${tableName} in ${relativeFile}`);
}

function replaceEndpointMetadata(
  root,
  relativeFile,
  receiver,
  verb,
  route,
  search,
  replacement,
) {
  const source = read(root, relativeFile);
  const escapedRoute = route.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const endpoint = source.match(
    new RegExp(`\\b${receiver}\\.Map${verb}\\s*\\(\\s*"${escapedRoute}"`),
  );
  if (!endpoint) throw new Error(`Could not find ${receiver}.Map${verb}("${route}")`);
  const start = endpoint.index;
  const terminator = source.slice(start).match(/\)\s*;/);
  const end = terminator ? start + terminator.index + terminator[0].lastIndexOf(";") : -1;
  if (end < 0) throw new Error(`Could not find metadata terminator for ${receiver}.Map${verb}`);
  const statement = source.slice(start, end + 1);
  const updated = statement.replace(search, replacement);
  if (updated === statement) {
    throw new Error(`Metadata mutation did not match ${receiver}.Map${verb}("${route}")`);
  }
  write(root, relativeFile, source.slice(0, start) + updated + source.slice(end + 1));
}

function balancedEnd(text, start, open, close) {
  let depth = 0;
  let quote = "";
  let escaped = false;
  for (let index = start; index < text.length; index += 1) {
    const character = text[index];
    if (quote) {
      if (escaped) escaped = false;
      else if (character === "\\") escaped = true;
      else if (character === quote) quote = "";
      continue;
    }
    if (character === '"' || character === "'") {
      quote = character;
      continue;
    }
    if (character === open) depth += 1;
    if (character === close && --depth === 0) return index;
  }
  return -1;
}

function methodRange(text, name) {
  const start = text.indexOf(`private static async Task<IResult> ${name}(`);
  if (start < 0) throw new Error(`Could not find method ${name}`);
  const openParen = text.indexOf("(", start);
  const closeParen = balancedEnd(text, openParen, "(", ")");
  const openBrace = text.indexOf("{", closeParen);
  const closeBrace = balancedEnd(text, openBrace, "{", "}");
  if (closeParen < 0 || openBrace < 0 || closeBrace < 0) {
    throw new Error(`Could not parse method ${name}`);
  }
  return { start, openParen, closeParen, openBrace, closeBrace };
}

function renameProductRequestParameter(root, methodName, parameterName) {
  const relativeFile = "Endpoints/ProductEndpoints.cs";
  const source = read(root, relativeFile);
  const range = methodRange(source, methodName);
  const method = source.slice(range.start, range.closeBrace + 1)
    .replace(/\bProductRequest\s+request\b/, `ProductRequest ${parameterName}`)
    .replace(/\brequest\b/g, parameterName);
  write(
    root,
    relativeFile,
    source.slice(0, range.start) + method + source.slice(range.closeBrace + 1),
  );
}

function inlineProductHandler(root, methodName, mapCall, parameterName) {
  const relativeFile = "Endpoints/ProductEndpoints.cs";
  let source = read(root, relativeFile);
  const range = methodRange(source, methodName);
  const parameters = source.slice(range.openParen + 1, range.closeParen)
    .replace(/\bProductRequest\s+request\b/, `ProductRequest ${parameterName}`);
  const body = source.slice(range.openBrace, range.closeBrace + 1)
    .replace(/\brequest\b/g, parameterName);
  source = source.replace(mapCall, `${mapCall.slice(0, mapCall.lastIndexOf(",") + 1)} async (${parameters}) => ${body})`);
  write(root, relativeFile, source);
}

function mutation(contract, name, mutate) {
  mutationFrom(contract, goldenRoots.get(contract), name, mutate);
}

function mutationFrom(contract, source, name, mutate) {
  try {
    const root = cloneForMutation(contract, name, source);
    mutate(root);
    expectFailure(contract, root, name);
  } catch (error) {
    failures.push(`${contract}: ${name} mutation failed: ${error.message}`);
    console.error(`FAIL ${contract}: ${name} mutation setup`);
  }
}

function testInactiveEnsureCreatedLayouts() {
  const source = `internal static class EnsureCreatedExamples
{
    private const string Regular = "Database.EnsureCreated();";
    private const string Verbatim = @"Database.EnsureCreatedAsync();";
    private const string Raw = """Database.EnsureCreated();""";

    // Database.EnsureCreated();
    /* Database.EnsureCreatedAsync(); */
}
`;
  for (const { contract } of cases) {
    const root = cloneForMutation(contract, "inactive-ensure-created-layout");
    write(root, "EnsureCreatedExamples.cs", source);
    expectPass(contract, root, "EnsureCreated text in comments and strings");
  }
}

function ensureCreatedMutations() {
  const syncSource = `using Microsoft.EntityFrameworkCore;

internal static class GeneratedDatabaseStartup
{
    public static void Initialize(DbContext context)
    {
        context.Database.EnsureCreated();
    }
}
`;
  for (const { contract } of cases) {
    mutation(contract, "active EnsureCreated outside Program.cs", (root) =>
      write(root, "GeneratedDatabaseStartup.cs", syncSource));
  }

  mutation("todo-api", "active EnsureCreatedAsync outside Program.cs", (root) =>
    write(
      root,
      "GeneratedDatabaseStartup.cs",
      `using Microsoft.EntityFrameworkCore;

internal static class GeneratedDatabaseStartup
{
    public static async Task InitializeAsync(DbContext context)
    {
        await context.Database.EnsureCreatedAsync();
    }
}
`,
    ));
}

function testAllowedStartupInitializationLayouts() {
  const source = `// SeedData.Initialize(app.Services);
/* DbInitializer.Seed(app.Services); */
const string seedExample = "SeedData.Initialize(app.Services);";
builder.Services.AddSingleton(TimeProvider.System);`;
  for (const { contract } of cases) {
    const root = cloneForMutation(contract, "allowed-startup-initialization-layout");
    insertBeforeAppRun(root, source);
    expectPass(contract, root, "startup seeding text in comments and strings with ordinary DI");
  }

  const inlineEndpointRoot = cloneForMutation("todo-api", "inline-endpoint-data-write-layout");
  insertBeforeAppRun(
    inlineEndpointRoot,
    `app.MapPost("/api/import-preview", async (
    TodoApi.Models.TodoItem item,
    TodoDbContext context) =>
{
    context.TodoItems.Add(item);
    await context.SaveChangesAsync();
    return Results.Ok(item);
});`,
  );
  expectPass("todo-api", inlineEndpointRoot, "inline endpoint data write is not startup seeding");

  const migrationRoot = cloneForMutation("mvc", "migration-insert-data-layout");
  replace(
    migrationRoot,
    "Migrations/20261012220000_InitialCreate.cs",
    "        migrationBuilder.CreateTable(",
    `        migrationBuilder.InsertData(
            table: "Authors",
            columns: new[] { "Id", "Name" },
            values: new object[] { 1, "Existing author" });

        migrationBuilder.CreateTable(`,
    1,
  );
  expectPass("mvc", migrationRoot, "EF migration InsertData");
}

function startupSeedingMutations() {
  mutation("razor-pages", "SeedData Initialize from startup", (root) =>
    insertBeforeAppRun(root, "SeedData.Initialize(app.Services);"));
  mutation("mvc", "DbInitializer Seed from startup", (root) =>
    insertBeforeAppRun(root, "DbInitializer.Seed(app.Services);"));
  mutation("todo-api", "direct startup Add and SaveChanges", (root) =>
    insertBeforeAppRun(
      root,
      `using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
    context.TodoItems.Add(new TodoApi.Models.TodoItem { Title = "Seeded", IsComplete = false });
    context.SaveChanges();
}`,
    ));
}

function migrationCompletenessMutations() {
  for (const [contract, relativeFile, tableName, label] of [
    ["razor-pages", "Migrations/20261012220533_InitialCreate.cs", "Categories", "Razor Category"],
    ["todo-api", "Migrations/20261012220222_InitialCreate.cs", "TodoItems", "Todo"],
    ["mvc", "Migrations/20261012220000_InitialCreate.cs", "Authors", "MVC Author"],
    ["parent-api", "Migrations/20261012220500_InitialCreate.cs", "Categories", "Parent API Category"],
    ["blazor", "Migrations/20261012220222_InitialCreate.cs", "Departments", "Blazor Department"],
  ]) {
    mutation(contract, `${label} migration table removed`, (root) =>
      removeMigrationCreateTable(root, relativeFile, tableName));
  }
}

function testContextRegistrationLayouts() {
  const todoRoot = cloneForMutation("todo-api", "block-context-callback-layout");
  replace(
    todoRoot,
    "Program.cs",
    /builder\.Services\.AddDbContext<TodoDbContext>\(options =>\r?\n    options\.UseSqlite\(builder\.Configuration\.GetConnectionString\("TodoDb"\)\)\);/,
    `builder.Services.AddDbContext<TodoDbContext>(
    options =>
    {
        options.UseSqlite(
            builder.Configuration.GetConnectionString("TodoDb"));
    });`,
    1,
  );
  expectPass("todo-api", todoRoot, "multiline block AddDbContext callback");

  const mvcRoot = cloneForMutation("mvc", "named-context-configuration-layout");
  replace(
    mvcRoot,
    "Program.cs",
    /builder\.Services\.AddDbContext<CatalogDbContext>\(options =>\r?\n    options\.UseSqlite\(builder\.Configuration\.GetConnectionString\("CatalogDb"\)\)\);/,
    "builder.Services.AddDbContext<CatalogDbContext>(ConfigureCatalogDatabase);",
    1,
  );
  replace(
    mvcRoot,
    "Program.cs",
    "app.Run();",
    `app.Run();

static void ConfigureCatalogDatabase(DbContextOptionsBuilder databaseOptions)
{
    databaseOptions.UseSqlite("Data Source=catalog.db");
}`,
    1,
  );
  expectPass("mvc", mvcRoot, "named AddDbContext configuration method");

  const blazorRoot = cloneForMutation("blazor", "factory-block-context-callback-layout");
  replace(
    blazorRoot,
    "Program.cs",
    /builder\.Services\.AddDbContextFactory<AppDbContext>\(options =>\r?\n    options\.UseSqlite\(builder\.Configuration\.GetConnectionString\("AppDbContext"\)\r?\n        \?\? throw new InvalidOperationException\("Connection string 'AppDbContext' was not found\."\)\)\);/,
    `builder.Services.AddDbContextFactory<AppDbContext>(options =>
{
    options.UseSqlite(
        builder.Configuration.GetConnectionString("AppDbContext")
            ?? throw new InvalidOperationException("Connection string 'AppDbContext' was not found."));
});`,
    1,
  );
  expectPass("blazor", blazorRoot, "multiline block AddDbContextFactory callback");
}

function contextRegistrationMutations() {
  mutation("todo-api", "UseSqlite only in unused local function", (root) =>
    replace(
      root,
      "Program.cs",
      /builder\.Services\.AddDbContext<TodoDbContext>\(options =>\r?\n    options\.UseSqlite\(builder\.Configuration\.GetConnectionString\("TodoDb"\)\)\);/,
      `builder.Services.AddDbContext<TodoDbContext>();

static void ConfigureTodoDatabase(DbContextOptionsBuilder options)
{
    options.UseSqlite("Data Source=todo.db");
}`,
      1,
    ));

  mutation("parent-api", "UseSqlite configures a different context registration", (root) => {
    replace(
      root,
      "Program.cs",
      /builder\.Services\.AddDbContext<StoreDbContext>\(options =>\r?\n    options\.UseSqlite\(builder\.Configuration\.GetConnectionString\("StoreDb"\)\)\);/,
      `builder.Services.AddDbContext<StoreDbContext>();
builder.Services.AddDbContext<ReportingDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("StoreDb")));`,
      1,
    );
    write(
      root,
      "Data/ReportingDbContext.cs",
      `using Microsoft.EntityFrameworkCore;

namespace ParentChildApi.Data;

public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options)
    : DbContext(options);
`,
    );
  });
}

function testEndpointHostMappingLayouts() {
  const todoRoot = cloneForMutation("todo-api", "formatted-host-mapping");
  replace(
    todoRoot,
    "Program.cs",
    "app.MapTodoEndpoints();",
    `app
    .MapTodoEndpoints
    ();`,
    1,
  );
  expectPass("todo-api", todoRoot, "formatted MapTodoEndpoints host invocation");

  const parentRoot = cloneForMutation("parent-api", "formatted-host-mapping");
  replace(
    parentRoot,
    "Program.cs",
    "app.MapProductEndpoints();",
    `app
    .MapProductEndpoints
    ();`,
    1,
  );
  expectPass("parent-api", parentRoot, "formatted MapProductEndpoints host invocation");
}

function testPatchTodoLayout() {
  const root = cloneForMutation("todo-api", "patch-layout");
  replace(root, "Endpoints/TodoEndpoints.cs", "group.MapPut", "group.MapPatch");
  replace(root, "TodoApi.http", "\nPUT ", "\nPATCH ");
  expectPass("todo-api", root, "PATCH update layout");
}

function testRouteGroupConstantLayouts() {
  const trailingSlashRoot = cloneForMutation("todo-api", "trailing-slash-route-group-layout");
  replace(
    trailingSlashRoot,
    "Endpoints/TodoEndpoints.cs",
    'MapGroup("/api/todos")',
    'MapGroup("/api/todos/")',
    1,
  );
  expectPass("todo-api", trailingSlashRoot, "normalized trailing-slash route-group layout");

  const todoRoot = cloneForMutation("todo-api", "constant-route-group-layout");
  replace(
    todoRoot,
    "Endpoints/TodoEndpoints.cs",
    '        var group = endpoints.MapGroup("/api/todos").WithTags("Todos");',
    `        const string basePath = "/api/todos";
        var group = endpoints.MapGroup(basePath).WithTags("Todos");`,
    1,
  );
  expectPass("todo-api", todoRoot, "const string route-group layout");

  const parentRoot = cloneForMutation("parent-api", "aliased-route-group-layout");
  replace(
    parentRoot,
    "Endpoints/ProductEndpoints.cs",
    '        var categories = endpoints.MapGroup("/api/categories").WithTags("Categories");',
    `        var categoryPath = "/api/categories";
        var categoryBasePath = categoryPath;
        var categories = endpoints.MapGroup(categoryBasePath).WithTags("Categories");`,
    1,
  );
  replace(
    parentRoot,
    "Endpoints/ProductEndpoints.cs",
    '        var products = endpoints.MapGroup("/api/products").WithTags("Products");',
    `        const string productPath = "/api/products";
        var productBasePath = productPath;
        var products = endpoints.MapGroup(productBasePath).WithTags("Products");`,
    1,
  );
  expectPass("parent-api", parentRoot, "var and aliased route-group layouts");
}

function testEndpointRouteConstantLayouts() {
  const todoRoot = cloneForMutation("todo-api", "constant-endpoint-route-layout");
  replace(
    todoRoot,
    "Endpoints/TodoEndpoints.cs",
    '        var group = endpoints.MapGroup("/api/todos").WithTags("Todos");',
    `        var group = endpoints.MapGroup("/api/todos").WithTags("Todos");
        const string byIdRoute = "/{id:int}";
        var updateByIdRoute = byIdRoute;
        var deleteByIdRoute = updateByIdRoute;`,
    1,
  );
  replace(todoRoot, "Endpoints/TodoEndpoints.cs", 'group.MapGet("/{id:int}",', "group.MapGet(byIdRoute,", 1);
  replace(todoRoot, "Endpoints/TodoEndpoints.cs", 'group.MapPut("/{id:int}",', "group.MapPut(updateByIdRoute,", 1);
  replace(todoRoot, "Endpoints/TodoEndpoints.cs", 'group.MapDelete("/{id:int}",', "group.MapDelete(deleteByIdRoute,", 1);
  expectPass("todo-api", todoRoot, "const and aliased endpoint route templates");

  const parentRoot = cloneForMutation("parent-api", "aliased-endpoint-route-layout");
  replace(
    parentRoot,
    "Endpoints/ProductEndpoints.cs",
    '        var categories = endpoints.MapGroup("/api/categories").WithTags("Categories");',
    `        var categories = endpoints.MapGroup("/api/categories").WithTags("Categories");
        const string categoryByIdRoute = "/{id:int}";
        var categoryUpdateRoute = categoryByIdRoute;
        var categoryDeleteRoute = categoryUpdateRoute;`,
    1,
  );
  replace(
    parentRoot,
    "Endpoints/ProductEndpoints.cs",
    '        var products = endpoints.MapGroup("/api/products").WithTags("Products");',
    `        var products = endpoints.MapGroup("/api/products").WithTags("Products");
        var productByIdRoute = "/{id:int}";
        var productUpdateRoute = productByIdRoute;
        var productDeleteRoute = productUpdateRoute;`,
    1,
  );
  replace(parentRoot, "Endpoints/ProductEndpoints.cs", 'categories.MapGet("/{id:int}",', "categories.MapGet(categoryByIdRoute,", 1);
  replace(parentRoot, "Endpoints/ProductEndpoints.cs", 'categories.MapPut("/{id:int}",', "categories.MapPut(categoryUpdateRoute,", 1);
  replace(parentRoot, "Endpoints/ProductEndpoints.cs", 'categories.MapDelete("/{id:int}",', "categories.MapDelete(categoryDeleteRoute,", 1);
  replace(parentRoot, "Endpoints/ProductEndpoints.cs", 'products.MapGet("/{id:int}",', "products.MapGet(productByIdRoute,", 1);
  replace(parentRoot, "Endpoints/ProductEndpoints.cs", 'products.MapPut("/{id:int}",', "products.MapPut(productUpdateRoute,", 1);
  replace(parentRoot, "Endpoints/ProductEndpoints.cs", 'products.MapDelete("/{id:int}",', "products.MapDelete(productDeleteRoute,", 1);
  expectPass("parent-api", parentRoot, "const, var, and aliased endpoint route templates");
}

function testTodoHttpLayouts() {
  const namedResponseRoot = cloneForMutation("todo-api", "named-response-without-jsonpath-root");
  replace(namedResponseRoot, "TodoApi.http", /\.response\.body\.\$\.id/g, ".response.body.id");
  expectPass("todo-api", namedResponseRoot, "named response body id layout");

  const handlerVariableRoot = cloneForMutation("todo-api", "response-handler-variable");
  replace(
    handlerVariableRoot,
    "TodoApi.http",
    /(\{\s*"title": "Verify the Minimal API",\s*"isComplete": false\s*\})/,
    (match, body) => `${body}

> {%
  client.global.set("todoId", response.body.id);
%}`,
    1,
  );
  replace(
    handlerVariableRoot,
    "TodoApi.http",
    /\{\{createTodo\.response\.body\.\$\.id\}\}/g,
    "{{todoId}}",
  );
  expectPass("todo-api", handlerVariableRoot, "response-handler id variable layout");

  const assignedVariableRoot = cloneForMutation("todo-api", "named-response-assigned-variable");
  replace(
    assignedVariableRoot,
    "TodoApi.http",
    /\{\{createTodo\.response\.body\.\$\.id\}\}/g,
    "{{todoId}}",
  );
  replace(
    assignedVariableRoot,
    "TodoApi.http",
    "@TodoApi_HostAddress = http://localhost:5082",
    `@TodoApi_HostAddress = http://localhost:5082
@todoId = {{createTodo.response.body.$.id}}`,
    1,
  );

  expectPass("todo-api", assignedVariableRoot, "named response assigned id variable layout");
}

function testParentApiHttpLayouts() {
  const namedResponseRoot = cloneForMutation("parent-api", "named-response-without-jsonpath-root");
  replace(namedResponseRoot, "ParentChildApi.http", /\.response\.body\.\$\.id/g, ".response.body.id");
  expectPass("parent-api", namedResponseRoot, "named response body id layout");

  const handlerVariableRoot = cloneForMutation("parent-api", "response-handler-variables");
  replace(
    handlerVariableRoot,
    "ParentChildApi.http",
    /(\{\s*"name": "Hardware"\s*\})/,
    (match, body) => `${body}

> {%
  client.global.set("categoryId", response.body.id);
%}`,
    1,
  );
  replace(
    handlerVariableRoot,
    "ParentChildApi.http",
    /(\{\s*"name": "Mechanical keyboard",\s*"price": 89\.99,\s*"categoryId": \{\{createCategory\.response\.body\.\$\.id\}\}\s*\})/,
    (match, body) => `${body}

> {%
  client.global.set("productId", response.body.$.id);
%}`,
    1,
  );
  replace(
    handlerVariableRoot,
    "ParentChildApi.http",
    /\{\{createCategory\.response\.body\.\$\.id\}\}/g,
    "{{categoryId}}",
  );
  replace(
    handlerVariableRoot,
    "ParentChildApi.http",
    /\{\{createProduct\.response\.body\.\$\.id\}\}/g,
    "{{productId}}",
  );
  expectPass("parent-api", handlerVariableRoot, "response-handler id variable layout");

  const assignedVariableRoot = cloneForMutation("parent-api", "named-response-assigned-variables");
  replace(
    assignedVariableRoot,
    "ParentChildApi.http",
    /\{\{createCategory\.response\.body\.\$\.id\}\}/g,
    "{{categoryId}}",
  );
  replace(
    assignedVariableRoot,
    "ParentChildApi.http",
    /\{\{createProduct\.response\.body\.\$\.id\}\}/g,
    "{{productId}}",
  );
  replace(
    assignedVariableRoot,
    "ParentChildApi.http",
    "@ParentChildApi_HostAddress = http://localhost:5083",
    `@ParentChildApi_HostAddress = http://localhost:5083
@categoryId = {{createCategory.response.body.$.id}}
@productId = {{createProduct.response.body.$.id}}`,
    1,
  );
  expectPass("parent-api", assignedVariableRoot, "named response assigned id variable layout");
}

function testTodoUpdatePatterns() {
  for (const [name, statement] of [
    ["Update", "dbContext.TodoItems.Update(submitted);"],
    ["Attach modified", "dbContext.TodoItems.Attach(submitted).State = EntityState.Modified;"],
  ]) {
    const root = cloneForMutation("todo-api", `${name.toLowerCase().replaceAll(" ", "-")}-submitted-entity-layout`);
    replace(
      root,
      "Endpoints/TodoEndpoints.cs",
      /        item\.Title = request\.Title\.Trim\(\);\s*        item\.IsComplete = request\.IsComplete;/,
      `        var submitted = new TodoItem
        {
            Id = item.Id,
            Title = request.Title.Trim(),
            IsComplete = request.IsComplete
        };
        ${statement}`,
      1,
    );
    expectPass("todo-api", root, `${name} validated submitted entity`);
  }
}

function testModelStateLayouts() {
  const razorEqualityRoot = cloneForMutation("razor-pages", "modelstate-equality-layouts");
  for (const relativeFile of [
    "Pages/Products/Create.cshtml.cs",
    "Pages/Products/Edit.cshtml.cs",
    "Pages/Categories/Create.cshtml.cs",
    "Pages/Categories/Edit.cshtml.cs",
  ]) {
    replace(
      razorEqualityRoot,
      relativeFile,
      "if (!ModelState.IsValid)",
      "if (ModelState.IsValid == false)",
      1,
    );
  }
  expectPass("razor-pages", razorEqualityRoot, "ModelState equality early-return layouts");

  const razorElseRoot = cloneForMutation("razor-pages", "modelstate-else-layout");
  replace(
    razorElseRoot,
    "Pages/Categories/Create.cshtml.cs",
    /        if \(!ModelState\.IsValid\)\s*\{\s*return Page\(\);\s*\}\s*context\.Categories\.Add\(Category\);\s*await context\.SaveChangesAsync\(\);\s*return RedirectToPage\("\.\/Index"\);/,
    `        if (ModelState.IsValid)
        {
            context.Categories.Add(Category);
            await context.SaveChangesAsync();
            return RedirectToPage("./Index");
        }
        else
        {
            return Page();
        }`,
    1,
  );
  expectPass("razor-pages", razorElseRoot, "ModelState valid-branch else layout");

  const mvcEqualityRoot = cloneForMutation("mvc", "modelstate-equality-layouts");
  replace(
    mvcEqualityRoot,
    "Controllers/BooksController.cs",
    /if \(ModelState\.IsValid\)/g,
    "if (ModelState.IsValid == true)",
  );
  replace(
    mvcEqualityRoot,
    "Controllers/AuthorsController.cs",
    /if \(!ModelState\.IsValid\)/g,
    "if (ModelState.IsValid == false)",
  );
  expectPass("mvc", mvcEqualityRoot, "ModelState equality layouts");

  const mvcElseRoot = cloneForMutation("mvc", "modelstate-else-layout");
  replace(
    mvcElseRoot,
    "Controllers/AuthorsController.cs",
    /        if \(!ModelState\.IsValid\)\s*\{\s*return View\(author\);\s*\}\s*context\.Add\(author\);\s*await context\.SaveChangesAsync\(\);\s*return RedirectToAction\(nameof\(Index\)\);/,
    `        if (ModelState.IsValid)
        {
            context.Add(author);
            await context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        else
        {
            return View(author);
        }`,
    1,
  );
  expectPass("mvc", mvcElseRoot, "ModelState valid-branch else layout");
}

function testApiMetadataLayouts() {
  const typedDefaultRoot = cloneForMutation("todo-api", "typed-default-response-layout");
  replaceEndpointMetadata(
    typedDefaultRoot,
    "Endpoints/TodoEndpoints.cs",
    "group",
    "Get",
    "",
    ".Produces<List<TodoItem>>(StatusCodes.Status200OK)",
    ".Produces<List<TodoItem>>()",
  );
  expectPass("todo-api", typedDefaultRoot, "typed Produces<T>() default 200 metadata");

  const validation422Root = cloneForMutation("todo-api", "validation-422-layout");
  replace(
    validation422Root,
    "Endpoints/TodoEndpoints.cs",
    ".ProducesValidationProblem()",
    ".ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)",
  );
  expectPass("todo-api", validation422Root, "ProducesValidationProblem 422 metadata");

  const typedProblemRoot = cloneForMutation("todo-api", "typed-problem-layout");
  replaceEndpointMetadata(
    typedProblemRoot,
    "Endpoints/TodoEndpoints.cs",
    "group",
    "Get",
    "/{id:int}",
    ".Produces(StatusCodes.Status404NotFound)",
    ".Produces<Microsoft.AspNetCore.Mvc.ProblemDetails>(StatusCodes.Status404NotFound)",
  );
  expectPass("todo-api", typedProblemRoot, "typed 404 metadata");

  const attributeRoot = cloneForMutation("todo-api", "response-attribute-layout");
  replaceEndpointMetadata(
    attributeRoot,
    "Endpoints/TodoEndpoints.cs",
    "group",
    "Delete",
    "/{id:int}",
    ".Produces(StatusCodes.Status204NoContent)",
    ".WithMetadata(new Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute(StatusCodes.Status204NoContent))",
  );
  replaceEndpointMetadata(
    attributeRoot,
    "Endpoints/TodoEndpoints.cs",
    "group",
    "Delete",
    "/{id:int}",
    ".Produces(StatusCodes.Status404NotFound)",
    ".WithMetadata(new Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute(StatusCodes.Status404NotFound))",
  );
  expectPass("todo-api", attributeRoot, "ProducesResponseType metadata");

  const bareProblemRoot = cloneForMutation("todo-api", "bare-problem-layout");
  replace(
    bareProblemRoot,
    "Endpoints/TodoEndpoints.cs",
    "        return endpoints;",
    `        group.MapGet("/problem-layout", () => TypedResults.Problem())
            .WithName("GetTodoProblemLayout")
            .WithDescription("Exercises the framework-default problem response status.")
            .ProducesProblem();

        return endpoints;`,
    1,
  );
  expectPass("todo-api", bareProblemRoot, "bare ProducesProblem default 500 metadata");
}

function apiMetadataMutations(contract, relativeFile, receiver, entityLabel) {
  const cases = [
    ["list wrong success status", "Get", "", "Status200OK", "Status201Created"],
    ["by-id missing not-found status", "Get", "/{id:int}", "Status404NotFound", "Status400BadRequest"],
    [
      "by-id wrong not-found status with numeric content type",
      "Get",
      "/{id:int}",
      ".Produces(StatusCodes.Status404NotFound)",
      '.Produces(StatusCodes.Status400BadRequest, "404")',
    ],
    ["create wrong success status", "Post", "", "Status201Created", "Status200OK"],
    [
      "create missing validation status",
      "Post",
      "",
      ".ProducesValidationProblem()",
      ".Produces(StatusCodes.Status409Conflict)",
    ],
    ["update wrong success status", "Put", "/{id:int}", "Status200OK", "Status201Created"],
    [
      "update missing validation status",
      "Put",
      "/{id:int}",
      ".ProducesValidationProblem()",
      ".Produces(StatusCodes.Status409Conflict)",
    ],
    ["update missing not-found status", "Put", "/{id:int}", "Status404NotFound", "Status409Conflict"],
    ["delete wrong success status", "Delete", "/{id:int}", "Status204NoContent", "Status200OK"],
    ["delete missing not-found status", "Delete", "/{id:int}", "Status404NotFound", "Status400BadRequest"],
  ];
  for (const [name, verb, route, search, replacement] of cases) {
    mutation(contract, `${entityLabel} ${name}`, (root) =>
      replaceEndpointMetadata(
        root,
        relativeFile,
        receiver,
        verb,
        route,
        search,
        replacement,
      ));
  }
}

function testTodoErrorConditionLayouts() {
  const anyRoot = cloneForMutation("todo-api", "todo-errors-any-layout");
  replace(anyRoot, "Endpoints/TodoEndpoints.cs", /errors\.Count > 0/g, "errors.Any()");
  expectPass("todo-api", anyRoot, "errors.Any() validation condition");

  const zeroRoot = cloneForMutation("todo-api", "todo-errors-zero-layout");
  replace(
    zeroRoot,
    "Endpoints/TodoEndpoints.cs",
    /if \(errors\.Count > 0\)\s*\{\s*return TypedResults\.ValidationProblem\(errors\);\s*\}/g,
    `if (errors.Count == 0)
        {
        }
        else
        {
            return TypedResults.ValidationProblem(errors);
        }`,
  );
  expectPass("todo-api", zeroRoot, "zero-count validation condition");

  const negatedAnyRoot = cloneForMutation("todo-api", "todo-errors-negated-any-layout");
  replace(
    negatedAnyRoot,
    "Endpoints/TodoEndpoints.cs",
    /if \(errors\.Count > 0\)\s*\{\s*return TypedResults\.ValidationProblem\(errors\);\s*\}/g,
    `if (!errors.Any())
        {
        }
        else
        {
            return TypedResults.ValidationProblem(errors);
        }`,
  );
  expectPass("todo-api", negatedAnyRoot, "negated errors.Any() validation condition");
}

function testRequiredValidationConditionLayouts() {
  const equalityTrueRoot = cloneForMutation("todo-api", "required-equality-true-layout");
  replace(
    equalityTrueRoot,
    "Endpoints/TodoEndpoints.cs",
    "if (string.IsNullOrWhiteSpace(request.Title))",
    "if (string.IsNullOrWhiteSpace(request.Title) == true)",
    1,
  );
  expectPass("todo-api", equalityTrueRoot, "required validation equality-true condition");

  const negatedElseRoot = cloneForMutation("todo-api", "required-negated-else-layout");
  replace(
    negatedElseRoot,
    "Endpoints/TodoEndpoints.cs",
    /        if \(string\.IsNullOrWhiteSpace\(request\.Title\)\)\s*\{\s*errors\[nameof\(request\.Title\)\] = \["Title is required\."\];\s*\}\s*else if \(request\.Title\.Trim\(\)\.Length > 200\)\s*\{\s*errors\[nameof\(request\.Title\)\] = \["Title must be 200 characters or fewer\."\];\s*\}/,
    `        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            if (request.Title.Trim().Length > 200)
            {
                errors[nameof(request.Title)] = ["Title must be 200 characters or fewer."];
            }
        }
        else
        {
            errors[nameof(request.Title)] = ["Title is required."];
        }`,
    1,
  );
  expectPass("todo-api", negatedElseRoot, "required validation negated else condition");

  const equalityFalseElseRoot = cloneForMutation("todo-api", "required-equality-false-else-layout");
  replace(
    equalityFalseElseRoot,
    "Endpoints/TodoEndpoints.cs",
    /        if \(string\.IsNullOrWhiteSpace\(request\.Title\)\)\s*\{\s*errors\[nameof\(request\.Title\)\] = \["Title is required\."\];\s*\}\s*else if \(request\.Title\.Trim\(\)\.Length > 200\)\s*\{\s*errors\[nameof\(request\.Title\)\] = \["Title must be 200 characters or fewer\."\];\s*\}/,
    `        if (string.IsNullOrWhiteSpace(request.Title) == false)
        {
        }
        else
        {
            errors[nameof(request.Title)] = ["Title is required."];
        }`,
    1,
  );
  expectPass("todo-api", equalityFalseElseRoot, "required validation equality-false else condition");
}

function replaceLoadedTargetLookupsWithIncludes(root, layouts) {
  for (const [relativeFile, search, replacement] of layouts) {
    replace(root, relativeFile, search, replacement, 1);
  }
}

function testRazorEditLayouts() {
  const root = cloneForMutation("razor-pages", "loaded-target-edit-layout");
  replace(
    root,
    "Pages/Products/Edit.cshtml.cs",
    "        context.Attach(Product).State = EntityState.Modified;",
    `        var trackedProduct = await context.Products.FindAsync(Product.Id);
        if (trackedProduct is null)
        {
            return NotFound();
        }
        trackedProduct.Name = Product.Name;
        trackedProduct.Price = Product.Price;
        trackedProduct.CategoryId = Product.CategoryId;`,
  );
  replace(
    root,
    "Pages/Categories/Edit.cshtml.cs",
    "        context.Attach(Category).State = EntityState.Modified;",
    `        var trackedCategory = await context.Categories.FindAsync(Category.Id);
        if (trackedCategory is null)
        {
            return NotFound();
        }
        trackedCategory.Name = Category.Name;`,
  );
  expectPass("razor-pages", root, "POST loaded-target edit layout");

  const equivalentNullRoot = cloneForMutation("razor-pages", "equivalent-null-edit-layout", root);
  replace(
    equivalentNullRoot,
    "Pages/Products/Edit.cshtml.cs",
    "if (trackedProduct is null)",
    "if (trackedProduct == null)",
    1,
  );
  replace(
    equivalentNullRoot,
    "Pages/Categories/Edit.cshtml.cs",
    `        if (trackedCategory is null)
        {
            return NotFound();
        }
        trackedCategory.Name = Category.Name;`,
    `        if (trackedCategory is not null)
        {
            trackedCategory.Name = Category.Name;
        }
        else
        {
            return NotFound();
        }`,
    1,
  );
  expectPass("razor-pages", equivalentNullRoot, "equivalent loaded-target null checks");

  const localNotFoundRoot = cloneForMutation("razor-pages", "local-not-found-result-layout", root);
  replace(
    localNotFoundRoot,
    "Pages/Products/Edit.cshtml.cs",
    `        if (trackedProduct is null)
        {
            return NotFound();
        }`,
    `        if (trackedProduct is null)
        {
            var missingProduct = NotFound();
            return missingProduct;
        }`,
    1,
  );
  replace(
    localNotFoundRoot,
    "Pages/Categories/Edit.cshtml.cs",
    `        if (trackedCategory is null)
        {
            return NotFound();
        }`,
    `        if (trackedCategory is null)
        {
            var missingCategory = NotFound();
            return missingCategory;
        }`,
    1,
  );
  expectPass("razor-pages", localNotFoundRoot, "local NotFound result layout");

  const includeRoot = cloneForMutation("razor-pages", "included-loaded-target-edit-layout", root);
  replaceLoadedTargetLookupsWithIncludes(includeRoot, [
    [
      "Pages/Products/Edit.cshtml.cs",
      "context.Products.FindAsync(Product.Id)",
      "context.Products.Include(product => product.Category).FirstOrDefaultAsync(product => product.Id == Product.Id)",
    ],
    [
      "Pages/Categories/Edit.cshtml.cs",
      "context.Categories.FindAsync(Category.Id)",
      "context.Categories.Include(category => category.Products).FirstOrDefaultAsync(category => category.Id == Category.Id)",
    ],
  ]);
  expectPass("razor-pages", includeRoot, "POST loaded-target edit layout with Include");

  mutationFrom("razor-pages", root, "Product loaded target self-assignment", (mutationRoot) => {
    replace(mutationRoot, "Pages/Products/Edit.cshtml.cs", "trackedProduct.Name = Product.Name;", "trackedProduct.Name = trackedProduct.Name;");
    replace(mutationRoot, "Pages/Products/Edit.cshtml.cs", "trackedProduct.Price = Product.Price;", "trackedProduct.Price = trackedProduct.Price;");
    replace(mutationRoot, "Pages/Products/Edit.cshtml.cs", "trackedProduct.CategoryId = Product.CategoryId;", "trackedProduct.CategoryId = trackedProduct.CategoryId;");
  });
  mutationFrom("razor-pages", root, "Category loaded target self-assignment", (mutationRoot) =>
    replace(
      mutationRoot,
      "Pages/Categories/Edit.cshtml.cs",
      "trackedCategory.Name = Category.Name;",
      "trackedCategory.Name = trackedCategory.Name;",
    ));
  mutationFrom("razor-pages", root, "Product loaded target missing check removed", (mutationRoot) =>
    replace(
      mutationRoot,
      "Pages/Products/Edit.cshtml.cs",
      `        if (trackedProduct is null)
        {
            return NotFound();
        }`,
      `        if (Product.Id != 0)
        {
            return NotFound();
        }`,
    ));
  mutationFrom("razor-pages", root, "Category loaded target missing check removed", (mutationRoot) =>
    replace(
      mutationRoot,
      "Pages/Categories/Edit.cshtml.cs",
      `        if (trackedCategory is null)
        {
            return NotFound();
        }`,
      `        if (Category.Id != 0)
        {
            return NotFound();
        }`,
    ));
  mutationFrom("razor-pages", root, "Product compound null check", (mutationRoot) =>
    replace(
      mutationRoot,
      "Pages/Products/Edit.cshtml.cs",
      "if (trackedProduct is null)",
      "if (trackedProduct is null && Product.Id < 0)",
      1,
    ));
  mutationFrom("razor-pages", root, "Category compound opposite-branch null check", (mutationRoot) =>
    replace(
      mutationRoot,
      "Pages/Categories/Edit.cshtml.cs",
      `        if (trackedCategory is null)
        {
            return NotFound();
        }`,
      `        if (trackedCategory is not null || Category.Id < 0)
        {
        }
        else
        {
            return NotFound();
        }`,
      1,
    ));
  mutationFrom("razor-pages", root, "Product missing target returns Page", (mutationRoot) =>
    replace(
      mutationRoot,
      "Pages/Products/Edit.cshtml.cs",
      `        if (trackedProduct is null)
        {
            return NotFound();
        }`,
      `        if (trackedProduct is null)
        {
            _ = NotFound();
            return Page();
        }`,
      1,
    ));
  mutationFrom("razor-pages", root, "Category missing target returns Page", (mutationRoot) =>
    replace(
      mutationRoot,
      "Pages/Categories/Edit.cshtml.cs",
      `        if (trackedCategory is null)
        {
            return NotFound();
        }`,
      `        if (trackedCategory is null)
        {
            _ = NotFound();
            return Page();
        }`,
      1,
    ));
}

function razorMutations() {
  const product = "Pages/Products";
  mutation("razor-pages", "remove list flow", (root) =>
    replace(root, `${product}/Index.cshtml.cs`, ".ToListAsync()", ".ToList()"));
  mutation("razor-pages", "remove details flow", (root) =>
    replace(root, `${product}/Details.cshtml.cs`, "SingleOrDefaultAsync", "SingleOrDefault"));
  mutation("razor-pages", "remove create flow", (root) =>
    replace(root, `${product}/Create.cshtml.cs`, "context.Products.Add(Product)", "_ = Product"));
  mutation("razor-pages", "remove edit flow", (root) =>
    replace(root, `${product}/Edit.cshtml.cs`, "EntityState.Modified", "EntityState.Unchanged"));
  for (const entity of ["Products", "Categories"]) {
    const singular = entity.endsWith("ies")
      ? `${entity.slice(0, -3)}y`
      : entity.slice(0, -1);
    for (const action of ["Create", "Edit"]) {
      mutation("razor-pages", `${singular} ${action} reverses ModelState polarity`, (root) =>
        replace(
          root,
          `Pages/${entity}/${action}.cshtml.cs`,
          "if (!ModelState.IsValid)",
          "if (ModelState.IsValid)",
          1,
        ));
    }
  }
  for (const action of ["Create", "Edit"]) {
    mutation("razor-pages", `Product ${action} removes submitted CategoryId validation`, (root) =>
      replace(
        root,
        `Pages/Products/${action}.cshtml.cs`,
        /\s*if \(!await context\.Categories\.AnyAsync\(category => category\.Id == Product\.CategoryId\)\)\s*\{\s*ModelState\.AddModelError\("Product\.CategoryId", "Select a valid category\."\);\s*\}/,
        "",
        1,
      ));
    mutation("razor-pages", `Product ${action} ignores submitted CategoryId lookup`, (root) =>
      replace(
        root,
        `Pages/Products/${action}.cshtml.cs`,
        /if \(!await context\.Categories\.AnyAsync\(category => category\.Id == Product\.CategoryId\)\)\s*\{\s*ModelState\.AddModelError\("Product\.CategoryId", "Select a valid category\."\);\s*\}/,
        "_ = await context.Categories.AnyAsync(category => category.Id == Product.CategoryId);",
        1,
      ));
  }
  mutation("razor-pages", "POST self-assignment cannot use GET lookup", (root) => {
    replace(
      root,
      `${product}/Edit.cshtml.cs`,
      "context.Attach(Product).State = EntityState.Modified;",
      `Product.Name = Product.Name;
        Product.Price = Product.Price;
        Product.CategoryId = Product.CategoryId;`,
    );
  });
  mutation("razor-pages", "remove delete flow", (root) =>
    replace(root, `${product}/Delete.cshtml.cs`, "context.Products.Remove(product)", "_ = product"));
  mutation("razor-pages", "remove validation", (root) =>
    replace(root, `${product}/Create.cshtml`, /asp-validation-(?:summary|for)/g, "data-no-validation"));
  mutation("razor-pages", "remove parent selection", (root) => {
    replace(root, `${product}/Create.cshtml`, /Product\.CategoryId/g, "Product.CategoryCode");
    replace(root, `${product}/Edit.cshtml`, /Product\.CategoryId/g, "Product.CategoryCode");
  });
  mutation("razor-pages", "remove navigation", (root) =>
    replace(root, "Pages/Shared/_Layout.cshtml", /Products/g, "Inventory"));
  mutation("razor-pages", "empty Razor pages", (root) => {
    for (const entity of ["Products", "Categories"]) {
      for (const action of ["Index", "Details", "Create", "Edit", "Delete"]) {
        write(root, `Pages/${entity}/${action}.cshtml`,
          `@page\n@model MyApp.Pages.${entity}.${action}Model\n<h1>${action}</h1>\n`);
        write(root, `Pages/${entity}/${action}.cshtml.cs`,
          `using Microsoft.AspNetCore.Mvc.RazorPages;\nnamespace MyApp.Pages.${entity};\npublic class ${action}Model : PageModel { }\n`);
      }
    }
  });
}

function todoMutations() {
  const endpoints = "Endpoints/TodoEndpoints.cs";
  mutation("todo-api", "remove MapTodoEndpoints host invocation", (root) =>
    replace(root, "Program.cs", "app.MapTodoEndpoints();", "", 1));
  mutation("todo-api", "comment MapTodoEndpoints host invocation", (root) =>
    replace(root, "Program.cs", "app.MapTodoEndpoints();", "// app.MapTodoEndpoints();", 1));
  mutation("todo-api", "Todo route base mismatch", (root) =>
    replace(root, endpoints, 'MapGroup("/api/todos")', 'MapGroup("/api/todos-broken")', 1));
  mutation("todo-api", "remove list flow", (root) =>
    replace(root, endpoints, ".ToListAsync()", ".ToList()", 1));
  mutation("todo-api", "remove by-id flow", (root) =>
    replace(root, endpoints, "SingleOrDefaultAsync(item => item.Id == id)", "SingleOrDefault(item => item.Id == id)", 1));
  mutation("todo-api", "remove create flow", (root) =>
    replace(root, endpoints, "dbContext.TodoItems.Add(item)", "_ = item"));
  mutation("todo-api", "remove update flow", (root) => {
    replace(root, endpoints, "item.Title = request.Title.Trim();", "_ = request.Title;", 1);
    replace(root, endpoints, "item.IsComplete = request.IsComplete;", "_ = request.IsComplete;", 1);
  });
  mutation("todo-api", "redirect update assignments to request", (root) => {
    replace(root, endpoints, "item.Title = request.Title.Trim();", "request.Title = request.Title.Trim();", 1);
    replace(root, endpoints, "item.IsComplete = request.IsComplete;", "request.IsComplete = request.IsComplete;", 1);
  });
  mutation("todo-api", "self-assign update target", (root) => {
    replace(root, endpoints, "item.Title = request.Title.Trim();", "item.Title = item.Title;", 1);
    replace(root, endpoints, "item.IsComplete = request.IsComplete;", "item.IsComplete = item.IsComplete;", 1);
  });
  mutation("todo-api", "Todo bare Attach does not mark modified", (root) =>
    replace(
      root,
      endpoints,
      /        item\.Title = request\.Title\.Trim\(\);\s*        item\.IsComplete = request\.IsComplete;/,
      `        var submitted = new TodoItem
        {
            Id = item.Id,
            Title = request.Title.Trim(),
            IsComplete = request.IsComplete
        };
        dbContext.TodoItems.Attach(submitted);`,
      1,
    ));
  mutation("todo-api", "remove delete flow", (root) =>
    replace(root, endpoints, "dbContext.TodoItems.Remove(item)", "_ = item"));
  mutation("todo-api", "remove validation", (root) =>
    replace(root, endpoints, "string.IsNullOrWhiteSpace(request.Title)", "false"));
  mutation("todo-api", "remove only create validation", (root) =>
    replace(
      root,
      endpoints,
      /(private static async Task<IResult> CreateTodoAsync[\s\S]*?)var errors = Validate\(request\);/,
      (match, prefix) => `${prefix}var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);`,
    ));
  mutation("todo-api", "remove only update validation", (root) =>
    replace(
      root,
      endpoints,
      /(private static async Task<IResult> UpdateTodoAsync[\s\S]*?)var errors = Validate\(request\);/,
      (match, prefix) => `${prefix}var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);`,
    ));
  mutation("todo-api", "Todo create adds required error for valid input", (root) =>
    replace(
      root,
      endpoints,
      /(private static async Task<IResult> CreateTodoAsync[\s\S]*?)var errors = Validate\(request\);/,
      (match, prefix) => `${prefix}var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            errors[nameof(request.Title)] = ["Title is required."];
        }`,
      1,
    ));
  mutation("todo-api", "Todo update adds required error for valid input", (root) =>
    replace(
      root,
      endpoints,
      /(private static async Task<IResult> UpdateTodoAsync[\s\S]*?)var errors = Validate\(request\);/,
      (match, prefix) => `${prefix}var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            errors[nameof(request.Title)] = ["Title is required."];
        }`,
      1,
    ));
  mutation("todo-api", "Todo create combined required-validation bypass", (root) =>
    replace(
      root,
      endpoints,
      /(private static async Task<IResult> CreateTodoAsync[\s\S]*?)var errors = Validate\(request\);/,
      (match, prefix) => `${prefix}var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            errors[nameof(request.Title)] = ["Title is required."];
        }
        if (string.IsNullOrWhiteSpace(dbContext.Database.ProviderName))
        {
            errors["Unrelated"] = ["Unrelated value is required."];
        }`,
      1,
    ));
  mutation("todo-api", "Todo update combined required-validation bypass", (root) =>
    replace(
      root,
      endpoints,
      /(private static async Task<IResult> UpdateTodoAsync[\s\S]*?)var errors = Validate\(request\);/,
      (match, prefix) => `${prefix}var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            errors[nameof(request.Title)] = ["Title is required."];
        }
        if (string.IsNullOrWhiteSpace(dbContext.Database.ProviderName))
        {
            errors["Unrelated"] = ["Unrelated value is required."];
        }`,
      1,
    ));
  mutation("todo-api", "Todo create reverses error condition", (root) => {
    replace(
      root,
      endpoints,
      /(private static async Task<IResult> CreateTodoAsync[\s\S]*?)if \(errors\.Count > 0\)/,
      (match, prefix) => `${prefix}if (errors.Count == 0)`,
      1,
    );
    addUnrelatedWarningsBranch(root, endpoints, "CreateTodoAsync");
  });
  mutation("todo-api", "Todo update reverses error condition", (root) => {
    replace(
      root,
      endpoints,
      /(private static async Task<IResult> UpdateTodoAsync[\s\S]*?)if \(errors\.Count > 0\)/,
      (match, prefix) => `${prefix}if (errors.Count == 0)`,
      1,
    );
    addUnrelatedWarningsBranch(root, endpoints, "UpdateTodoAsync");
  });
  mutation("todo-api", "bodyless POST request", (root) =>
    replace(
      root,
      "TodoApi.http",
      /POST (\S+\/api\/todos)\s*Content-Type: application\/json\s*\{\s*"title": "Verify the Minimal API",\s*"isComplete": false\s*\}/,
      (match, url) => `POST ${url}`,
      1,
    ));
  mutation("todo-api", "bodyless update request", (root) =>
    replace(
      root,
      "TodoApi.http",
      /PUT (\S+\/api\/todos\/\{\{createTodo\.response\.body\.\$\.id\}\})\s*Content-Type: application\/json\s*\{\s*"title": "Verify the Minimal API",\s*"isComplete": true\s*\}/,
      (match, url) => `PUT ${url}`,
      1,
    ));
  mutation("todo-api", "POST missing required Title", (root) =>
    replace(root, "TodoApi.http", /\s*"title": "Verify the Minimal API",/, "", 1));
  mutation("todo-api", "POST missing required IsComplete", (root) =>
    replace(root, "TodoApi.http", /,\s*"isComplete": false/, "", 1));
  mutation("todo-api", "update missing required Title", (root) =>
    replace(
      root,
      "TodoApi.http",
      /(PUT [^\r\n]+\s*Content-Type: application\/json\s*\{\s*)"title": "Verify the Minimal API",\s*/,
      (match, prefix) => prefix,
      1,
    ));
  mutation("todo-api", "update missing required IsComplete", (root) =>
    replace(root, "TodoApi.http", /,\s*"isComplete": true/, "", 1));
  mutation("todo-api", "POST uses wrong content type", (root) =>
    replace(
      root,
      "TodoApi.http",
      /(POST [^\r\n]+\s*)Content-Type: application\/json/,
      (match, prefix) => `${prefix}Content-Type: text/plain`,
      1,
    ));
  mutation("todo-api", "update uses wrong content type", (root) =>
    replace(
      root,
      "TodoApi.http",
      /(PUT [^\r\n]+\s*)Content-Type: application\/json/,
      (match, prefix) => `${prefix}Content-Type: text/plain`,
      1,
    ));
  mutation("todo-api", "hard-coded dependent request ids", (root) =>
    replace(root, "TodoApi.http", /\{\{createTodo\.response\.body\.\$\.id\}\}/g, "1"));
  mutation("todo-api", "whole response used instead of response id", (root) =>
    replace(
      root,
      "TodoApi.http",
      /\{\{createTodo\.response\.body\.\$\.id\}\}/g,
      "{{createTodo.response.body.$}}",
    ));
  mutation("todo-api", "dynamic route-group base", (root) =>
    replace(
      root,
      endpoints,
      '        var group = endpoints.MapGroup("/api/todos").WithTags("Todos");',
      `        var basePath = GetTodoBasePath();
        var group = endpoints.MapGroup(basePath).WithTags("Todos");`,
      1,
    ));
  mutation("todo-api", "dynamic endpoint route template", (root) =>
    replace(
      root,
      endpoints,
      '        group.MapGet("/{id:int}", GetTodoByIdAsync)',
      `        var byIdRoute = $"/{{{nameof(TodoItem.Id).ToLowerInvariant()}:int}}";
        group.MapGet(byIdRoute, GetTodoByIdAsync)`,
      1,
    ));
}

function testMvcEditLayouts() {
  const root = cloneForMutation("mvc", "loaded-target-edit-layout");
  replace(
    root,
    "Controllers/BooksController.cs",
    "                context.Update(book);",
    `                var trackedBook = await context.Books.FindAsync(id);
                if (trackedBook is null)
                {
                    return NotFound();
                }
                trackedBook.Title = book.Title;
                trackedBook.PublicationYear = book.PublicationYear;
                trackedBook.AuthorId = book.AuthorId;`,
  );
  replace(
    root,
    "Controllers/AuthorsController.cs",
    "            context.Update(author);",
    `            var trackedAuthor = await context.Authors.FindAsync(id);
            if (trackedAuthor is null)
            {
                return NotFound();
            }
            trackedAuthor.Name = author.Name;`,
  );
  expectPass("mvc", root, "POST loaded-target edit layout");

  const equivalentNullRoot = cloneForMutation("mvc", "equivalent-null-edit-layout", root);
  replace(
    equivalentNullRoot,
    "Controllers/BooksController.cs",
    `                if (trackedBook is null)
                {
                    return NotFound();
                }
                trackedBook.Title = book.Title;
                trackedBook.PublicationYear = book.PublicationYear;
                trackedBook.AuthorId = book.AuthorId;`,
    `                if (trackedBook != null)
                {
                    trackedBook.Title = book.Title;
                    trackedBook.PublicationYear = book.PublicationYear;
                    trackedBook.AuthorId = book.AuthorId;
                }
                else
                {
                    return NotFound();
                }`,
    1,
  );
  replace(
    equivalentNullRoot,
    "Controllers/AuthorsController.cs",
    "if (trackedAuthor is null)",
    "if (trackedAuthor == null)",
    1,
  );
  expectPass("mvc", equivalentNullRoot, "equivalent loaded-target null checks");

  const localNotFoundRoot = cloneForMutation("mvc", "local-not-found-result-layout", root);
  replace(
    localNotFoundRoot,
    "Controllers/BooksController.cs",
    `                if (trackedBook is null)
                {
                    return NotFound();
                }`,
    `                if (trackedBook is null)
                {
                    var missingBook = NotFound();
                    return missingBook;
                }`,
    1,
  );
  replace(
    localNotFoundRoot,
    "Controllers/AuthorsController.cs",
    `            if (trackedAuthor is null)
            {
                return NotFound();
            }`,
    `            if (trackedAuthor is null)
            {
                var missingAuthor = NotFound();
                return missingAuthor;
            }`,
    1,
  );
  expectPass("mvc", localNotFoundRoot, "local NotFound result layout");

  const includeRoot = cloneForMutation("mvc", "included-loaded-target-edit-layout", root);
  replaceLoadedTargetLookupsWithIncludes(includeRoot, [
    [
      "Controllers/BooksController.cs",
      "context.Books.FindAsync(id)",
      "context.Books.Include(book => book.Author).FirstOrDefaultAsync(book => book.Id == id)",
    ],
    [
      "Controllers/AuthorsController.cs",
      "context.Authors.FindAsync(id)",
      "context.Authors.Include(author => author.Books).FirstOrDefaultAsync(author => author.Id == id)",
    ],
  ]);
  expectPass("mvc", includeRoot, "POST loaded-target edit layout with Include");

  mutationFrom("mvc", root, "Book loaded target self-assignment", (mutationRoot) => {
    replace(mutationRoot, "Controllers/BooksController.cs", "trackedBook.Title = book.Title;", "trackedBook.Title = trackedBook.Title;");
    replace(mutationRoot, "Controllers/BooksController.cs", "trackedBook.PublicationYear = book.PublicationYear;", "trackedBook.PublicationYear = trackedBook.PublicationYear;");
    replace(mutationRoot, "Controllers/BooksController.cs", "trackedBook.AuthorId = book.AuthorId;", "trackedBook.AuthorId = trackedBook.AuthorId;");
  });
  mutationFrom("mvc", root, "Author loaded target self-assignment", (mutationRoot) =>
    replace(
      mutationRoot,
      "Controllers/AuthorsController.cs",
      "trackedAuthor.Name = author.Name;",
      "trackedAuthor.Name = trackedAuthor.Name;",
    ));
  mutationFrom("mvc", root, "Book loaded target missing check removed", (mutationRoot) =>
    replace(
      mutationRoot,
      "Controllers/BooksController.cs",
      `                if (trackedBook is null)
                {
                    return NotFound();
                }`,
      `                if (id != book.Id)
                {
                    return NotFound();
                }`,
    ));
  mutationFrom("mvc", root, "Author loaded target missing check removed", (mutationRoot) =>
    replace(
      mutationRoot,
      "Controllers/AuthorsController.cs",
      `            if (trackedAuthor is null)
            {
                return NotFound();
            }`,
      `            if (id != author.Id)
            {
                return NotFound();
            }`,
    ));
  mutationFrom("mvc", root, "Book compound null check", (mutationRoot) =>
    replace(
      mutationRoot,
      "Controllers/BooksController.cs",
      "if (trackedBook is null)",
      "if (trackedBook is null && id < 0)",
      1,
    ));
  mutationFrom("mvc", root, "Author compound opposite-branch null check", (mutationRoot) =>
    replace(
      mutationRoot,
      "Controllers/AuthorsController.cs",
      `            if (trackedAuthor is null)
            {
                return NotFound();
            }`,
      `            if (trackedAuthor is not null || id < 0)
            {
            }
            else
            {
                return NotFound();
            }`,
      1,
    ));
  mutationFrom("mvc", root, "Book missing target returns View", (mutationRoot) =>
    replace(
      mutationRoot,
      "Controllers/BooksController.cs",
      `                if (trackedBook is null)
                {
                    return NotFound();
                }`,
      `                if (trackedBook is null)
                {
                    _ = NotFound();
                    return View(model);
                }`,
      1,
    ));
  mutationFrom("mvc", root, "Author missing target returns View", (mutationRoot) =>
    replace(
      mutationRoot,
      "Controllers/AuthorsController.cs",
      `            if (trackedAuthor is null)
            {
                return NotFound();
            }`,
      `            if (trackedAuthor is null)
            {
                _ = NotFound();
                return View(model);
            }`,
      1,
    ));
}

function testMvcNavigationValidationLayouts() {
  const root = cloneForMutation("mvc", "validate-never-navigation-layout");
  replace(
    root,
    "Models/Book.cs",
    "using System.ComponentModel.DataAnnotations;",
    `using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;`,
    1,
  );
  replace(
    root,
    "Models/Book.cs",
    "    public Author? Author { get; set; }",
    `    [ValidateNever]
    public Author Author { get; set; } = null!;`,
    1,
  );
  expectPass("mvc", root, "ValidateNever navigation layout");
}

function mvcMutations() {
  const books = "Controllers/BooksController.cs";
  mutation("mvc", "remove list flow", (root) =>
    replace(root, books, ".ToListAsync()", ".ToList()", 1));
  mutation("mvc", "remove details flow", (root) =>
    replace(root, books, "FirstOrDefaultAsync(item => item.Id == id)", "FirstOrDefault(item => item.Id == id)", 1));
  mutation("mvc", "remove create flow", (root) =>
    replace(root, books, "context.Add(book)", "_ = book"));
  mutation("mvc", "remove edit flow", (root) =>
    replace(root, books, "context.Update(book)", "_ = book"));
  mutation("mvc", "Book bare Attach does not mark modified", (root) =>
    replace(root, books, "context.Update(book)", "context.Attach(book)"));
  mutation("mvc", "Author bare Attach does not mark modified", (root) =>
    replace(
      root,
      "Controllers/AuthorsController.cs",
      "context.Update(author)",
      "context.Attach(author)",
    ));
  for (const [entity, relativeFile] of [
    ["Book", books],
    ["Author", "Controllers/AuthorsController.cs"],
  ]) {
    for (const action of ["Create", "Edit"]) {
      mutation("mvc", `${entity} ${action} reverses ModelState polarity`, (root) =>
        replace(
          root,
          relativeFile,
          new RegExp(
            `(public async Task<IActionResult> ${action}\\b[\\s\\S]*?)if \\((!?)ModelState\\.IsValid\\)`,
          ),
          (match, prefix, negated) =>
            `${prefix}if (${negated ? "" : "!"}ModelState.IsValid)`,
          1,
        ));
    }
  }
  mutation("mvc", "remove delete flow", (root) =>
    replace(root, books, "context.Books.Remove(book)", "_ = book"));
  mutation("mvc", "remove validation", (root) =>
    replace(root, books, /ModelState\.IsValid/g, "true"));
  mutation("mvc", "Book navigation is implicitly required", (root) =>
    replace(root, "Models/Book.cs", "public Author? Author { get; set; }", "public Author Author { get; set; } = null!;", 1));
  mutation("mvc", "commented nullable Book navigation decoy", (root) =>
    replace(
      root,
      "Models/Book.cs",
      "    public Author? Author { get; set; }",
      `    // public Author? Author { get; set; }
    public Author Author { get; set; } = null!;`,
      1,
    ));
  mutation("mvc", "ValidateNever applied to AuthorId instead of Book navigation", (root) => {
    replace(
      root,
      "Models/Book.cs",
      "using System.ComponentModel.DataAnnotations;",
      `using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;`,
      1,
    );
    replace(
      root,
      "Models/Book.cs",
      "    public int AuthorId { get; set; }",
      `    [ValidateNever]
    public int AuthorId { get; set; }`,
      1,
    );
    replace(
      root,
      "Models/Book.cs",
      "    public Author? Author { get; set; }",
      "    public Author Author { get; set; } = null!;",
      1,
    );
  });
  for (const action of ["Create", "Edit"]) {
    mutation("mvc", `Book ${action} removes submitted AuthorId validation`, (root) =>
      replace(
        root,
        books,
        new RegExp(
          `(public async Task<IActionResult> ${action}\\b[\\s\\S]*?)\\s*var authorExists = await context\\.Authors\\.AnyAsync\\(author => author\\.Id == book\\.AuthorId\\);\\s*if \\(!authorExists\\)\\s*\\{\\s*ModelState\\.AddModelError\\(nameof\\(book\\.AuthorId\\), "Select a valid author\\."\\);\\s*\\}`,
        ),
        (match, prefix) => prefix,
        1,
      ));
    mutation("mvc", `Book ${action} ignores submitted AuthorId lookup`, (root) =>
      replace(
        root,
        books,
        new RegExp(
          `(public async Task<IActionResult> ${action}\\b[\\s\\S]*?)var authorExists = await context\\.Authors\\.AnyAsync\\(author => author\\.Id == book\\.AuthorId\\);\\s*if \\(!authorExists\\)\\s*\\{\\s*ModelState\\.AddModelError\\(nameof\\(book\\.AuthorId\\), "Select a valid author\\."\\);\\s*\\}`,
        ),
        (match, prefix) =>
          `${prefix}_ = await context.Authors.AnyAsync(author => author.Id == book.AuthorId);`,
        1,
      ));
  }
  mutation("mvc", "remove parent selection", (root) => {
    replace(root, "Views/Books/Create.cshtml", /AuthorId/g, "WriterId");
    replace(root, "Views/Books/Edit.cshtml", /AuthorId/g, "WriterId");
  });
  mutation("mvc", "remove only Create parent selection", (root) =>
    replace(root, "Views/Books/Create.cshtml", /AuthorId/g, "WriterId"));
  mutation("mvc", "remove only Edit parent selection", (root) =>
    replace(root, "Views/Books/Edit.cshtml", /AuthorId/g, "WriterId"));
  mutation("mvc", "remove navigation", (root) => {
    replace(root, "Views/Shared/_Layout.cshtml", /Books/g, "CatalogItems");
    replace(root, "Views/Shared/_Layout.cshtml", /Authors/g, "Writers");
  });
  mutation("mvc", "empty controllers", (root) => {
    write(root, books,
      "using Microsoft.AspNetCore.Mvc;\nnamespace CatalogMvc.Controllers;\npublic class BooksController : Controller { }\n");
    write(root, "Controllers/AuthorsController.cs",
      "using Microsoft.AspNetCore.Mvc;\nnamespace CatalogMvc.Controllers;\npublic class AuthorsController : Controller { }\n");
  });
}

function testParentApiHandlerLayouts() {
  const namedRoot = cloneForMutation("parent-api", "named-handler-parameter-layout");
  renameProductRequestParameter(namedRoot, "CreateProductAsync", "createPayload");
  renameProductRequestParameter(namedRoot, "UpdateProductAsync", "updateCommand");
  expectPass("parent-api", namedRoot, "named handlers with arbitrary request parameter names");

  const lambdaRoot = cloneForMutation("parent-api", "lambda-parameter-layout");
  inlineProductHandler(
    lambdaRoot,
    "CreateProductAsync",
    'products.MapPost("", CreateProductAsync)',
    "newProduct",
  );
  inlineProductHandler(
    lambdaRoot,
    "UpdateProductAsync",
    'products.MapPut("/{id:int}", UpdateProductAsync)',
    "changedProduct",
  );
  expectPass("parent-api", lambdaRoot, "inline lambdas with arbitrary request parameter names");
}

function testParentApiErrorConditionLayouts() {
  const anyRoot = cloneForMutation("parent-api", "errors-any-layout");
  replace(anyRoot, "Endpoints/ProductEndpoints.cs", /errors\.Count > 0/g, "errors.Any()");
  expectPass("parent-api", anyRoot, "errors.Any() validation condition");

  const zeroRoot = cloneForMutation("parent-api", "errors-zero-layout");
  replace(
    zeroRoot,
    "Endpoints/ProductEndpoints.cs",
    /if \(errors\.Count > 0\)\s*\{\s*return TypedResults\.ValidationProblem\(errors\);\s*\}/g,
    `if (errors.Count == 0)
        {
        }
        else
        {
            return TypedResults.ValidationProblem(errors);
        }`,
  );
  expectPass("parent-api", zeroRoot, "zero-count validation condition");

  const negatedAnyRoot = cloneForMutation("parent-api", "errors-negated-any-layout");
  replace(
    negatedAnyRoot,
    "Endpoints/ProductEndpoints.cs",
    /if \(errors\.Count > 0\)\s*\{\s*return TypedResults\.ValidationProblem\(errors\);\s*\}/g,
    `if (!errors.Any())
        {
        }
        else
        {
            return TypedResults.ValidationProblem(errors);
        }`,
  );
  expectPass("parent-api", negatedAnyRoot, "negated errors.Any() validation condition");
}

function addUnrelatedWarningsBranch(root, relativeFile, methodName) {
  const source = read(root, relativeFile);
  const range = methodRange(source, methodName);
  const method = source.slice(range.start, range.closeBrace + 1);
  const updated = method.replace(
    /(if \(errors\.Count == 0\)\s*\{\s*return TypedResults\.ValidationProblem\(errors\);\s*\})/,
    `$1

        var warnings = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Unrelated"] = ["warning"]
        };
        if (warnings.Any())
        {
            return TypedResults.ValidationProblem(warnings);
        }`,
  );
  if (updated === method) throw new Error(`Could not add unrelated warnings branch to ${methodName}`);
  write(
    root,
    relativeFile,
    source.slice(0, range.start) + updated + source.slice(range.closeBrace + 1),
  );
}

function parentApiMutations() {
  const endpoints = "Endpoints/ProductEndpoints.cs";
  const http = "ParentChildApi.http";
  mutation("parent-api", "remove MapProductEndpoints host invocation", (root) =>
    replace(root, "Program.cs", "app.MapProductEndpoints();", "", 1));
  mutation("parent-api", "comment MapProductEndpoints host invocation", (root) =>
    replace(root, "Program.cs", "app.MapProductEndpoints();", "// app.MapProductEndpoints();", 1));
  mutation("parent-api", "Product route base mismatch", (root) =>
    replace(root, endpoints, 'MapGroup("/api/products")', 'MapGroup("/api/products-broken")', 1));
  mutation("parent-api", "remove list flow", (root) =>
    replace(root, endpoints, ".ToListAsync()", ".ToList()", 1));
  mutation("parent-api", "remove by-id flow", (root) =>
    replace(root, endpoints, ".SingleOrDefaultAsync()", ".SingleOrDefault()", 1));
  mutation("parent-api", "remove create flow", (root) =>
    replace(root, endpoints, "dbContext.Categories.Add(category)", "_ = category"));
  mutation("parent-api", "remove update flow", (root) =>
    replace(root, endpoints, "category.Name = request.Name.Trim();", "_ = request.Name"));
  mutation("parent-api", "redirect Category update assignment to request", (root) =>
    replace(root, endpoints, "category.Name = request.Name.Trim();", "request.Name = request.Name.Trim();"));
  mutation("parent-api", "self-assign Category update target", (root) =>
    replace(root, endpoints, "category.Name = request.Name.Trim();", "category.Name = category.Name;"));
  mutation("parent-api", "redirect Product update assignments to unrelated object", (root) => {
    replace(root, endpoints, "product.Name = request.Name.Trim();", "unrelated.Name = request.Name.Trim();");
    replace(root, endpoints, "product.Price = request.Price;", "unrelated.Price = request.Price;");
    replace(root, endpoints, "product.CategoryId = request.CategoryId;", "unrelated.CategoryId = request.CategoryId;");
  });
  mutation("parent-api", "self-assign Product update target", (root) => {
    replace(root, endpoints, "product.Name = request.Name.Trim();", "product.Name = product.Name;");
    replace(root, endpoints, "product.Price = request.Price;", "product.Price = product.Price;");
    replace(root, endpoints, "product.CategoryId = request.CategoryId;", "product.CategoryId = product.CategoryId;");
  });
  mutation("parent-api", "Category bare Attach does not mark modified", (root) =>
    replace(
      root,
      endpoints,
      "        category.Name = request.Name.Trim();",
      `        var submitted = new Category
        {
            Id = category.Id,
            Name = request.Name.Trim()
        };
        dbContext.Categories.Attach(submitted);`,
      1,
    ));
  mutation("parent-api", "Product bare Attach does not mark modified", (root) =>
    replace(
      root,
      endpoints,
      /        product\.Name = request\.Name\.Trim\(\);\s*        product\.Price = request\.Price;\s*        product\.CategoryId = request\.CategoryId;/,
      `        var submitted = new Product
        {
            Id = product.Id,
            Name = request.Name.Trim(),
            Price = request.Price,
            CategoryId = request.CategoryId
        };
        dbContext.Products.Attach(submitted);`,
      1,
    ));
  mutation("parent-api", "remove delete flow", (root) =>
    replace(root, endpoints, "dbContext.Categories.Remove(category)", "_ = category"));
  mutation("parent-api", "remove validation", (root) =>
    replace(root, endpoints, "string.IsNullOrWhiteSpace(request.Name)", "false"));
  mutation("parent-api", "remove parent id validation", (root) =>
    replace(root, endpoints, "dbContext.Categories.FindAsync(request.CategoryId)", "dbContext.Products.FindAsync(request.CategoryId)"));
  mutation("parent-api", "remove only Product create validation", (root) =>
    replace(
      root,
      endpoints,
      /(private static async Task<IResult> CreateProductAsync[\s\S]*?)var errors = Validate\(request\);/,
      (match, prefix) => `${prefix}var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);`,
    ));
  mutation("parent-api", "remove only Product update validation", (root) =>
    replace(
      root,
      endpoints,
      /(private static async Task<IResult> UpdateProductAsync[\s\S]*?)var errors = Validate\(request\);/,
      (match, prefix) => `${prefix}var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);`,
    ));
  mutation("parent-api", "Category create adds required error for valid input", (root) =>
    replace(
      root,
      endpoints,
      /(private static async Task<IResult> CreateCategoryAsync[\s\S]*?)var errors = Validate\(request\);/,
      (match, prefix) => `${prefix}var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            errors[nameof(request.Name)] = ["Name is required."];
        }`,
      1,
    ));
  mutation("parent-api", "Category update adds required error for valid input", (root) =>
    replace(
      root,
      endpoints,
      /(private static async Task<IResult> UpdateCategoryAsync[\s\S]*?)var errors = Validate\(request\);/,
      (match, prefix) => `${prefix}var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            errors[nameof(request.Name)] = ["Name is required."];
        }`,
      1,
    ));
  mutation("parent-api", "Category create combined required-validation bypass", (root) =>
    replace(
      root,
      endpoints,
      /(private static async Task<IResult> CreateCategoryAsync[\s\S]*?)var errors = Validate\(request\);/,
      (match, prefix) => `${prefix}var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            errors[nameof(request.Name)] = ["Name is required."];
        }
        if (string.IsNullOrWhiteSpace(dbContext.Database.ProviderName))
        {
            errors["Unrelated"] = ["Unrelated value is required."];
        }`,
      1,
    ));
  mutation("parent-api", "Category update combined required-validation bypass", (root) =>
    replace(
      root,
      endpoints,
      /(private static async Task<IResult> UpdateCategoryAsync[\s\S]*?)var errors = Validate\(request\);/,
      (match, prefix) => `${prefix}var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            errors[nameof(request.Name)] = ["Name is required."];
        }
        if (string.IsNullOrWhiteSpace(dbContext.Database.ProviderName))
        {
            errors["Unrelated"] = ["Unrelated value is required."];
        }`,
      1,
    ));
  mutation("parent-api", "remove only Product create parent lookup", (root) =>
    replace(
      root,
      endpoints,
      /(private static async Task<IResult> CreateProductAsync[\s\S]*?)dbContext\.Categories\.FindAsync\(request\.CategoryId\)/,
      (match, prefix) => `${prefix}dbContext.Products.FindAsync(request.CategoryId)`,
    ));
  mutation("parent-api", "remove only Product update parent lookup", (root) =>
    replace(
      root,
      endpoints,
      /(private static async Task<IResult> UpdateProductAsync[\s\S]*?)dbContext\.Categories\.FindAsync\(request\.CategoryId\)/,
      (match, prefix) => `${prefix}dbContext.Products.FindAsync(request.CategoryId)`,
    ));
  mutation("parent-api", "Product create parent lookup uses constant", (root) =>
    replace(
      root,
      endpoints,
      /(private static async Task<IResult> CreateProductAsync[\s\S]*?)dbContext\.Categories\.FindAsync\(request\.CategoryId\)/,
      (match, prefix) => `${prefix}dbContext.Categories.FindAsync(1)`,
    ));
  mutation("parent-api", "Product update parent lookup uses unrelated id", (root) =>
    replace(
      root,
      endpoints,
      /(private static async Task<IResult> UpdateProductAsync[\s\S]*?)dbContext\.Categories\.FindAsync\(request\.CategoryId\)/,
      (match, prefix) => `${prefix}dbContext.Categories.FindAsync(id)`,
    ));
  mutation("parent-api", "Product create parent lookup uses unrelated object CategoryId", (root) =>
    replace(
      root,
      endpoints,
      /(private static async Task<IResult> CreateProductAsync[\s\S]*?)dbContext\.Categories\.FindAsync\(request\.CategoryId\)/,
      (match, prefix) => `${prefix}dbContext.Categories.FindAsync(existing.CategoryId)`,
    ));
  mutation("parent-api", "Product update parent lookup uses unrelated nested CategoryId", (root) =>
    replace(
      root,
      endpoints,
      /(private static async Task<IResult> UpdateProductAsync[\s\S]*?)dbContext\.Categories\.FindAsync\(request\.CategoryId\)/,
      (match, prefix) => `${prefix}dbContext.Categories.FindAsync(unrelated.Parent.CategoryId)`,
    ));
  mutation("parent-api", "Product create awaits and ignores correct parent lookup", (root) =>
    replace(
      root,
      endpoints,
      /var category = await dbContext\.Categories\.FindAsync\(request\.CategoryId\);\s*if \(category is null\)\s*\{\s*errors\[nameof\(request\.CategoryId\)\] = \["CategoryId must identify an existing category\."\];\s*\}/,
      `_ = await dbContext.Categories.FindAsync(request.CategoryId);
        var category = new Category { Name = "ignored" };`,
      1,
    ));
  mutation("parent-api", "Product update awaits and ignores correct parent lookup", (root) =>
    replace(
      root,
      endpoints,
      /(private static async Task<IResult> UpdateProductAsync[\s\S]*?)var category = await dbContext\.Categories\.FindAsync\(request\.CategoryId\);\s*if \(category is null\)\s*\{\s*errors\[nameof\(request\.CategoryId\)\] = \["CategoryId must identify an existing category\."\];\s*\}/,
      (match, prefix) => `${prefix}_ = await dbContext.Categories.FindAsync(request.CategoryId);
        var category = new Category { Name = "ignored" };`,
      1,
    ));
  mutation("parent-api", "Product create reverses error condition", (root) =>
    replace(
      root,
      endpoints,
      /(private static async Task<IResult> CreateProductAsync[\s\S]*?)if \(errors\.Count > 0\)/,
      (match, prefix) => `${prefix}if (errors.Count == 0)`,
      1,
    ));
  mutation("parent-api", "Product update reverses error condition", (root) =>
    replace(
      root,
      endpoints,
      /(private static async Task<IResult> UpdateProductAsync[\s\S]*?)if \(errors\.Count > 0\)/,
      (match, prefix) => `${prefix}if (errors.Count == 0)`,
      1,
    ));
  mutation("parent-api", "Category create reverses error condition", (root) => {
    replace(
      root,
      endpoints,
      /(private static async Task<IResult> CreateCategoryAsync[\s\S]*?)if \(errors\.Count > 0\)/,
      (match, prefix) => `${prefix}if (errors.Count == 0)`,
      1,
    );
    addUnrelatedWarningsBranch(root, endpoints, "CreateCategoryAsync");
  });
  mutation("parent-api", "Category update reverses error condition", (root) => {
    replace(
      root,
      endpoints,
      /(private static async Task<IResult> UpdateCategoryAsync[\s\S]*?)if \(errors\.Count > 0\)/,
      (match, prefix) => `${prefix}if (errors.Count == 0)`,
      1,
    );
    addUnrelatedWarningsBranch(root, endpoints, "UpdateCategoryAsync");
  });
  mutation("parent-api", "bodyless Category POST request", (root) =>
    replace(root, http, /\n\{\s*"name": "Hardware"\s*\}/, "", 1));
  mutation("parent-api", "bodyless Category update request", (root) =>
    replace(root, http, /\n\{\s*"name": "Computer hardware"\s*\}/, "", 1));
  mutation("parent-api", "bodyless Product POST request", (root) =>
    replace(
      root,
      http,
      /\n\{\s*"name": "Mechanical keyboard",\s*"price": 89\.99,\s*"categoryId": \{\{createCategory\.response\.body\.\$\.id\}\}\s*\}/,
      "",
      1,
    ));
  mutation("parent-api", "bodyless Product update request", (root) =>
    replace(
      root,
      http,
      /\n\{\s*"name": "Mechanical keyboard",\s*"price": 79\.99,\s*"categoryId": \{\{createCategory\.response\.body\.\$\.id\}\}\s*\}/,
      "",
      1,
    ));
  mutation("parent-api", "Category POST missing required name", (root) =>
    replace(root, http, /\s*"name": "Hardware"\s*/, "\n", 1));
  mutation("parent-api", "Category update missing required name", (root) =>
    replace(root, http, /\s*"name": "Computer hardware"\s*/, "\n", 1));
  mutation("parent-api", "Product POST missing required name", (root) =>
    replace(root, http, /\s*"name": "Mechanical keyboard",/, "", 1));
  mutation("parent-api", "Product POST missing required price", (root) =>
    replace(root, http, /\s*"price": 89\.99,/, "", 1));
  mutation("parent-api", "Product POST missing required categoryId", (root) =>
    replace(root, http, /,\s*"categoryId": \{\{createCategory\.response\.body\.\$\.id\}\}/, "", 1));
  mutation("parent-api", "Product update missing required name", (root) =>
    replace(
      root,
      http,
      /(PUT [^\r\n]+\/api\/products\/[^\r\n]+\s*Content-Type: application\/json\s*\{\s*)"name": "Mechanical keyboard",/,
      (match, prefix) => prefix,
      1,
    ));
  mutation("parent-api", "Product update missing required price", (root) =>
    replace(
      root,
      http,
      /(PUT [^\r\n]+\/api\/products\/[^\r\n]+\s*Content-Type: application\/json\s*\{\s*"name": "Mechanical keyboard",)\s*"price": 79\.99,/,
      (match, prefix) => prefix,
      1,
    ));
  mutation("parent-api", "Product update missing required categoryId", (root) =>
    replace(
      root,
      http,
      /(PUT [^\r\n]+\/api\/products\/[^\r\n]+\s*Content-Type: application\/json\s*\{\s*"name": "Mechanical keyboard",\s*"price": 79\.99),\s*"categoryId": \{\{createCategory\.response\.body\.\$\.id\}\}/,
      (match, prefix) => prefix,
      1,
    ));
  mutation("parent-api", "Category POST uses wrong content type", (root) =>
    replace(
      root,
      http,
      /(POST [^\r\n]+\/api\/categories\s*)Content-Type: application\/json/,
      (match, prefix) => `${prefix}Content-Type: text/plain`,
      1,
    ));
  mutation("parent-api", "Category update uses wrong content type", (root) =>
    replace(
      root,
      http,
      /(PUT [^\r\n]+\/api\/categories\/[^\r\n]+\s*)Content-Type: application\/json/,
      (match, prefix) => `${prefix}Content-Type: text/plain`,
      1,
    ));
  mutation("parent-api", "Product POST uses wrong content type", (root) =>
    replace(
      root,
      http,
      /(POST [^\r\n]+\/api\/products\s*)Content-Type: application\/json/,
      (match, prefix) => `${prefix}Content-Type: text/plain`,
      1,
    ));
  mutation("parent-api", "Product update uses wrong content type", (root) =>
    replace(
      root,
      http,
      /(PUT [^\r\n]+\/api\/products\/[^\r\n]+\s*)Content-Type: application\/json/,
      (match, prefix) => `${prefix}Content-Type: text/plain`,
      1,
    ));
  mutation("parent-api", "hard-coded Category dependent request ids", (root) =>
    replace(
      root,
      http,
      /(\/api\/categories\/)\{\{createCategory\.response\.body\.\$\.id\}\}/g,
      (match, prefix) => `${prefix}1`,
    ));
  mutation("parent-api", "hard-coded Product dependent request ids", (root) =>
    replace(
      root,
      http,
      /(\/api\/products\/)\{\{createProduct\.response\.body\.\$\.id\}\}/g,
      (match, prefix) => `${prefix}1`,
    ));
  mutation("parent-api", "hard-coded Product POST CategoryId", (root) =>
    replace(
      root,
      http,
      /"categoryId": \{\{createCategory\.response\.body\.\$\.id\}\}/,
      '"categoryId": 1',
      1,
    ));
  mutation("parent-api", "whole Category response used as dependent request id", (root) =>
    replace(
      root,
      http,
      /(\/api\/categories\/)\{\{createCategory\.response\.body\.\$\.id\}\}/g,
      "$1{{createCategory.response.body.$}}",
    ));
  mutation("parent-api", "whole Product response used as dependent request id", (root) =>
    replace(
      root,
      http,
      /(\/api\/products\/)\{\{createProduct\.response\.body\.\$\.id\}\}/g,
      "$1{{createProduct.response.body.$}}",
    ));
  mutation("parent-api", "whole Category response used for Product POST", (root) =>
    replace(
      root,
      http,
      /"categoryId": \{\{createCategory\.response\.body\.\$\.id\}\}/,
      '"categoryId": {{createCategory.response.body.$}}',
      1,
    ));
}

function blazorMutations() {
  const employees = "Components/Pages/Employees.razor";
  mutation("blazor", "remove list flow", (root) =>
    replace(root, employees, /(\.Employees[\s\S]*?)\.ToListAsync\(\);/, (...args) => `${args[1]}.ToList();`, 1));
  mutation("blazor", "remove details flow", (root) =>
    replace(root, employees, /selectedEmployee\s*=\s*await/g, "selectedEmployee ="));
  mutation("blazor", "remove create flow", (root) =>
    replace(root, employees, "await db.Employees.AddAsync", "_ = db.Employees.Entry"));
  mutation("blazor", "remove edit flow", (root) =>
    replace(root, employees, /\s*employee\.(?:FirstName|LastName|Email|DepartmentId)\s*=\s*model\.(?:FirstName|LastName|Email|DepartmentId);/g, ""));
  mutation("blazor", "shared Employee bare Attach does not update", (root) =>
    replace(
      root,
      employees,
      /        employee\.FirstName = model\.FirstName;\s*        employee\.LastName = model\.LastName;\s*        employee\.Email = model\.Email;\s*        employee\.DepartmentId = model\.DepartmentId;/,
      "        db.Employees.Attach(model);",
      1,
    ));
  mutation("blazor", "shared Department bare Attach does not update", (root) =>
    replace(
      root,
      "Components/Pages/Departments.razor",
      "        department.Name = model.Name;",
      "        db.Departments.Attach(model);",
      1,
    ));
  mutation("blazor", "remove delete flow", (root) =>
    replace(root, employees, "db.Employees.Remove(employee)", "_ = employee"));
  mutation("blazor", "remove validation", (root) =>
    replace(root, employees, "<DataAnnotationsValidator />", ""));
  mutation("blazor", "remove parent selection", (root) =>
    replace(root, employees, /<InputSelect[\s\S]*?<\/InputSelect>/, "<p>No department selection</p>"));
  mutation("blazor", "remove navigation", (root) =>
    replace(root, "Components/Layout/NavMenu.razor", /employees/gi, "staff"));
  mutation("blazor", "empty components", (root) => {
    write(root, employees, '@page "/employees"\n@rendermode InteractiveServer\n<h1>Employees</h1>\n');
    write(root, "Components/Pages/Departments.razor",
      '@page "/departments"\n@rendermode InteractiveServer\n<h1>Departments</h1>\n');
  });
}

function entityComponents(namespaceName, entity, parent = null) {
  const plural = `${entity}s`;
  const lower = plural.toLowerCase();
  const fields = entity === "Employee"
    ? '<InputText @bind-Value="model.FirstName" /><InputText @bind-Value="model.LastName" /><InputText @bind-Value="model.Email" />'
    : '<InputText @bind-Value="model.Name" />';
  const parentMarkup = parent
    ? `<InputSelect @bind-Value="model.${parent}Id">@foreach (var option in options) { <option value="@option.Id">@option.Name</option> }</InputSelect>`
    : "";
  const parentCode = parent
    ? `private List<${parent}> options = [];\nprotected override async Task OnInitializedAsync() { await using var db = await DbFactory.CreateDbContextAsync(); options = await db.${parent}s.AsNoTracking().ToListAsync(); }`
    : "";
  const assignments = entity === "Employee"
    ? "target.FirstName = model.FirstName; target.LastName = model.LastName; target.Email = model.Email; target.DepartmentId = model.DepartmentId;"
    : "target.Name = model.Name;";

  return {
    [`Components/Pages/${entity}/Index.razor`]: `@page "/${lower}"\n@using ${namespaceName}.Data\n@using ${namespaceName}.Models\n@using Microsoft.EntityFrameworkCore\n@inject IDbContextFactory<AppDbContext> DbFactory\n@foreach (var item in items) { <a href="/${lower}/details/@item.Id">@item.Id</a> }\n@code { private List<${entity}> items = []; protected override async Task OnInitializedAsync() { await using var db = await DbFactory.CreateDbContextAsync(); items = await db.${plural}.AsNoTracking().ToListAsync(); } }\n`,
    [`Components/Pages/${entity}/Details.razor`]: `@page "/${lower}/details/{Id:int}"\n@using ${namespaceName}.Data\n@using ${namespaceName}.Models\n@using Microsoft.EntityFrameworkCore\n@inject IDbContextFactory<AppDbContext> DbFactory\n<h1>${entity} details</h1>@if (selected is not null) { <p>@selected.Id</p> }\n@code { [Parameter] public int Id { get; set; } private ${entity}? selected; protected override async Task OnInitializedAsync() { await using var db = await DbFactory.CreateDbContextAsync(); selected = await db.${plural}.AsNoTracking().FirstOrDefaultAsync(x => x.Id == Id); } }\n`,
    [`Components/Pages/${entity}/Create.razor`]: `@page "/${lower}/create"\n@using ${namespaceName}.Data\n@using ${namespaceName}.Models\n@using Microsoft.EntityFrameworkCore\n@inject IDbContextFactory<AppDbContext> DbFactory\n<EditForm Model="@model" OnValidSubmit="CreateAsync"><DataAnnotationsValidator /><ValidationSummary />${fields}${parentMarkup}<button type="submit">Create</button></EditForm>\n@code { private ${entity} model = new(); ${parentCode} private async Task CreateAsync() { await using var db = await DbFactory.CreateDbContextAsync(); await db.${plural}.AddAsync(model); await db.SaveChangesAsync(); model = new(); } }\n`,
    [`Components/Pages/${entity}/Edit.razor`]: `@page "/${lower}/edit/{Id:int}"\n@using ${namespaceName}.Data\n@using ${namespaceName}.Models\n@using Microsoft.EntityFrameworkCore\n@inject IDbContextFactory<AppDbContext> DbFactory\n<EditForm Model="@model" OnValidSubmit="UpdateAsync"><DataAnnotationsValidator /><ValidationMessage For="@(() => model.${entity === "Employee" ? "FirstName" : "Name"})" />${fields}${parentMarkup}<button type="submit">Save</button></EditForm>\n@code { [Parameter] public int Id { get; set; } private ${entity} model = new(); private List<${parent ?? "Department"}> options = []; protected override async Task OnInitializedAsync() { await using var db = await DbFactory.CreateDbContextAsync(); model = await db.${plural}.AsNoTracking().FirstOrDefaultAsync(x => x.Id == Id) ?? new(); ${parent ? `options = await db.${parent}s.AsNoTracking().ToListAsync();` : ""} } private async Task UpdateAsync() { await using var db = await DbFactory.CreateDbContextAsync(); var target = await db.${plural}.FindAsync(Id); if (target is null) return; ${assignments} await db.SaveChangesAsync(); } }\n`,
    [`Components/Pages/${entity}/Delete.razor`]: `@page "/${lower}/delete/{Id:int}"\n@using ${namespaceName}.Data\n@using ${namespaceName}.Models\n@using Microsoft.EntityFrameworkCore\n@inject IDbContextFactory<AppDbContext> DbFactory\n<button @onclick="DeleteAsync">Delete</button>\n@code { [Parameter] public int Id { get; set; } private async Task DeleteAsync() { await using var db = await DbFactory.CreateDbContextAsync(); var target = await db.${plural}.FindAsync(Id); if (target is null) return; db.${plural}.Remove(target); await db.SaveChangesAsync(); } }\n`,
  };
}

function testSeparateBlazorLayout() {
  const root = cloneForMutation("blazor", "separate-component-layout");
  fs.rmSync(path.join(root, "Components", "Pages", "Employees.razor"));
  fs.rmSync(path.join(root, "Components", "Pages", "Departments.razor"));
  const generated = {
    ...entityComponents("BlazorApp", "Employee", "Department"),
    ...entityComponents("BlazorApp", "Department"),
  };
  for (const [relative, text] of Object.entries(generated)) write(root, relative, text);
  expectPass("blazor", root, "synthetic separate-component layout");
  mutationFrom("blazor", root, "separate remove create validation", (mutationRoot) =>
    replace(mutationRoot, "Components/Pages/Employee/Create.razor", "<DataAnnotationsValidator />", ""));
  mutationFrom("blazor", root, "separate remove edit validation", (mutationRoot) =>
    replace(mutationRoot, "Components/Pages/Employee/Edit.razor", "<DataAnnotationsValidator />", ""));
  mutationFrom("blazor", root, "separate remove edit Department selector", (mutationRoot) =>
    replace(
      mutationRoot,
      "Components/Pages/Employee/Edit.razor",
      /<InputSelect[\s\S]*?<\/InputSelect>/,
      "<p>No department selection</p>",
    ));
  mutationFrom("blazor", root, "separate remove update SaveChangesAsync", (mutationRoot) =>
    replace(
      mutationRoot,
      "Components/Pages/Employee/Edit.razor",
      "await db.SaveChangesAsync();",
      "_ = target;",
    ));
  mutationFrom("blazor", root, "separate Employee bare Attach does not update", (mutationRoot) =>
    replace(
      mutationRoot,
      "Components/Pages/Employee/Edit.razor",
      "target.FirstName = model.FirstName; target.LastName = model.LastName; target.Email = model.Email; target.DepartmentId = model.DepartmentId;",
      "db.Employees.Attach(model);",
      1,
    ));
  mutationFrom("blazor", root, "separate Department bare Attach does not update", (mutationRoot) =>
    replace(
      mutationRoot,
      "Components/Pages/Department/Edit.razor",
      "target.Name = model.Name;",
      "db.Departments.Attach(model);",
      1,
    ));
  mutationFrom("blazor", root, "separate remove delete SaveChangesAsync", (mutationRoot) =>
    replace(
      mutationRoot,
      "Components/Pages/Employee/Delete.razor",
      "await db.SaveChangesAsync();",
      "_ = target;",
    ));
}

function combinedEmployeeComponent() {
  return `@page "/employees"
@using BlazorApp.Data
@using BlazorApp.Models
@using Microsoft.EntityFrameworkCore
@inject IDbContextFactory<AppDbContext> DbFactory
@foreach (var employee in employees)
{
    <button @onclick="() => ShowDetailsAsync(employee.Id)">@employee.FirstName</button>
    <button @onclick="() => DeleteEmployeeAsync(employee.Id)">Delete</button>
}
@if (selectedEmployee is not null)
{
    <section aria-label="Employee details">@selectedEmployee.Email</section>
}
<EditForm Model="@createModel" OnValidSubmit="CreateEmployeeAsync">
    <DataAnnotationsValidator />
    <ValidationSummary />
    <InputText @bind-Value="createModel.FirstName" />
    <InputText @bind-Value="createModel.LastName" />
    <InputText @bind-Value="createModel.Email" />
    <InputSelect @bind-Value="createModel.DepartmentId">
        @foreach (var department in createDepartments)
        {
            <option value="@department.Id">@department.Name</option>
        }
    </InputSelect>
</EditForm>
<EditForm Model="@editModel" OnValidSubmit="UpdateEmployeeAsync">
    <DataAnnotationsValidator />
    <ValidationMessage For="@(() => editModel.FirstName)" />
    <InputText @bind-Value="editModel.FirstName" />
    <InputText @bind-Value="editModel.LastName" />
    <InputText @bind-Value="editModel.Email" />
    <InputSelect @bind-Value="editModel.DepartmentId">
        @foreach (var department in editDepartments)
        {
            <option value="@department.Id">@department.Name</option>
        }
    </InputSelect>
</EditForm>
@code {
    private List<Employee> employees = [];
    private List<Department> createDepartments = [];
    private List<Department> editDepartments = [];
    private Employee createModel = new();
    private Employee editModel = new();
    private Employee? selectedEmployee;

    protected override async Task OnInitializedAsync()
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        employees = await db.Employees.AsNoTracking().ToListAsync();
        createDepartments = await db.Departments.AsNoTracking().ToListAsync();
        editDepartments = await db.Departments.AsNoTracking().ToListAsync();
    }

    private async Task ShowDetailsAsync(int id)
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        selectedEmployee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(employee => employee.Id == id);
    }

    private async Task CreateEmployeeAsync()
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        await db.Employees.AddAsync(createModel);
        await db.SaveChangesAsync();
        createModel = new();
    }

    private async Task UpdateEmployeeAsync()
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        var target = await db.Employees.FindAsync(editModel.Id);
        if (target is null) return;
        target.FirstName = editModel.FirstName;
        target.LastName = editModel.LastName;
        target.Email = editModel.Email;
        target.DepartmentId = editModel.DepartmentId;
        await db.SaveChangesAsync();
    }

    private async Task DeleteEmployeeAsync(int id)
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        var target = await db.Employees.FindAsync(id);
        if (target is null) return;
        db.Employees.Remove(target);
        await db.SaveChangesAsync();
    }
}
`;
}

function testCombinedTwoFormBlazorLayout() {
  const root = cloneForMutation("blazor", "combined-two-form-layout");
  write(root, "Components/Pages/Employees.razor", combinedEmployeeComponent());
  expectPass("blazor", root, "synthetic combined two-form layout");
  const attachModifiedRoot = cloneForMutation(
    "blazor",
    "combined-attach-modified-layout",
    root,
  );
  replace(
    attachModifiedRoot,
    "Components/Pages/Employees.razor",
    /        target\.FirstName = editModel\.FirstName;\s*        target\.LastName = editModel\.LastName;\s*        target\.Email = editModel\.Email;\s*        target\.DepartmentId = editModel\.DepartmentId;/,
    "        db.Employees.Attach(editModel).State = EntityState.Modified;",
    1,
  );
  expectPass("blazor", attachModifiedRoot, "combined Attach plus Modified update layout");
  mutationFrom("blazor", root, "combined Employee bare Attach does not update", (mutationRoot) =>
    replace(
      mutationRoot,
      "Components/Pages/Employees.razor",
      /        target\.FirstName = editModel\.FirstName;\s*        target\.LastName = editModel\.LastName;\s*        target\.Email = editModel\.Email;\s*        target\.DepartmentId = editModel\.DepartmentId;/,
      "        db.Employees.Attach(editModel);",
      1,
    ));
  mutationFrom("blazor", root, "combined swapped create and edit handlers", (mutationRoot) => {
    replace(
      mutationRoot,
      "Components/Pages/Employees.razor",
      'OnValidSubmit="CreateEmployeeAsync"',
      'OnValidSubmit="TemporaryEmployeeAsync"',
    );
    replace(
      mutationRoot,
      "Components/Pages/Employees.razor",
      'OnValidSubmit="UpdateEmployeeAsync"',
      'OnValidSubmit="CreateEmployeeAsync"',
    );
    replace(
      mutationRoot,
      "Components/Pages/Employees.razor",
      'OnValidSubmit="TemporaryEmployeeAsync"',
      'OnValidSubmit="UpdateEmployeeAsync"',
    );
  });
  mutationFrom("blazor", root, "combined two forms share one dual-operation handler", (mutationRoot) => {
    replace(
      mutationRoot,
      "Components/Pages/Employees.razor",
      'OnValidSubmit="CreateEmployeeAsync"',
      'OnValidSubmit="SaveEmployeeAsync"',
    );
    replace(
      mutationRoot,
      "Components/Pages/Employees.razor",
      'OnValidSubmit="UpdateEmployeeAsync"',
      'OnValidSubmit="SaveEmployeeAsync"',
    );
    replace(
      mutationRoot,
      "Components/Pages/Employees.razor",
      "    private async Task DeleteEmployeeAsync(int id)",
      `    private async Task SaveEmployeeAsync()
    {
        if (createModel.Id == 0)
        {
            await CreateEmployeeAsync();
        }
        else
        {
            await UpdateEmployeeAsync();
        }
    }

    private async Task DeleteEmployeeAsync(int id)`,
      1,
    );
  });
  mutationFrom("blazor", root, "combined create assignments cannot stand in for edit form", (mutationRoot) => {
    replace(
      mutationRoot,
      "Components/Pages/Employees.razor",
      "        await db.Employees.AddAsync(createModel);",
      `        var employee = new Employee();
        employee.FirstName = createModel.FirstName;
        employee.LastName = createModel.LastName;
        employee.Email = createModel.Email;
        employee.DepartmentId = createModel.DepartmentId;
        await db.Employees.AddAsync(employee);`,
    );
    replace(
      mutationRoot,
      "Components/Pages/Employees.razor",
      /(<EditForm Model="@editModel"[\s\S]*?)\s*<DataAnnotationsValidator \/>/,
      (match, prefix) => prefix,
    );
    replace(
      mutationRoot,
      "Components/Pages/Employees.razor",
      /(<EditForm Model="@editModel"[\s\S]*?)<InputSelect[\s\S]*?<\/InputSelect>/,
      (match, prefix) => `${prefix}<p>No department selection</p>`,
    );
  });
  mutationFrom("blazor", root, "combined create handler uses edit form model", (mutationRoot) =>
    replace(
      mutationRoot,
      "Components/Pages/Employees.razor",
      "await db.Employees.AddAsync(createModel)",
      "await db.Employees.AddAsync(editModel)",
    ));
  mutationFrom("blazor", root, "combined remove edit validation", (mutationRoot) =>
    replace(
      mutationRoot,
      "Components/Pages/Employees.razor",
      /(<EditForm Model="@editModel"[\s\S]*?)\s*<DataAnnotationsValidator \/>/,
      "$1",
    ));
  mutationFrom("blazor", root, "combined remove edit Department selector", (mutationRoot) =>
    replace(
      mutationRoot,
      "Components/Pages/Employees.razor",
      /(<EditForm Model="@editModel"[\s\S]*?)<InputSelect[\s\S]*?<\/InputSelect>/,
      "$1<p>No department selection</p>",
    ));
  mutationFrom("blazor", root, "combined remove edit Department choices load", (mutationRoot) =>
    replace(
      mutationRoot,
      "Components/Pages/Employees.razor",
      "editDepartments = await db.Departments.AsNoTracking().ToListAsync();",
      "editDepartments = [];",
    ));
}

function replaceSharedEmployeeSubmit(root, body) {
  replace(
    root,
    "Components/Pages/Employees.razor",
    /    private async Task SaveEmployeeAsync\(\)\s*\{[\s\S]*?\n    \}\s*\n\s*    private async Task AddEmployeeAsync/,
    `    private async Task SaveEmployeeAsync()
    {
${body}
    }

    private async Task AddEmployeeAsync`,
    1,
  );
}

function sharedBlazorBranchMutations() {
  const directNewRoot = cloneForMutation("blazor", "shared-direct-new-predicate");
  replace(
    directNewRoot,
    "Components/Pages/Employees.razor",
    /        var wasNew = employeeForm\.Id == 0;\s*        if \(wasNew\)/,
    "        if (employeeForm.Id == 0)",
  );
  expectPass("blazor", directNewRoot, "shared direct new-entity predicate");

  const directExistingRoot = cloneForMutation("blazor", "shared-direct-existing-predicate");
  replaceSharedEmployeeSubmit(directExistingRoot, `        if (employeeForm.Id != 0)
        {
            if (!await UpdateEmployeeAsync(employeeForm))
            {
                return;
            }
        }
        else
        {
            await AddEmployeeAsync(employeeForm);
        }`);
  expectPass("blazor", directExistingRoot, "shared direct existing-entity predicate");

  mutation("blazor", "shared Employee reverses new-state predicate", (root) =>
    replace(
      root,
      "Components/Pages/Employees.razor",
      "var wasNew = employeeForm.Id == 0;",
      "var wasNew = employeeForm.Id != 0;",
      1,
    ));
  mutation("blazor", "shared Department reverses new-state predicate", (root) =>
    replace(
      root,
      "Components/Pages/Departments.razor",
      "var wasNew = departmentForm.Id == 0;",
      "var wasNew = departmentForm.Id != 0;",
      1,
    ));
  mutation("blazor", "shared helpers outside form-state branches", (root) =>
    replaceSharedEmployeeSubmit(root, `        if (employeeForm.Id == 0)
        {
            statusMessage = "Creating.";
        }
        else
        {
            statusMessage = "Editing.";
        }

        await AddEmployeeAsync(employeeForm);
        await UpdateEmployeeAsync(employeeForm);`));
  mutation("blazor", "shared helpers both in create branch", (root) =>
    replaceSharedEmployeeSubmit(root, `        var wasNew = employeeForm.Id == 0;
        if (wasNew)
        {
            await AddEmployeeAsync(employeeForm);
            await UpdateEmployeeAsync(employeeForm);
        }
        else
        {
            return;
        }`));
  mutation("blazor", "shared helpers both in edit branch", (root) =>
    replaceSharedEmployeeSubmit(root, `        var wasNew = employeeForm.Id == 0;
        if (wasNew)
        {
            return;
        }
        else
        {
            await AddEmployeeAsync(employeeForm);
            await UpdateEmployeeAsync(employeeForm);
        }`));
}

try {
  for (const item of cases) materializeGolden(item);
  for (const item of restraintCases) materializeRestraintGolden(item);
  for (const item of scopeCases) materializeScopeGolden(item);
  scopeMutations();
  testInactiveEnsureCreatedLayouts();
  ensureCreatedMutations();
  testAllowedStartupInitializationLayouts();
  startupSeedingMutations();
  migrationCompletenessMutations();
  testContextRegistrationLayouts();
  contextRegistrationMutations();
  testEndpointHostMappingLayouts();
  testPatchTodoLayout();
  testRouteGroupConstantLayouts();
  testEndpointRouteConstantLayouts();
  testTodoHttpLayouts();
  testParentApiHttpLayouts();
  testTodoUpdatePatterns();
  testApiMetadataLayouts();
  testTodoErrorConditionLayouts();
  testRequiredValidationConditionLayouts();
  testModelStateLayouts();
  apiMetadataMutations("todo-api", "Endpoints/TodoEndpoints.cs", "group", "Todo");
  testRazorEditLayouts();
  razorMutations();
  todoMutations();
  testMvcEditLayouts();
  testMvcNavigationValidationLayouts();
  mvcMutations();
  testParentApiHandlerLayouts();
  testParentApiErrorConditionLayouts();
  apiMetadataMutations(
    "parent-api",
    "Endpoints/ProductEndpoints.cs",
    "categories",
    "Category",
  );
  apiMetadataMutations(
    "parent-api",
    "Endpoints/ProductEndpoints.cs",
    "products",
    "Product",
  );
  parentApiMutations();
  blazorMutations();
  testSeparateBlazorLayout();
  testCombinedTwoFormBlazorLayout();
  sharedBlazorBranchMutations();
  restraintMutations();
} catch (error) {
  failures.push(error.stack || error.message);
}

console.log(`Validator test artifacts: ${runRoot}`);
if (failures.length > 0) {
  console.error(`\n${failures.length} unexpected result${failures.length === 1 ? "" : "s"}:`);
  for (const failure of failures) console.error(`\n${failure}`);
  process.exit(1);
}

console.log(`PASS all validator tests (${cases.length + restraintCases.length + scopeCases.length} contracts)`);
cleanupArtifacts();
