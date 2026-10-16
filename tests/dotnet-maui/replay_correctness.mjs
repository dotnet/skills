// Explicitly opt-in paid semantic replay; no agent execution or preference votes.
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const judgeModel = process.argv[2];
assert.ok(judgeModel, "Usage: node tests/dotnet-maui/replay_correctness.mjs <judge-model> [stimulus-name]");
assert.ok(process.argv.length <= 4, "Expected at most one stimulus selector");
const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, "../..");
const { loadEvalSpec } = await import(pathToFileURL(
  resolve(root, "eng/evaluation-tools/node_modules/@microsoft/vally/dist/index.js"),
));
const binding = await loadEvalSpec(resolve(here, "maui-data-binding/eval.yaml"));
const di = await loadEvalSpec(resolve(here, "maui-dependency-injection/eval.yaml"));
const safe = await loadEvalSpec(resolve(here, "maui-safe-area/eval.yaml"));
const fragment = '<Label Text="{Binding Value, Source={x:Reference amount}, x:DataType=Slider}" />';
const sourceAnswer = `${fragment} uses the existing page namespaces and Slider named amount. `
  + "Keep the invoice BindingContext and page x:DataType unchanged. "
  + "Enable MauiEnableXamlCBindingWithSourceCompilation if it is not already enabled.";
