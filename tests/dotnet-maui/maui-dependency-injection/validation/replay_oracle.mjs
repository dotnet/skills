// Paid judges are intentionally excluded; use the production parser and static oracle graders.
import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, "../../../..");
const { loadEvalSpec, gradeOracle } = await import(pathToFileURL(
  resolve(root, "eng/evaluation-tools/node_modules/@microsoft/vally/dist/index.js"),
));
const spec = await loadEvalSpec(resolve(here, "../eval.yaml"));
const python = process.env.PYTHON ?? "python3";
const mutations = JSON.parse(execFileSync(
  python, [resolve(here, "check_contracts.py"), "--export"], { encoding: "utf8" },
));
const ownershipCases = JSON.parse(execFileSync(
  python, [resolve(here, "check_contracts.py"), "--export-ownership"], { encoding: "utf8" },
));
assert.equal(mutations.length, spec.stimuli.length);
const rootAlternative = "No, neither an empty constructor nor a replacement route is required. "
  + "Typed ShellContent first tries GetService(typeof(DetailsPage)), then "
  + "ActivatorUtilities.CreateInstance, which satisfies constructor parameters "
  + "from DI even though the page itself is not registered.";
const rootResolution = "Neither: you don't need an empty constructor or a pushed route. "
  + "Typed ShellContent obtains the parent context's services, tries "
  + "GetService(typeof(DetailsPage)), and falls back to "
  + "ActivatorUtilities.CreateInstance, which resolves IProductService from DI. "
  + "Registering DetailsPage is optional lifetime control; leave the root tab alone.";

for (const [index, original] of spec.stimuli.entries()) {
  const configs = original.graders.filter(g => g.type !== "prompt");
  assert.ok(configs.every(g => [
    "output-matches", "output-not-matches", "output-contains", "output-not-contains",
  ].includes(g.type)), "Only free static output graders may run in this replay");
  const cases = [
    { label: "golden", message: null, passed: true },
    { label: "mutation", message: mutations[index], passed: false },
  ];
  cases.push(...ownershipCases.filter(([caseIndex]) => caseIndex === index)
    .map(([, label, message, passed]) => ({ label, message, passed })));
  if (original.name === "Keep typed root content constructor injection") {
    cases.push(
      { label: "equivalent-wording", message: rootAlternative, passed: true },
      { label: "resolution-wording", message: rootResolution, passed: true },
      {
        label: "negation-after-constructor",
        message: "In MAUI 10, keep the root ShellContent and constructor injection. "
          + "With its handler/context available, MAUI resolves DetailsPage from DI or uses "
          + "ActivatorUtilities to construct it. A registered IProductService is sufficient; "
          + "an empty constructor is neither needed nor recommended.",
        passed: true,
      },
      {
        label: "unnecessary-constructor",
        message: "Add an empty constructor and replace the root tab with a pushed route.",
        passed: false,
      },
      {
        label: "template-bypass",
        message: "Typed Shell templates never use DI. No empty constructor is needed; replace the root tab.",
        passed: false,
      },
    );
  }
  if (original.name === "Resolve a routed page without registering its concrete type") {
    cases.push({
      label: "route-resolution",
      message: "Yes. Shell's type route can construct an unregistered DetailsPage through DI, "
        + "resolving its registered DetailsViewModel and IProductService. If IProductService "
        + "is missing, page activation fails with a resolution exception. Page registration "
        + "is optional lifetime control.",
      passed: true,
    });
  }
  if (original.name === "Fix manual construction inside a template factory") {
    cases.push(
      {
        label: "template-resolution",
        message: "The DataTemplate(Func<object>) explicitly constructs the page and ViewModel, "
          + "so registrations and the test fake are never consulted. Use "
          + "new DataTemplate(() => services.GetRequiredService<DetailsPage>()). "
          + "Keep the dependency-taking constructors.",
        passed: true,
      },
      {
        label: "nested-manual-graph",
        message: "The correct fix is new DataTemplate(() => new DetailsPage("
          + "new DetailsViewModel(new ProductService()))). This uses the registered test fake.",
        passed: false,
      },
      {
        label: "typed-xaml",
        message: 'Use ContentTemplate="{DataTemplate views:DetailsPage}" on initialized MAUI 10 ShellContent.',
        passed: true,
      },
    );
  }
  for (const testCase of cases) {
    const stimulus = structuredClone(original);
    if (testCase.message)
      stimulus.golden_trajectory.inline.steps.at(-1).message = testCase.message;
    const workspaceDir = resolve(here, `.local/oracle/${index}-${testCase.label}`);
    try {
      const graded = await gradeOracle({
        stimulus,
        graderConfigs: configs,
        baseDir: resolve(here, ".."),
        workspaceDir,
      });
      assert.notEqual(graded.result.status, "error", graded.result.evidence);
      assert.equal(graded.result.passed, testCase.passed,
        `${original.name}: ${testCase.label}: ${graded.result.evidence}`);
      await graded.cleanup();
    } finally {
      await rm(workspaceDir, { recursive: true, force: true });
    }
  }
  console.log(`PASS: production oracle golden acceptance / mutation rejection: ${original.name}`);
}
console.log("PASS: 14 goldens, 19 mutations and 10 alternatives; no agent execution or paid prompt grader");