const fullPage = `<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
 xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
 xmlns:vm="clr-namespace:App.ViewModels" x:DataType="vm:InvoiceViewModel">
 <VerticalStackLayout>
  <Slider x:Name="amount" />
  ${fragment}
 </VerticalStackLayout>
</ContentPage>
Keep the existing invoice context and enable source compilation if needed.`;
const settingsAnswer = `<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
 xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
 xmlns:vm="clr-namespace:App.ViewModels" x:DataType="vm:SettingsViewModel">
 <VerticalStackLayout>
  <Label Text="{Binding Theme}" />
  <Switch IsToggled="{Binding NotificationsEnabled}" />
  <Stepper Value="{Binding FontSize}" />
 </VerticalStackLayout>
</ContentPage>
Keep the existing SettingsViewModel BindingContext. x:DataType is type metadata,
not a context assignment. Enable MauiStrictXamlCompilation and append XC0045 to
WarningsAsErrors while preserving existing warnings. XC0045 diagnoses invalid
typed members; missing-type fallback diagnostics are different.`;
const cases = [
  [safe, "maui-safe-area", "Keyboard avoidance with safe area for chat UI", "golden", null, true],
  [safe, "maui-safe-area", "Keyboard avoidance with safe area for chat UI", "direct-android",
    'On Android MAUI 10, use a bounded <ScrollView SafeAreaEdges="All">'
      + '<VerticalStackLayout><Label Text="Messages"/><Entry Placeholder="Message"/>'
      + '<Button Text="Send"/></VerticalStackLayout></ScrollView>. '
      + "All protects bars and the keyboard; keep the composer inside the protected scroller. "
      + "Resize still exists. Verify the target patch/window IME configuration and real device behavior; "
      + "iOS content-inset handling differs, so this Android implementation is not iOS keyboard proof.", true],
  [safe, "maui-safe-area", "Keyboard avoidance with safe area for chat UI", "unprotected-sibling",
    'Use <Grid RowDefinitions="*,Auto" SafeAreaEdges="None"><ScrollView Grid.Row="0" SafeAreaEdges="All">'
      + '<VerticalStackLayout/></ScrollView><Grid Grid.Row="1" ColumnDefinitions="*,Auto" SafeAreaEdges="None">'
      + '<Entry/><Button Grid.Column="1" Text="Send"/></Grid></Grid>. '
      + "The scroller automatically protects its sibling composer from the keyboard. Resize still exists.", false],
  [safe, "maui-safe-area", "Keyboard avoidance with safe area for chat UI", "bars-only",
    'Use <ContentPage SafeAreaEdges="Container"><Grid RowDefinitions="*,Auto">'
      + '<ScrollView/><Entry Grid.Row="1"/></Grid></ContentPage>. '
      + "Container alone protects the composer from SoftInput as well as bars. Resize still exists.", false],
  [safe, "maui-safe-area", "Keyboard avoidance with safe area for chat UI", "blanket-api-denial",
    'Use a Grid with RowDefinitions="*,Auto" and SafeAreaEdges="All", with the message scroller '
      + "and sibling composer in its two rows. This is mandatory on every platform because "
      + "ScrollView does not expose or process SoftInput or All anywhere. Resize still exists.", false],
  [safe, "maui-safe-area", "ScrollView keyboard wrapper", "golden", null, true],
  [safe, "maui-safe-area", "ScrollView keyboard wrapper", "direct-android",
    'On Android MAUI 10, check the exact MAUI patch, window IME configuration and finite viewport '
      + 'when the direct setup fails. Use <ScrollView SafeAreaEdges="All">'
      + '<VerticalStackLayout><Entry/><Button Text="Submit"/></VerticalStackLayout></ScrollView> '
      + "in the available bounded viewport; Submit scrolls with the protected form. "
      + "Android consumes IME insets here. Verify keyboard show/hide and focused-field scrolling "
      + "on the target device, not merely property assignment; iOS needs its own validation.", true],
  [safe, "maui-safe-area", "ScrollView keyboard wrapper", "unprotected-sibling",
    'Set SoftInput only on the ScrollView and keep Submit outside it on an edge-to-edge surface. '
      + "The scroller's inset automatically moves all its sibling controls too.", false],
  [safe, "maui-safe-area", "ScrollView keyboard wrapper", "false-platform-proof",
    'Use ScrollView.SafeAreaEdges = SafeAreaEdges.All with Submit in the bounded scrolling form. '
      + "The property accepts All, proving native keyboard delivery succeeds on every iOS and Android "
      + "version; no target-version, window or device check is needed.", false],
  [safe, "maui-safe-area", "ScrollView keyboard wrapper", "blanket-api-denial",
    'Use a page with SafeAreaEdges="All" and a Grid with RowDefinitions="*,Auto" containing '
      + "the bounded scroller and Submit. A shared inset owner is always mandatory because "
      + "ScrollView never supports or processes SoftInput on any platform.", false],
  [binding, "maui-data-binding", "Set up compiled bindings with x:DataType on a page",
    "golden", null, true],
  [binding, "maui-data-binding", "Set up compiled bindings with x:DataType on a page",
    "existing-context", settingsAnswer, true],
  [binding, "maui-data-binding", "Set up compiled bindings with x:DataType on a page",
    "invented-collection",
    settingsAnswer.replace('<Label Text="{Binding Theme}" />', '<Picker ItemsSource="{Binding Themes}" />'),
    false],
  [binding, "maui-data-binding", "Set up compiled bindings with x:DataType on a page",
    "scalar-items-source",
    settingsAnswer.replace('<Label Text="{Binding Theme}" />', '<Picker ItemsSource="{Binding Theme}" />'),
    false],
  [binding, "maui-data-binding", "Explicit source compilation", "golden", null, true],
  [binding, "maui-data-binding", "Explicit source compilation", "fragment",
    sourceAnswer, true],
  [binding, "maui-data-binding", "Explicit source compilation", "full-page",
    fullPage, true],
  [binding, "maui-data-binding", "Explicit source compilation", "multiple-content-roots",
    fullPage.replace(" <VerticalStackLayout>\n", "").replace(" </VerticalStackLayout>\n", ""), false],
  [binding, "maui-data-binding", "Explicit source compilation", "wrong-source-type",
    sourceAnswer.replace("x:DataType=Slider", "x:DataType=vm:InvoiceViewModel"), false],
  [binding, "maui-data-binding", "Preserve working supplied implementation", "golden",
    null, true],
  [binding, "maui-data-binding", "Preserve working supplied implementation", "concise-limitation",
    "The supplied managed behavior check passes and all inputs are unchanged. "
      + "This does not verify XAML compilation, native rendering or a device; none were run.", true],
  [binding, "maui-data-binding", "Preserve working supplied implementation", "equivalent-limitation",
    "Checks.cs passed and the supplied files are unchanged. Only property-change logic was "
      + "checked here; this says nothing about rendering or execution on a phone.", true],
  [binding, "maui-data-binding", "Preserve working supplied implementation", "negated-limitation",
    "Checks.cs passed and the supplied files are unchanged. The check only validates "
      + "view-model notification behavior. Platform-specific MAUI UI validation was not performed.", true],
  [binding, "maui-data-binding", "Preserve working supplied implementation", "missing-limitation",
    "The checks pass. All supplied files are unchanged.", false],
  [binding, "maui-data-binding", "Preserve working supplied implementation", "implicit-evidence-only",
    "The platform-neutral checks passed and all supplied files are unchanged.", false],
  [binding, "maui-data-binding", "Preserve working supplied implementation", "false-device-proof",
    "The checks pass and files are unchanged, proving the native MAUI page works on a device.", false],
  [di, "maui-dependency-injection", "Preserve a shared cache while fixing HTTP registration",
    "golden", null, true],
  [di, "maui-dependency-injection", "Preserve a shared cache while fixing HTTP registration",
    "separate-singleton-cache",
    "The later AddHttpClient<IProductCatalog, ProductCatalog> replaces normal singleton resolution "
      + "with a transient typed client. Remove the competing catalog Singleton registration. "
      + "Register AddSingleton<IProductCache, ProductCache>() and inject that cache into the "
      + "transient ProductCatalog(HttpClient, IProductCache). Keep the typed HTTP registration "
      + "and base URL; every catalog shares the cache but the HTTP consumer remains transient. "
      + "No network, build, device or file actions ran.", true],
  [di, "maui-dependency-injection", "Preserve a shared cache while fixing HTTP registration",
    "verified-default-retention",
    "The later typed AddHttpClient registration wins, so remove it and register a named client "
      + "with the API base URL. Construct one AddSingleton<IProductCatalog> using that named "
      + "factory client so the catalog cache stays shared. Retention is valid only after verifying "
      + ".NET 9+ on a SocketsHttpHandler-supported platform, the default primary handler with no "
      + "custom replacement, and an appropriate HandlerLifetime: this default sets "
      + "PooledConnectionLifetime from HandlerLifetime. Do not assume those prerequisites here. "
      + "If they cannot be verified, inject IHttpClientFactory and create/dispose a named client "
      + "per cache-miss operation instead. No actions or validation ran.", true],
  [di, "maui-dependency-injection", "Preserve a shared cache while fixing HTTP registration",
    "explicit-connection-policy",
    "The later typed registration makes catalog resolution transient. Remove it. Use a single "
      + "AddSingleton<IProductCatalog> registration whose HttpClient has the API base URL and "
      + "a SocketsHttpHandler configured with PooledConnectionLifetime=TimeSpan.FromMinutes(2), "
      + "on a platform supporting that handler. The catalog owns this long-lived client and the "
      + "shared cache; its owner disposes the client at shutdown. The connection policy, not "
      + "factory pooling alone, permits DNS refresh. No actions or validation ran.", true],
  [di, "maui-dependency-injection", "Preserve a shared cache while fixing HTTP registration",
    "pooling-alone-assurance",
    "The later AddHttpClient registration makes the catalog transient. Remove it, register a "
      + "named client with the API URL and capture CreateClient once in AddSingleton<IProductCatalog>. "
      + "Indefinite retention is always safe because IHttpClientFactory pools handlers; no "
      + "version, platform, primary-handler or connection-lifetime check is needed. "
      + "No actions or validation ran.", false],
  [di, "maui-dependency-injection", "Preserve a shared cache while fixing HTTP registration",
    "combined-lifetimes",
    "Keep AddSingleton<IProductCatalog, ProductCatalog>() followed by "
      + "AddHttpClient<IProductCatalog, ProductCatalog>(). Both lifetimes combine automatically "
      + "so one shared cache and safe HTTP ownership are guaranteed. No actions ran.", false],
];
const selectedCases = process.argv[3]
  ? cases.filter(([, , name]) => name === process.argv[3])
  : cases;