const reference = await readFile(resolve(root,
  "plugins/dotnet-maui/skills/maui-dependency-injection/references/dependency-injection-api.md"), "utf8");
const httpSection = reference.split("## Shared Cache and HTTP Client Ownership\n")[1]
  .split("## Constructor Injection\n")[0];
const examples = [...httpSection.matchAll(/```csharp\n([\s\S]*?)\n```/g)].map(m => m[1]);
assert.equal(examples.length, 2, "Compile the exact shipping HTTP registration and service");
await mkdir(resolve(here, ".local"), { recursive: true });
const httpWorkspace = await mkdtemp(resolve(here, ".local/http-"));
try {
  await writeFile(resolve(httpWorkspace, "Probe.csproj"),
    await readFile(resolve(here, "MauiDiProbe/MauiDiProbe.csproj")));
  await writeFile(resolve(httpWorkspace, "ProductCatalog.cs"), examples[1]);
  const program = `using Microsoft.Extensions.DependencyInjection;
using System.Net;

var services = new ServiceCollection();
${examples[0].replaceAll("builder.Services", "services")}
var handler = new LocalHandler();
services.AddHttpClient("products").ConfigurePrimaryHttpMessageHandler(() => handler);
using var provider = services.BuildServiceProvider();
var first = provider.GetRequiredService<ProductCatalog>();
var second = provider.GetRequiredService<ProductCatalog>();
if (!ReferenceEquals(first, second))
    throw new InvalidOperationException("Documented cache owner must remain shared");
if (await first.GetNameAsync(1) != "Product" ||
    await second.GetNameAsync(1) != "Product" ||
    await second.GetNameAsync(2) != "Product" || handler.Requests != 2)
    throw new InvalidOperationException("Documented HTTP cache contract failed");
handler.Fail = true;
try
{
    await first.GetNameAsync(3);
    throw new InvalidOperationException("HTTP failure was concealed");
}
catch (HttpRequestException) { }
var conflicting = new ServiceCollection();
conflicting.AddSingleton<TypedCatalog>();
conflicting.AddHttpClient<TypedCatalog>(client =>
    client.BaseAddress = new Uri("https://api.example.com/"));
using var conflictingProvider = conflicting.BuildServiceProvider();
var typedFirst = conflictingProvider.GetRequiredService<TypedCatalog>();
var typedSecond = conflictingProvider.GetRequiredService<TypedCatalog>();
if (ReferenceEquals(typedFirst, typedSecond) ||
    typedFirst.Client.BaseAddress != new Uri("https://api.example.com/"))
    throw new InvalidOperationException("Typed registration overwrite was not reproduced");
Console.WriteLine("PASS: shipping named-client example preserves cache and propagates HTTP failure");
Console.WriteLine("PASS: later typed-client registration overrides singleton resolution");

public sealed class TypedCatalog(HttpClient client)
{
    public HttpClient Client { get; } = client;
}

sealed class LocalHandler : HttpMessageHandler
{
    public int Requests { get; private set; }
    public bool Fail { get; set; }
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken token)
    {
        if (request.RequestUri?.Host != "api.example.com")
            throw new InvalidOperationException("Named-client configuration was lost");
        Requests++;
        return Task.FromResult(new HttpResponseMessage(
            Fail ? HttpStatusCode.InternalServerError : HttpStatusCode.OK)
            { Content = new StringContent("Product") });
    }
}`;
  await writeFile(resolve(httpWorkspace, "Program.cs"), program);
  console.log(execFileSync("dotnet", ["run", "--project", "Probe.csproj", "--verbosity", "quiet"],
    { cwd: httpWorkspace, encoding: "utf8" }).trim());
  assert.ok(program.includes("AddSingleton<ProductCatalog>"));
  await writeFile(resolve(httpWorkspace, "Program.cs"),
    program.replace("AddSingleton<ProductCatalog>", "AddTransient<ProductCatalog>"));
  const broken = spawnSync("dotnet", ["run", "--project", "Probe.csproj", "--no-restore",
    "--verbosity", "quiet"], { cwd: httpWorkspace, encoding: "utf8" });
  assert.equal(broken.error, undefined);
  assert.notEqual(broken.status, 0);
  assert.match(broken.stdout + broken.stderr, /Documented cache owner must remain shared/);
  console.log("PASS: transient cache-owner mutation rejected; no real network requests");
} finally {
  await rm(httpWorkspace, { recursive: true, force: true });
}