assert.ok(selectedCases.length, "Unknown stimulus selector");

for (const [spec, suite, name, label, output, passed] of selectedCases) {
  const original = spec.stimuli.find(stimulus => stimulus.name === name);
  assert.ok(original, name);
  const stimulus = structuredClone(original);
  assert.equal(stimulus.graders.find(grader => grader.type === "prompt").config.scoring, "binary");
  if (output !== null)
    stimulus.golden_trajectory.inline.steps.at(-1).message = output;
  const workspaceDir = await mkdtemp(resolve(here, ".correctness-"));
  const specPath = resolve(here, suite, `.correctness-${process.pid}.yaml`);
  let specWritten = false;
  try {
    // JSON is valid YAML; keep the temporary spec beside its production fixtures.
    await writeFile(specPath, JSON.stringify({ ...spec, stimuli: [stimulus] }), { flag: "wx" });
    specWritten = true;
    const execution = spawnSync(process.execPath, [
      resolve(root, "eng/evaluation-tools/vally.mjs"), "oracle",
      "--eval-spec", specPath, "--stimulus", name, "--workspace", workspaceDir,
      "--judge-model", judgeModel, "--output", "jsonl",
    ], { cwd: root, encoding: "utf8", maxBuffer: 10 * 1024 * 1024 });
    if (execution.error)
      throw execution.error;
    assert.equal(execution.signal, null, execution.stderr);
    const records = execution.stdout.split("\n").filter(line => line.startsWith("{")).map(line => JSON.parse(line));
    assert.equal(records.length, 1, execution.stdout + execution.stderr);
    const grade = records[0].gradeResult;
    assert.notEqual(records[0].status, "error", JSON.stringify(records[0]));
    assert.notEqual(grade.status, "error", JSON.stringify(grade));
    const prompts = grade.details.filter(grader => grader.kind === "llm");
    assert.equal(prompts.length, 1, "The production prompt grader must actually run");
    const [prompt] = prompts;
    assert.notEqual(prompt.status, "error", JSON.stringify(prompt));
    assert.equal(prompt.metadata.model, judgeModel);
    assert.equal(prompt.metadata.scoring, "binary");
    for (const grader of grade.details.filter(grader => grader.kind !== "llm"))
      assert.equal(grader.passed, true, "The semantic contrast must not fail on an unrelated deterministic grader");
    assert.equal(prompt.passed, passed, `${name}/${label}: ${JSON.stringify(grade)}`);
    assert.equal(grade.passed, passed, `${name}/${label}: ${JSON.stringify(grade)}`);
    if (passed)
      assert.equal(execution.status, 0, execution.stderr);
    for (const step of stimulus.golden_trajectory.inline.steps) {
      for (const call of step.tool_calls ?? []) {
        assert.equal(call.function_name, "bash", "Unsupported curated tool call");
        const replay = spawnSync("bash", ["-c", call.arguments.command], {
          cwd: workspaceDir, encoding: "utf8",
        });
        if (replay.error)
          throw replay.error;
        assert.equal(replay.status, 0, replay.stderr);
        const observation = step.observation.results.find(result => result.source_call_id === call.tool_call_id);
        assert.ok(observation, "Every curated tool call needs a replayable observation");
        assert.equal(replay.stdout.trim(), observation.content.trim(), "Curated observation must match real fixture execution");
      }
    }
    console.log(JSON.stringify({ judgeModel, name, label, expectedPass: passed, grade }));
  } finally {
    if (specWritten)
      await rm(specPath, { force: true });
    await rm(workspaceDir, { recursive: true, force: true });
  }
}
console.log(`PASS: ${selectedCases.length} production correctness-contract replays; no agent run or historical rescoring`);
