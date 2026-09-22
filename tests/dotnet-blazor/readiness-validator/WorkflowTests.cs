using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlazorComponentReadiness.Validator.Assessment;
using BlazorComponentReadiness.Validator.Cli;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Validation;
using CurrentAssessmentService = BlazorComponentReadiness.Validator.Assessment.AssessmentService;

internal static class WorkflowTests
{
    private static readonly string[] RequiredReferences =
    [
        "assessment-workflow.md",
        "offline-release-facts.md",
        "package-preparation.md",
        "scoped-component-profile.md",
        "input-candidates.md",
        "partner-preview.md",
        "report-contract.md",
        "remediation-guidance.md",
        "feedback-contract.md",
        "library-assessment.md",
        "worker-execution.md",
        "artifact-acquisition.md",
        "documentation-discovery.md",
        "status-boundaries.md",
        "targeted-profiles.md",
        "learning-loop.md",
        "blinded-comparison.md",
        "area-provenance-integrity.md",
        "area-security-privacy.md",
        "area-accessibility.md",
        "area-blazor-runtime.md",
        "area-trim-performance.md",
        "area-ci-release.md",
        "area-support-lifecycle.md",
        "area-conditional-families.md",
        "overlay-scaffolder.md",
        "overlay-ai-skill.md",
        "requirement-basis.md"
    ];

    private static readonly string[] Statuses =
    [
        "verified",
        "gap",
        "owner evidence required",
        "not tested",
        "not applicable"
    ];

    public static void Run(string pluginRoot)
    {
        var skillRoot = Path.Combine(pluginRoot, "skills", "blazor-component-readiness");
        var skillPath = Path.Combine(skillRoot, "SKILL.md");
        var referencesRoot = Path.Combine(skillRoot, "references");
        var skill = File.ReadAllText(skillPath);

        Assert(File.ReadLines(skillPath).Count() < 500, "portable skill must remain below 500 lines");
        Assert(skill.Length < 20_000, "portable skill must remain below the 5,000-token chars/4 warning proxy");
        AssertFrontmatter(skill, "fc2661e8a8ac2561f12c987945ab60efa01c0a39486d8ab38bc12f1f0e971f24", "skill");

        foreach (var reference in RequiredReferences)
        {
            var path = Path.Combine(referencesRoot, reference);
            Assert(File.Exists(path), $"required flattened reference is missing: {reference}");
            Assert(
                skill.Contains($"references/{reference}", StringComparison.Ordinal),
                $"portable skill must progressively link reference: {reference}");
        }

        AssertInstructionLinks(pluginRoot, skillPath, referencesRoot);
        AssertExecutionPrerequisite(pluginRoot, skill, referencesRoot);
        AssertReadingRoutes(skill, referencesRoot);

        AssertContains(skill, "explicit vendor self-assessment", "explicit-intent router");
        AssertContains(skill, "ordinary Blazor authoring/debugging", "ordinary-work dormancy");
        AssertContains(skill, "first-party framework review", "first-party dormancy");
        AssertContains(skill, "generic NuGet help", "NuGet dormancy");
        AssertContains(skill, "CI/PR/issue triage", "triage dormancy");
        AssertContains(skill, "implementation-only work", "implementation dormancy");
        AssertContains(skill, "certification", "certification boundary");
        AssertContains(skill, "Requires an active .NET 11 SDK", ".NET 11 prerequisite");
        AssertContains(skill, "structural validation", "structural/factual boundary");
        AssertContains(skill, "112 catalog entries", "core count");
        AssertContains(skill, "60 `repository-wide` / 52 `component-specific`", "scope ownership");
        AssertContains(skill, "`source-available`, `closed-source`, or `unresolved`", "source availability");
        AssertContains(skill, "owner-supplied-internal-evidence", "owner evidence");
        AssertContains(skill, "owner-supplied-public-evidence", "public owner evidence");
        AssertOwnedRules(skill, referencesRoot);
        AssertGuidanceClarity(skill, referencesRoot);
        AssertWorksheetPromptContracts(pluginRoot);
        AssertContains(skill, "Never run Git metadata commands inside an archive", "archive Git boundary");
        AssertContains(skill, "Package-only", "package-only mode");
        AssertContains(skill, "components: []", "empty package inventory");
        AssertContains(skill, "No component worker", "package-only worker boundary");
        AssertContains(skill, "Single component", "standalone component mode");
        AssertContains(skill, "Explicit package and component request", "separate dual-unit mode");
        AssertContains(skill, "Full library", "library mode");
        AssertContains(skill, "unsupported host: full-library assessment requires isolated workers", "unsupported-host stop");
        AssertContains(skill, "never use a shared-context or serialized fallback", "no shared-context fallback");
        AssertContains(skill, "interruption", "resume behavior");
        AssertContains(skill, "feedback", "feedback workflow");
        AssertContains(skill, "factual correction", "correction workflow");
        AssertContains(skill, "decision-guidance.md", "decision guidance");
        AssertContains(skill, "Run automatically", "automatic validation");
        var workflow = File.ReadAllText(Path.Combine(referencesRoot, "assessment-workflow.md"));
        Assert(
            Regex.IsMatch(
                workflow,
                @"assessment init --kind component\b[^\r\n]*--component <component-id>"),
            "standalone component init example requires the component identity");

        foreach (var status in Statuses)
        {
            AssertContains(skill, $"`{status}`", $"status '{status}'");
        }

        foreach (var limit in new[] { "256 MiB nupkg", "1 MiB nuspec", "4 MiB authored ledger",
                     "64 MiB serialized", "32 supplemental" })
        {
            AssertContains(
                File.ReadAllText(Path.Combine(referencesRoot, "artifact-acquisition.md")),
                limit,
                $"resource limit '{limit}'");
        }

        AssertRubricIsSoleRequirementSource(referencesRoot);
        AssertNoForbiddenCoupling(pluginRoot);
        AssertAgents(pluginRoot);
        AssertContracts(referencesRoot);
        AssertRemediationGuidance(pluginRoot, skill, referencesRoot);
        AssertRequestedHelpBoundaries(pluginRoot, referencesRoot);
        AssertGuidanceFixtures(pluginRoot);
        AssertComponentFixture(pluginRoot);
        AssertEmbeddedSbomFixtures(pluginRoot, referencesRoot);
        AssertWorkerLaunchContract(pluginRoot);
        AssertReadinessLauncherExample(skillRoot);
        AssertOptionalJqInventoryProjection(pluginRoot);
        AssertEvidenceTextConstraints(skillRoot);
        AssertSourceFindingExample(skillRoot);
    }

    private static void AssertEvidenceTextConstraints(string skillRoot)
    {
        var reference = File.ReadAllText(Path.Combine(skillRoot, "references", "input-candidates.md"));
        AssertContains(reference, "<launcher> evidence draft-add --help", "evidence text rules are discoverable from actual help");
        foreach (var constraint in new[]
        {
            "| `--claim` | 512 UTF-8 bytes |", "| `--method` | 512 UTF-8 bytes |",
            "| `--locator` | 2048 UTF-8 bytes; provenance-specific grammar also applies |",
            "| `--component` | 256 UTF-8 bytes when required by scope |",
            "inclusive", "bytes, not characters", "nonempty, NFC-normalized",
            "leading or trailing whitespace", "C0/C1", "except U+200C/U+200D",
            "internal whitespace", "syntactically atomic non-Markdown sentences",
            "component-specific", "omit it for repository-wide", "exact component spelling/case",
            "YYYY-MM-DDTHH:mm:ssZ", "`EV1-` followed by 64 lowercase hexadecimal digits",
            "report the first failure", "do not repair or truncate text"
        })
            AssertContains(reference, constraint, $"evidence authoring constraint: {constraint}");
        AssertBefore(reference, "Evidence text limits", "CAPTURED_AT=",
            "authors see byte and text constraints before the existing producer examples");
    }

    private static void AssertSourceFindingExample(string skillRoot)
    {
        var reference = File.ReadAllText(Path.Combine(skillRoot, "references", "area-blazor-runtime.md"));
        var candidates = File.ReadAllText(Path.Combine(skillRoot, "references", "input-candidates.md"));
        AssertContains(candidates, "area-blazor-runtime.md#synthetic-source-finding-example",
            "input owner links to complete source-finding example");
        AssertContains(reference, "[synthetic worked example](#synthetic-source-finding-example)",
            "source-proof boundary discovers the worked flow");
        const string assetLink = "../assets/source-finding/SyntheticCallbackGroup.cs.txt";
        AssertContains(reference, assetLink, "inert template has a plugin-local content link");
        var asset = Path.GetFullPath(Path.Combine(skillRoot, "references", assetLink));
        Assert(File.Exists(asset), "source-finding content is delivered as text");
        Assert(!File.GetAttributes(asset).HasFlag(FileAttributes.ReparsePoint), "source-finding template is a regular file");
        Assert(!Path.GetRelativePath(skillRoot, asset).StartsWith("..", StringComparison.Ordinal),
            "source-finding link stays inside portable skill");
        Assert(!File.Exists(Path.Combine(Path.GetDirectoryName(asset)!, "SyntheticCallbackGroup.cs")),
            "no executable-suffix counterpart ships in plugin assets");
        var source = File.ReadAllText(asset);
        AssertContains(source, "_ = SelectionChanged.InvokeAsync(child);", "synthetic callback is deliberately unawaited");
        AssertContains(source, "return Task.CompletedTask;", "source finding is observable without execution");
        foreach (var boundary in new[]
        {
            "reader must already own", "does not supply a package", "test-only", "not reader deliverables",
            "no checkout/cache fallback", "Never compile, script", "no `component_id` field",
            "without BOM or trailing newline", "not** mean an operation ran",
            "No runtime operation was performed", "all unrelated rows still null", "completion_state:",
            "incomplete", "No report, revision or reader", "not `owner_inputs`"
        })
            AssertContains(reference, boundary, "source-finding ownership/execution boundary");
        var blocks = string.Join("\n", new[]
        {
            "### Validate the existing setup", "### Materialize both complete protocols",
            "### Reconfirm inputs and bind the evidence", "### Author only the source-backed gap"
        }.Select(anchor => EvidenceTests.ExtractCodeBlock(reference, anchor, "powershell")));
        foreach (var required in new[]
        {
            "System.Text.Json.Utf8JsonWriter", "--input $Candidates",
            "inputs discover", "inputs confirm", "inputs validate",
            "assessment init --kind component --component $ComponentId", "assessment export-identity",
            "evidence draft-add", "--kind reviewer-generated-analysis", "--kind reproduced-runtime-observation",
            "evidence ledger-build --kind component", "evidence ledger-validate",
            "--root $InputRoot --manifest $finalInput --output $bundle",
            "assessment canonicalize", "assessment validate", "-ceq 'BEQ-12'"
        })
            AssertContains(blocks, required, "complete existing producer path");
        Assert(!Regex.IsMatch(blocks, @"(?im)^\s*(Add-Type|Invoke-Expression|dotnet\s+(run|build|publish))\b"),
            "example has no assessed-source execution command");
    }

    private static void AssertFrontmatter(string text, string expected, string label)
    {
        var frontmatter = Regex.Match(text, @"\A---\r?\n.*?\r?\n---\r?\n", RegexOptions.Singleline).Value;
        Assert(frontmatter.Length > 0, $"{label} frontmatter must exist");
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(frontmatter)));
        Assert(digest == expected, $"{label} routing metadata/tools/frontmatter bytes must stay unchanged");
    }

    private static void AssertInstructionLinks(string pluginRoot, string skillPath, string referencesRoot)
    {
        var paths = new[] { skillPath, Path.Combine(pluginRoot, "README.md") }
            .Concat(RequiredReferences.Select(name => Path.Combine(referencesRoot, name)))
            .Concat(Directory.GetFiles(Path.Combine(pluginRoot, "agents"), "*.agent.md"));
        foreach (var path in paths)
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(path), @"\]\(([^)]+)\)"))
            {
                var value = match.Groups[1].Value;
                if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                    value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Assert(!Path.IsPathRooted(value), $"instruction link must be relative: {path}: {value}");
                var parts = value.Replace('\\', '/').Split('#', 2);
                if (path == skillPath)
                {
                    Assert(parts[0].Split('/', StringSplitOptions.RemoveEmptyEntries).Length <= 2,
                        $"skill link must be at most one directory deep: {value}");
                }

                var target = parts[0].Length == 0 ? path :
                    Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, parts[0]));
                var relative = Path.GetRelativePath(pluginRoot, target);
                Assert(!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative),
                    $"instruction link must stay within the portable plugin: {value}");
                Assert(File.Exists(target), $"broken instruction link: {path}: {value}");
                if (parts.Length == 2)
                {
                    var anchors = Regex.Matches(File.ReadAllText(target), @"(?m)^#{1,6} (.+?)\r?$")
                        .Select(heading => Regex.Replace(heading.Groups[1].Value.ToLowerInvariant(),
                            @"[^\p{L}\p{N}\s_-]", "").Replace(' ', '-'));
                    Assert(anchors.Contains(parts[1], StringComparer.Ordinal),
                        $"broken instruction anchor: {path}: {value}");
                }
            }
        }
    }

    private static void AssertExecutionPrerequisite(string pluginRoot, string skill, string referencesRoot)
    {
        var prerequisite = Regex.Match(skill,
            @"(?ms)^## Assessed-code execution prerequisite\r?\n.*?(?=^## |\z)").Value;
        foreach (var rule in new[]
        {
            "Static inspection is distinct from restoring, building, publishing, starting, or executing",
            "explicit authorization for that exact executable work",
            "inputs whose trust has not been established",
            "host-enforced controls isolating credentials, filesystem access and network access",
            "This skill implements no sandbox",
            "Disposable directories, hashes, input/scope confirmation",
            "tool or CLI approvals, path/URL grants and worker prompts are not OS confinement",
            "do not execute assessed code",
            "Continue authorized static work",
            "`not tested` with the actual prerequisite blocker",
            "Do not expand permissions, install tools, retry a denied operation, or qualify another host as a workaround",
            "does not prohibit authorized static inspection or use of the trusted bundled validator"
        })
            AssertContains(prerequisite, rule, "authoritative execution prerequisite");

        foreach (var path in new[]
        {
            Path.Combine(pluginRoot, "README.md"),
            Path.Combine(pluginRoot, "agents", "blazor-component-readiness.agent.md"),
            Path.Combine(pluginRoot, "agents", "blazor-component-readiness-worker.agent.md"),
            Path.Combine(referencesRoot, "assessment-workflow.md"),
            Path.Combine(referencesRoot, "worker-execution.md"),
            Path.Combine(referencesRoot, "area-blazor-runtime.md"),
            Path.Combine(referencesRoot, "area-trim-performance.md")
        })
            AssertContains(File.ReadAllText(path), "SKILL.md#assessed-code-execution-prerequisite",
                $"{Path.GetFileName(path)} routes executable work to the common prerequisite");

        foreach (var text in new[]
        {
            skill,
            File.ReadAllText(Path.Combine(pluginRoot, "README.md")),
            File.ReadAllText(Path.Combine(referencesRoot, "requirement-basis.md")),
            File.ReadAllText(Path.Combine(referencesRoot, "checklist.md"))
        })
        {
            AssertContains(text, "bundled partner-readiness baseline", "default partner baseline is explicit");
            AssertContains(text, "not a universal engineering or adoption standard for every Blazor library",
                "default baseline is not a universal adoption standard");
        }
    }

    private static void AssertReadingRoutes(string skill, string referencesRoot)
    {
        var routes = new (string Name, string[] Owners)[]
        {
            ("Single component", ["scoped-component-profile.md", "assessment-workflow.md"]),
            ("Package-only", ["assessment-workflow.md"]),
            ("Explicit package and component request",
                ["report-contract.md#ordinary-component-binding", "library-assessment.md#split-coordination"]),
            ("Full library", ["library-assessment.md", "worker-execution.md"]),
            ("Targeted follow-up / worksheet", ["targeted-profiles.md", "status-boundaries.md"]),
            ("Offline release facts / authorized identity-only handoff", ["offline-release-facts.md", "input-candidates.md"]),
            ("Optional scoped-package preparation", ["package-preparation.md", "input-candidates.md"]),
            ("Existing reader, feedback or correction", ["report-contract.md", "partner-preview.md", "feedback-contract.md"]),
            ("Requested recommendations or troubleshooting", ["remediation-guidance.md"]),
            ("Explicit blinded comparison", ["blinded-comparison.md", "worker-execution.md"])
        };
        foreach (var (name, owners) in routes)
        {
            var row = skill.Split('\n').Single(line => line.StartsWith($"| **{name}** |", StringComparison.Ordinal));
            foreach (var owner in owners)
            {
                AssertContains(row, $"(references/{owner})", $"explicit next-read owner for {name}");
            }
            if (name is "Package-only" or "Offline release facts / authorized identity-only handoff" or
                "Optional scoped-package preparation")
            {
                foreach (var forbidden in new[] { "library-assessment.md", "worker-execution.md",
                             "area-blazor-runtime.md", "area-accessibility.md" })
                {
                    Assert(!row.Contains($"(references/{forbidden})", StringComparison.Ordinal),
                        $"{name} must not require {forbidden}");
                }
            }
            if (name == "Optional scoped-package preparation")
            {
                foreach (var boundary in new[] { "operator-invoked", "authorized-package-48/1.0.0",
                             "optional, not a component or package-assessment prerequisite", "does not assign statuses" })
                    AssertContains(row, boundary, "optional preparation route");
            }
            if (name == "Targeted follow-up / worksheet")
            {
                Assert(!row.Contains("(references/assessment-workflow.md)", StringComparison.Ordinal),
                    "worksheet guidance must not require the canonical assessment workflow");
                AssertContains(row, "only named areas", "worksheet reads remain task-scoped");
                AssertContains(row, "canonical correction steps only when requested",
                    "worksheet route does not silently become a canonical correction");
            }
            if (name == "Existing reader, feedback or correction")
            {
                AssertContains(row, "Rendering alone does not require reacquisition/retesting",
                    "shared acquisition guidance does not force work for rendering");
            }
            if (name == "Requested recommendations or troubleshooting")
            {
                AssertContains(row, "advice, not execution", "advice does not authorize probes");
                AssertContains(row, "need no failed criterion or new assessment", "general help has no assessment prerequisite");
            }
        }

        // Required edges are explicit, task-scoped prerequisites, not every navigation backlink.
        var edges = new (string Owner, string Target)[]
        {
            ("assessment-workflow.md", "input-candidates.md"),
            ("assessment-workflow.md", "status-boundaries.md"),
            ("assessment-workflow.md", "area-provenance-integrity.md"),
            ("assessment-workflow.md", "artifact-acquisition.md"),
            ("assessment-workflow.md", "artifact-acquisition.md#original-library-source-closure"),
            ("assessment-workflow.md", "documentation-discovery.md"),
            ("assessment-workflow.md", "report-contract.md"),
            ("assessment-workflow.md", "partner-preview.md"),
            ("offline-release-facts.md", "input-candidates.md"),
            ("library-assessment.md", "worker-execution.md"),
            ("area-accessibility.md", "area-blazor-runtime.md#cheap-preflight"),
            ("area-trim-performance.md", "artifact-acquisition.md#original-library-source-closure"),
            ("report-contract.md", "remediation-guidance.md"),
            ("partner-preview.md", "remediation-guidance.md")
        };
        foreach (var (owner, target) in edges)
        {
            AssertContains(File.ReadAllText(Path.Combine(referencesRoot, owner)), $"({target})",
                $"{owner} required prerequisite {target}");
        }
        var workflow = File.ReadAllText(Path.Combine(referencesRoot, "assessment-workflow.md"));
        var offline = File.ReadAllText(Path.Combine(referencesRoot, "offline-release-facts.md"));
        var input = File.ReadAllText(Path.Combine(referencesRoot, "input-candidates.md"));
        AssertContains(offline, "does not require", "unscored route exclusions");
        AssertContains(input, "does not require", "unscored producer exception");
        foreach (var forbidden in new[] { "library-assessment.md", "worker-execution.md", "area-blazor-runtime.md",
                     "area-accessibility.md", "assessment-workflow.md" })
        {
            Assert(!offline.Contains($"({forbidden})", StringComparison.Ordinal),
                $"offline facts must not add a mandatory {forbidden} read");
            Assert(!input.Contains($"({forbidden})", StringComparison.Ordinal),
                $"shared producer must not add a mandatory {forbidden} read");
        }
        Assert(!workflow.Contains("comparison inputs-freeze", StringComparison.Ordinal) &&
               !workflow.Contains("inventory discover", StringComparison.Ordinal) &&
               !workflow.Contains("## Render-mode matrix", StringComparison.Ordinal),
            "common workflow must not embed conditional comparison, library or component procedures");
        AssertContains(skill, "not mandatory reread cycles", "finite reading graph");
        AssertContains(skill, "Acquisition/document discovery may be skipped", "confirmed-input route");

        AssertBefore(workflow, "(scoped-component-profile.md)", "assessment init --kind", "profile before generic initialization");
        AssertBefore(workflow, "(status-boundaries.md)", "## 4.", "status rules before canonical production");
        AssertBefore(workflow, "explicitly", "assessment export-identity", "confirmation before identity");
        AssertBefore(workflow, "Attempt each authorized, accessible family", "## 3.",
            "accessible evidence collection precedes final row decisions");
        AssertBefore(input, "(scoped-component-profile.md)", "assessment init --kind", "profile before producer initialization");
        var report = File.ReadAllText(Path.Combine(referencesRoot, "report-contract.md"));
        AssertBefore(report, "(scoped-component-profile.md)", "## Ordinary component binding", "profile before ordinary binding");
        var reader = File.ReadAllText(Path.Combine(referencesRoot, "partner-preview.md"));
        AssertBefore(reader, "(scoped-component-profile.md)", "reader render", "profile before reader generation");
        var feedback = File.ReadAllText(Path.Combine(referencesRoot, "feedback-contract.md"));
        AssertBefore(feedback, "(scoped-component-profile.md)", "Feedback keys", "component scope before feedback keys");
        var worker = File.ReadAllText(Path.Combine(referencesRoot, "worker-execution.md"));
        AssertBefore(worker, "(scoped-component-profile.md)", "1. Copy only", "profile before ordinary unit staging");
        AssertContains(report, "### Component scope and disclosure", "normal component disclosure contract");
        AssertContains(File.ReadAllText(Path.Combine(referencesRoot, "artifact-acquisition.md")),
            "## Offline release facts", "old acquisition anchor preserved");
    }

    private static void AssertBefore(string text, string prerequisite, string action, string label)
    {
        var first = text.IndexOf(prerequisite, StringComparison.Ordinal);
        var next = text.IndexOf(action, StringComparison.Ordinal);
        Assert(first >= 0 && next > first, label);
    }

    private static void AssertRemediationGuidance(string pluginRoot, string skill, string referencesRoot)
    {
        var guidance = File.ReadAllText(Path.Combine(referencesRoot, "remediation-guidance.md"));
        var report = File.ReadAllText(Path.Combine(referencesRoot, "report-contract.md"));
        var agent = File.ReadAllText(Path.Combine(pluginRoot, "agents", "blazor-component-readiness.agent.md"));
        foreach (var text in new[] { agent, report, guidance })
            AssertContains(text, "existing validated revision", "guidance uses retained validation");
        AssertBefore(agent, "(../skills/blazor-component-readiness/references/remediation-guidance.md)",
            "For ordinary canonical units", "advice-only routing precedes assessment work");
        AssertContains(agent, "does not enter the canonical assessment steps below", "advice-only route boundary");
        foreach (var rule in new[] { "Source validation manifest SHA-256:", "No feedback file is required",
                     "No filename request or second confirmation", "may remain empty",
                     "adds no validation requirement", "Replace it only after another explicit guidance request" })
            AssertContains(report, rule, "optional guidance contract");
        foreach (var rule in new[] { "Factual-report-only requests create no companion", "Do not rerun the assessment",
                     "or perform network research", "only requested unresolved findings",
                     "exact missing evidence or owner decision", "Evidence to support reassessment",
                     "Implementation references", "Owner decisions and limitations", "Authority disclaimer",
                     "canonical bytes, statuses, evidence, counts, reports and manifests unchanged",
                     "outside `revisions/`", "not mandatory designs", "unsigned build digest",
                     "signed final digest", "author-signed/upload digest equality", "Signing may change bytes",
                     "do not require unsigned and signed digests to be equal",
                     "preserved author signature", "repository signature or countersignature",
                     "upload/distribution digest equality after repository signing",
                     "Missing signature/correspondence records remain unresolved" })
            AssertContains(guidance, rule, "bounded remediation guidance");
        var ids = Regex.Matches(guidance, @"\b(?:PI|CI)-\d{2}\b").Select(match => match.Value).ToHashSet();
        Assert(ids.SetEquals(["PI-03", "PI-05", "PI-06", "PI-07", "PI-08", "PI-09", "PI-10", "PI-11",
                             "CI-05", "CI-07", "CI-08"]), "authored guidance stays in the approved SBOM/release slice");
        foreach (var reference in new[] {
            "https://github.com/microsoft/sbom-tool/blob/4091b7bcce1640c4db42b5fad63d7d1b7bc0e4cf/README.md",
            "https://learn.microsoft.com/nuget/nuget-org/trusted-publishing",
            "https://learn.microsoft.com/nuget/create-packages/sign-a-package",
            "https://learn.microsoft.com/dotnet/core/tools/dotnet-nuget-verify",
            "https://docs.github.com/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations",
            "https://learn.microsoft.com/nuget/reference/signed-packages-reference" })
            AssertContains(guidance, reference, "verified primary implementation reference");
        AssertContains(guidance, "Reviewed **2026-09-14**", "source review date");
        AssertContains(guidance, "`PI-10` and `PI-11` are **versioned extensions**", "PI extension classification");
        AssertContains(guidance, "`CI-05`, `CI-07`, and `CI-08` are **versioned extensions**", "CI extension classification");
        AssertContains(guidance, "predicate type matching the actual SBOM", "attestation format correspondence");
    }

    private static void AssertGuidanceFixtures(string pluginRoot)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(pluginRoot, "..", ".."));
        var fixtureRoot = Path.Combine(repositoryRoot, "tests", "dotnet-blazor", "blazor-component-readiness", "fixtures");
        var helper = Path.Combine(fixtureRoot, "fixture-tool.py");
        var snapshot = Path.Combine(fixtureRoot, "guidance-revisions.zip.b64");
        var eval = Path.Combine(fixtureRoot, "..", "eval.yaml");
        var evalText = File.ReadAllText(eval);
        Assert(!Regex.IsMatch(evalText, @"(?m)^\s*(grading_environment|output_delivery):") &&
            !evalText.Contains("\"write_only\"", StringComparison.Ordinal),
            "guidance eval must use the repository-pinned Vally schema");
        AssertContains(evalText, "They do not prove the absence of transient",
            "automatic grader scope is explicit");
        AssertContains(evalText, "complete retained results.jsonl trajectory and events.jsonl calls",
            "live no-rerun conclusion requires complete raw-event review");
        Assert(Regex.Matches(evalText,
            @"(?m)^\s*(?:args:\s*\r?\n\s*- -I\s*\r?\n\s*- -c\s*\r?\n\s*- &guidance_program|args: \[-I, -c, \*guidance_program,)").Count == 3,
            "all guidance grader bootstraps ignore workspace import shadowing");
        using var tools = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repositoryRoot, "eng", "evaluation-tools", "package.json")));
        Assert(tools.RootElement.GetProperty("dependencies").GetProperty("@microsoft/vally-cli").GetString() == "0.14.0",
            "guidance grader contract is tested against the declared Vally 0.14.0 pin");
        var root = Path.Combine(Environment.GetEnvironmentVariable("READINESS_TEST_ARTIFACTS") ?? Path.GetTempPath(),
            "guidance-" + Guid.NewGuid().ToString("N"));
        var previous = Environment.GetEnvironmentVariable("READINESS_SKILL_ROOT");
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("READINESS_SKILL_ROOT", Path.Combine(pluginRoot, "skills", "blazor-component-readiness"));
        try
        {
            foreach (var item in new[] { ("established", "gap"), ("insufficient", "not tested") })
            {
                var caseRoot = Path.Combine(root, item.Item1);
                var setup = RunGuidanceHelper(helper, "prepare-guidance", "--snapshot", snapshot, "--case", item.Item1,
                    "--root", caseRoot, "--grading-files");
                Assert(setup.ExitCode == 0, $"guidance fixture setup exit {setup.ExitCode}: {setup.StandardError}");
                var helperDigest = Convert.ToHexStringLower(SHA256.HashData(
                    File.ReadAllBytes(Path.Combine(caseRoot, ".grading", "guidance-tool.py"))));
                AssertContains(evalText, $"expected = \"{helperDigest}\"", "staged grader helper pinned by trusted program config");
                var hashesDigest = Convert.ToHexStringLower(SHA256.HashData(
                    File.ReadAllBytes(Path.Combine(caseRoot, ".grading", "retained-hashes.json"))));
                AssertContains(evalText, $"&guidance_{item.Item1}_digest {hashesDigest}", "selected-case hash manifest pinned");
                Assert(Directory.GetFiles(Path.Combine(caseRoot, ".grading")).Length == 2,
                    "no eval spec, alternative case or canned remediation answers staged");
                var revision = RevisionService.VerifyRevision(caseRoot, Path.Combine(caseRoot, "out", "revisions", "0001"),
                    null, null, validateChain: true);
                Assert(revision.Assessment.Rows.Count == 60 && revision.Assessment.RubricVersion == "2.1.0",
                    "guidance fixtures are genuinely validated current ordinary package revisions");
                foreach (var id in new[] { "PI-07", "CI-07", "CI-08" })
                {
                    var row = revision.Assessment.Rows.Single(row => row.Id == id);
                    Assert(row.Status == item.Item2 && row.EvidenceIds.Count == 1,
                        "guidance fixtures retain exact unresolved results and supporting evidence");
                    Assert(row.OwnerAction is null, "optional default actions remain valid when empty");
                }
                Assert(Directory.GetFiles(revision.Directory).Length == 5 &&
                    !Directory.GetFiles(caseRoot, "decision-guidance.md", SearchOption.AllDirectories).Any(),
                    "retained revision has no prewritten guidance or feedback prerequisite");
            }
            var controls = RunGuidanceHelper(helper, "guidance-selftests", "--snapshot", snapshot, "--eval", eval,
                "--scratch", Path.Combine(root, "controls"));
            Assert(controls.ExitCode == 0 && controls.StandardOutput.Contains("VALID guidance controls 77", StringComparison.Ordinal),
                $"guidance controls exit {controls.ExitCode}: {controls.StandardOutput} {controls.StandardError}");
            Console.Write(controls.StandardOutput);
        }
        finally
        {
            Environment.SetEnvironmentVariable("READINESS_SKILL_ROOT", previous);
            Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertRequestedHelpBoundaries(string pluginRoot, string referencesRoot)
    {
        var guidance = File.ReadAllText(Path.Combine(referencesRoot, "remediation-guidance.md"));
        foreach (var rule in new[]
        {
            "Requested documentation-testing help", "Requested data-transfer troubleshooting",
            "An assessment or failed row is not a prerequisite", "answer inline",
            "Do not create an assessment, invent a finding, add a criterion",
            "may be discussed even when its related DOCX check is verified",
            "do not authorize probes, installation, external research or execution",
            "BEQ-23 already covers locating public samples",
            "A full-dataset transfer may be intentional",
            "does not disable normal work needed for PERF-06"
        })
        {
            AssertContains(guidance, rule, "request-only help without replacement obligations");
        }

        var repositoryRoot = Path.GetFullPath(Path.Combine(pluginRoot, "..", ".."));
        var helper = Path.Combine(repositoryRoot, "tests", "dotnet-blazor", "blazor-component-readiness",
            "fixtures", "fixture-tool.py");
        var scratch = Path.Combine(Environment.GetEnvironmentVariable("READINESS_TEST_ARTIFACTS") ??
            Path.GetTempPath(), "requested-help-" + Guid.NewGuid().ToString("N"));
        var result = RunGuidanceHelper(helper, "requested-help-selftests", "--scratch", scratch);
        Assert(result.ExitCode == 0 && result.StandardOutput.Contains("VALID requested help controls 24", StringComparison.Ordinal),
            $"requested-help controls: {result.StandardOutput} {result.StandardError}");
        Console.Write(result.StandardOutput);
    }

    private static void AssertComponentFixture(string pluginRoot)
    {
        var repository = Path.GetFullPath(Path.Combine(pluginRoot, "..", ".."));
        var fixture = Path.Combine(repository, "tests", "dotnet-blazor", "blazor-component-readiness", "fixtures", "component");
        var root = Path.Combine(Environment.GetEnvironmentVariable("READINESS_TEST_ARTIFACTS") ??
            Path.GetTempPath(), "component-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var path in Directory.GetFiles(fixture))
            {
                File.Copy(path, Path.Combine(root, Path.GetFileName(path)));
            }
            var package = Path.Combine(root, "package.nupkg");
            File.WriteAllBytes(package, Convert.FromBase64String(File.ReadAllText(Path.Combine(root, "package.nupkg.b64"))));
            var input = BlazorComponentReadiness.Validator.Inputs.InputManifestService.Discover(
                root, package, File.ReadAllBytes(Path.Combine(root, "candidates.json")));
            foreach (var (entry, file) in new[] { ("README.md", "package-readme.md"), ("contentFiles/any/any/Grid.razor", "Grid.razor") })
            {
                var digest = NupkgInspector.ComputeEvidenceContentSha256(package, "package:entry/" + entry);
                Assert(digest == Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, file)))),
                    "component fixture's declared package/source capture must match actual archive bytes");
            }
            Assert(input.Components.Count == 1 && input.Components[0].Id == "grid", "component fixture selects only Grid");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertEmbeddedSbomFixtures(string pluginRoot, string referencesRoot)
    {
        var provenance = File.ReadAllText(Path.Combine(referencesRoot, "area-provenance-integrity.md"));
        AssertContains(provenance, "nonempty `sourcesContent`", "distributed embedded content is an applicable surface");
        AssertContains(provenance, "`node_modules` matches are only part", "positive dependency matches are not exhaustive");
        AssertContains(provenance, "otherwise evidenced", "missing metadata is not a new universal failure rule");
        AssertContains(provenance, "including an orphaned map that no bundle references",
            "distributed source-map bytes do not require bundle linkage");
        AssertContains(provenance, "not whether the map's embedded bytes shipped",
            "bundle correspondence is distinct from distribution");
        AssertContains(File.ReadAllText(Path.Combine(referencesRoot, "status-boundaries.md")),
            "Positive matches remain supported", "partial positives survive a proved omission");
        AssertContains(File.ReadAllText(Path.Combine(referencesRoot, "remediation-guidance.md")),
            "remove the distributed content if the owner determines", "bounded omission remedy preserves owner decisions");
        var repositoryRoot = Path.GetFullPath(Path.Combine(pluginRoot, "..", ".."));
        var fixtureRoot = Path.Combine(repositoryRoot, "tests", "dotnet-blazor", "blazor-component-readiness", "fixtures");
        var artifacts = Environment.GetEnvironmentVariable("READINESS_TEST_ARTIFACTS");
        var root = Path.Combine(artifacts ?? Path.GetTempPath(),
            "embedded-" + Guid.NewGuid().ToString("N"));
        var helper = Path.Combine(fixtureRoot, "fixture-tool.py");
        var nativeControls = Path.Combine(root, "native-controls");
        var previous = Environment.GetEnvironmentVariable("READINESS_SKILL_ROOT");
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("READINESS_SKILL_ROOT", Path.Combine(pluginRoot, "skills", "blazor-component-readiness"));
        try
        {
            foreach (var (name, status) in new[] { ("release-01", "verified"), ("release-02", "gap"), ("release-03", "not tested") })
            {
                var caseRoot = Path.Combine(nativeControls, name);
                var setup = RunGuidanceHelper(helper, "prepare-guidance",
                    "--snapshot", Path.Combine(fixtureRoot, "guidance-revisions.zip.b64"),
                    "--case", "established", "--root", caseRoot);
                Assert(setup.ExitCode == 0, $"native grader control setup: {setup.StandardError}");
                var seed = Path.Combine(caseRoot, "out", "revisions", "0001");
                var retained = RevisionService.VerifyRevision(caseRoot, seed, null, null, validateChain: true);
                var evidenceId = retained.Assessment.Rows.Single(row => row.Id == "PI-07").EvidenceIds.Single();
                var rows = retained.Assessment.Rows.Select(row => row.Id == "PI-08" ? row with
                {
                    Status = status,
                    Observation = "Synthetic structural grader control, not a finding about a raw release fixture.",
                    EvidenceIds = [evidenceId]
                } : row).ToArray();
                var summaries = RubricLoader.Load(retained.Assessment.RubricVersion).Statuses
                    .Select(value => (Status: value, Rows: rows.Where(row => row.Status == value).ToArray()))
                    .Where(group => group.Rows.Length > 0)
                    .Select(group => new AssessmentSummaryGroup(group.Status, "Synthetic structural grader control.",
                        group.Rows.Select(row => row.Id).ToArray(),
                        group.Rows.SelectMany(row => row.EvidenceIds).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()))
                    .ToArray();
                var assessmentPath = Path.Combine(caseRoot, "control.assessment.json");
                File.WriteAllBytes(assessmentPath, CurrentAssessmentService.Serialize(retained.Assessment with
                {
                    Rows = rows,
                    SummaryGroups = summaries
                }));
                var generated = Path.Combine(caseRoot, "generated");
                Directory.CreateDirectory(generated);
                var revisions = Path.Combine(generated, "revisions");
                var revision = Path.Combine(revisions, "0001");
                var reader = Path.Combine(generated, "readable");
                Native(ExitCodes.Success, "report", "render", "--root", caseRoot,
                    "--input", Path.Combine(seed, "input-manifest.json"), "--assessment", assessmentPath,
                    "--evidence", Path.Combine(seed, "package.evidence.json"), "--output", revisions);
                Native(ExitCodes.Success, "report", "verify", "--root", caseRoot, "--revision", revision);
                Native(ExitCodes.Success, "reader", "render", "--root", caseRoot, "--revision", revision, "--output", reader);
                Native(ExitCodes.Success, "reader", "verify", "--root", caseRoot, "--revision", revision, "--output", reader);

                // The program grader is structural; only the existing native verifier asserts byte binding.
                foreach (var file in new[] { "package.report.md", "package.evidence.json" })
                {
                    var target = Path.Combine(revision, file);
                    var original = File.ReadAllBytes(target);
                    try
                    {
                        File.WriteAllBytes(target, [.. original, (byte)'\n']);
                        Native(ExitCodes.ValidationFailure, "report", "verify", "--root", caseRoot, "--revision", revision);
                    }
                    finally
                    {
                        File.WriteAllBytes(target, original);
                    }
                }
                Native(ExitCodes.Success, "report", "verify", "--root", caseRoot, "--revision", revision);
                Native(ExitCodes.Success, "reader", "verify", "--root", caseRoot, "--revision", revision, "--output", reader);
            }
            var result = RunGuidanceHelper(helper, "embedded-sbom-selftests",
                "--snapshot", Path.Combine(fixtureRoot, "embedded-assets.zip.b64"),
                "--eval", Path.Combine(fixtureRoot, "..", "eval.yaml"), "--scratch", Path.Combine(root, "controls"),
                "--controls", nativeControls);
            Assert(result.ExitCode == 0 && result.StandardOutput.Contains("VALID embedded SBOM controls 54", StringComparison.Ordinal),
                $"embedded SBOM controls exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
            Console.Write(result.StandardOutput);
            Console.WriteLine("VALID embedded native report/evidence tamper controls 6");
            if (artifacts is not null)
                Console.WriteLine($"Native embedded grader controls: {nativeControls}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("READINESS_SKILL_ROOT", previous);
            if (artifacts is null)
                Directory.Delete(root, recursive: true);
        }

        static void Native(int expected, params string[] arguments)
        {
            var error = new StringWriter();
            var exit = CliApplication.Run(arguments, new StringWriter(), error);
            Assert(exit == expected, $"native embedded grader control {string.Join(' ', arguments.Take(2))}: exit {exit}, expected {expected}: {error}");
        }
    }

    private static ProcessResult RunGuidanceHelper(string helper, params string[] arguments)
    {
        var start = new ProcessStartInfo("python")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(helper);
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("could not start guidance fixture helper");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }

    private static void AssertOwnedRules(string skill, string referencesRoot)
    {
        var sharedPrerequisites = Regex.Match(skill,
            @"(?ms)\A.*?(?=^## Select the task and next reads\r?$)").Value;
        var workflow = NormalizeWhitespace(File.ReadAllText(Path.Combine(referencesRoot, "assessment-workflow.md")));
        foreach (var rule in new[] { "truncated output saved to a file", "inspect relevant saved bytes" })
        {
            AssertContains(sharedPrerequisites, rule, "SKILL.md owns saved-output recovery before task selection");
            Assert(!workflow.Contains(rule, StringComparison.Ordinal),
                "assessment-only workflow must not duplicate shared saved-output recovery");
        }

        var ownership = new (string Owner, string[] Rules)[]
        {
            ("artifact-acquisition.md", ["package inspect", "source capture-archive", "actual library project or solution",
                "does not establish complete project/import closure", "style/theme assets"]),
            ("input-candidates.md", ["inputs discover", "inputs confirm", "inputs validate",
                "evidence draft-add", "assessment export-identity", "evidence ledger-build",
                "evidence ledger-validate", "evidence bundle", "explicitly `inputs confirm`", "EV1"]),
            ("assessment-workflow.md", ["assessment init --kind component", "assessment init --kind package",
                "assessment canonicalize", "assessment validate", "report render", "report verify",
                "Only assessment schema 2 is accepted", "structural validation", "Missing supplied probe results",
                "blanket not-tested template", "Low record count alone", "timebox",
                "Earlier row decisions are provisional", "recompute affected statuses",
                "no component selection or component-specific source closure"]),
            ("area-conditional-families.md", ["`AI-06` applies only to a **new** AI skill",
                "before merge", "known-existing", "newness is missing", "draft status unassigned"]),
            ("overlay-ai-skill.md", ["`AI-06` applies only to a new AI skill",
                "RAI review before merge", "missing newness", "draft status unassigned"]),
            ("report-contract.md", ["assessment init --kind component", "assessment revise",
                "Source validation manifest SHA-256:", "explicit", "decision-guidance.md"]),
            ("library-assessment.md", ["inventory discover", "inventory confirm", "inventory status",
                "library reconcile", "library validate", "library index", "current-generation.json",
                "unsupported host: full-library assessment requires isolated workers"]),
            ("blinded-comparison.md", ["comparison inputs-freeze", "comparison inputs-validate",
                "coverage surface", "before reading evidence"]),
            ("status-boundaries.md", ["public-absence-v1", "observed static-SSR behavior",
                "`RepositoryUrl` is not `ProjectUrl`", "Absence of a feature claim"]),
            ("area-ci-release.md", ["actual release job graph", "privileged", "path equality and in-place ordering"]),
            ("area-blazor-runtime.md", ["every claimed render mode", "dynamic_child_lifecycle.applicability", "`source-proof-v1`",
                "public `[Parameter]`", "event handlers, callbacks, conditional branches",
                "not runtime proof", "delivered input event", "supported-context basis", "net10.0"]),
            ("area-trim-performance.md", ["mutable", "applicable rerender risk", "Without descendant render counts",
                "no repeated identity surface", "PERF-06", "profiling every component by default",
                "Do not demand a vendor budget"]),
            ("offline-release-facts.md", ["never authenticated", "RDF equivalence was not assessed",
                "not RDF equivalence or", "not-comparable", "all ten required", "64 MiB",
                "pre-output confirmed manifest", "separate explicit", "result-basename"]),
            ("scoped-component-profile.md", ["all 52", "does not start a package assessment",
                "may be optionally bound", "do not discover or infer", "supersession ancestors",
                "not a self-contained", "does not export raw inputs", "exact canonical copies",
                "immutable revisions", "no-overwrite protection"])
        };
        foreach (var (owner, rules) in ownership)
        {
            var text = File.ReadAllText(Path.Combine(referencesRoot, owner));
            foreach (var rule in rules)
            {
                AssertContains(text, rule, $"{owner} owns '{rule}'");
            }
        }
    }

    private static void AssertGuidanceClarity(string skill, string referencesRoot)
    {
        var status = File.ReadAllText(Path.Combine(referencesRoot, "status-boundaries.md"));
        var targeted = File.ReadAllText(Path.Combine(referencesRoot, "targeted-profiles.md"));
        AssertSavedOutputBoundaries(skill);
        AssertRecordedFactBoundaries(status);
        AssertWorksheetFinalization(targeted);

        // These in-memory controls test documentation guards, not evidence adjudication.
        AssertGuidanceGuardRejects(() => AssertSavedOutputBoundaries(skill.Replace(
            "do not expand permissions", "expand permissions", StringComparison.Ordinal)),
            "saved-output guard rejects permission expansion");
        AssertGuidanceGuardRejects(() => AssertSavedOutputBoundaries(skill.Replace(
            "forbids alternative access", "allows alternative access", StringComparison.Ordinal)),
            "saved-output guard rejects content-exclusion workarounds");
        var unknownTiming = status.Split('\n').Single(line =>
            line.StartsWith('|') && line.Contains("availability time is unknown", StringComparison.Ordinal));
        AssertGuidanceGuardRejects(() => AssertRecordedFactBoundaries(status.Replace(unknownTiming,
            unknownTiming.Replace("| `owner evidence required` |", "| `gap` |", StringComparison.Ordinal),
            StringComparison.Ordinal)), "timing guard rejects treating an unknown time as a proved late fix");
        var lateFix = status.Split('\n').Single(line =>
            line.StartsWith('|') && line.Contains("first fix became available after disclosure", StringComparison.Ordinal));
        AssertGuidanceGuardRejects(() => AssertRecordedFactBoundaries(status.Replace(lateFix,
            lateFix.Replace("| `gap` |", "| `owner evidence required` |", StringComparison.Ordinal),
            StringComparison.Ordinal)), "timing guard rejects softening a directly proved late fix");
        AssertGuidanceGuardRejects(() => AssertRecordedFactBoundaries(status.Replace(
            "Interpret `null` only according to declared semantics.", "", StringComparison.Ordinal)),
            "null guard rejects missing declared-semantics guidance");
        AssertGuidanceGuardRejects(() => AssertWorksheetFinalization(targeted.Replace(
            "do not normalize status", "normalize status", StringComparison.Ordinal)),
            "worksheet guard rejects automatic synonym normalization");
        AssertGuidanceGuardRejects(() => AssertWorksheetFinalization(targeted.Replace(
            "not canonical", "require canonical", StringComparison.Ordinal)),
            "worksheet guard rejects imposing the canonical assessment schema");
    }

    private static void AssertSavedOutputBoundaries(string skill)
    {
        var recovery = Regex.Match(skill,
            @"(?ms)^## Saved-output evidence recovery\r?\n.*?(?=^## |\z)").Value;
        AssertBefore(skill, "## Saved-output evidence recovery", "## Select the task and next reads",
            "saved-output prerequisites apply before worksheet and assessment route selection");
        foreach (var rule in new[]
        {
            "evidence acquisition under the selected route",
            "inspect relevant saved bytes using permitted targeted searches or ranges",
            "before concluding the evidence is missing",
            "Respect access denials and existing authorization/isolation requirements",
            "do not expand permissions or retry denied access",
            "Already-authorized alternative sources may establish the facts",
            "identify that acquisition route",
            "do not describe it as saved-file recovery",
            "Organizational content exclusion",
            "forbids alternative access to the excluded content",
            "does not require reacquisition for advice-only or reader-only rendering",
            "or override the selected route's prerequisites"
        })
        {
            AssertContains(recovery, rule, "shared saved-output acquisition boundary");
        }
    }

    private static void AssertRecordedFactBoundaries(string status)
    {
        var examples = Regex.Match(status,
            @"(?ms)^## Paired boundary examples\r?\n.*?(?=^## |\z)").Value;
        var unknownTiming = examples.Split('\n').Single(line =>
            line.StartsWith('|') && line.Contains("availability time is unknown", StringComparison.Ordinal));
        foreach (var rule in new[] { "first fix occurred", "owner-held", "| `owner evidence required` |",
                     "Retain the occurrence", "request the missing timing", "not evidence that a fix occurred" })
        {
            AssertContains(unknownTiming, rule, "recorded occurrence with unknown owner-held availability time");
        }
        var lateFix = examples.Split('\n').Single(line =>
            line.StartsWith('|') && line.Contains("first fix became available after disclosure", StringComparison.Ordinal));
        foreach (var rule in new[] { "| `gap` |", "fix-before-disclosure requirement",
                     "other missing facts do not soften the conflict" })
        {
            AssertContains(lateFix, rule, "directly established late availability remains a gap");
        }
        foreach (var rule in new[] { "Interpret `null` only according to declared semantics",
                     "Unspecified or contradictory null meanings", "prove neither absence nor compliant chronology",
                     "do not map every null to `owner evidence required`", "or weaken an independently proved conflict" })
        {
            AssertContains(examples, rule, "declared null semantics preserve uncertainty and direct conflicts");
        }
        var decisionOrder = Regex.Match(status,
            @"(?ms)^## Decision order\r?\n.*?(?=^## |\z)").Value;
        AssertContains(decisionOrder, "Do not retreat to uncertainty after one required conjunct is directly observed missing",
            "timing and null examples preserve the failed-conjunct decision rule");
    }

    private static void AssertWorksheetFinalization(string targeted)
    {
        var finalization = Regex.Match(targeted,
            @"(?ms)^Before finalizing a worksheet,.*?(?=\r?\n\r?\n|\z)").Value;
        foreach (var rule in new[] { "exact declared status labels", "meanings/codes", "nullable fields",
                     "allowed prose fields or accompanying text", "do not normalize status synonyms or invent null meanings",
                     "validation appropriate to that worksheet", "not canonical assessment validation",
                     "independent worksheet schema", "contract ambiguities rather than guessing undeclared rules" })
        {
            AssertContains(finalization, rule, "worksheet finalization owns its independent declared contract");
        }
        AssertContains(targeted, "Unselected rows are not reverified",
            "worksheet finalization does not expand targeted corrections");
    }

    private static void AssertWorksheetPromptContracts(string pluginRoot)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(pluginRoot, "..", ".."));
        var evalRoot = Path.Combine(repositoryRoot, "tests", "dotnet-blazor", "blazor-component-readiness");
        var fixtureRoot = Path.Combine(evalRoot, "fixtures", "terra-residuals");
        var eval = File.ReadAllText(Path.Combine(evalRoot, "eval.yaml")).ReplaceLineEndings("\n");
        using var release = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtureRoot, "release-execution", "records.json")));
        using var ai = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtureRoot, "promoted-ai", "source-manifest.json")));
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        CollectWorksheetIdentifiers(release.RootElement, identifiers);
        CollectWorksheetIdentifiers(ai.RootElement, identifiers);
        var recordId = release.RootElement.GetProperty("records")[0].GetProperty("id").GetString()!;

        var contracts = new[]
        {
            (Name: "Credit bounded release execution records",
                Fixture: "release-execution", Input: "records.json", Output: "release-decisions.json",
                Command: "verify-release-records", Success: "VALID release execution bounded facts and limitations",
                Clauses: new[]
                {
                    "`status` is non-null and is exactly `verified` or `not tested`",
                    "Retain the supported `scope` code and qualifying `evidence_ids` even if the requested execution fact remains unresolved",
                    "`scope` is JSON null only when there is no qualifying support",
                    "`remaining_fact` holds only the listed missing-execution code for an unresolved execution fact, otherwise JSON null",
                    "Publication questions have no missing-execution code, even when not verified",
                    "Keep broader caveats and explanations in the accompanying prose, not in code fields"
                }),
            (Name: "Apply AI requirements from promoted source deliverables",
                Fixture: "promoted-ai", Input: "source-manifest.json", Output: "ai-decisions.json",
                Command: "verify-ai-applicability", Success: "VALID AI applicability promotion newness and review timing",
                Clauses: new[]
                {
                    "`family_applicability` is exactly `applicable` or `not applicable`",
                    "Assigned row `status` values are exactly `verified`, `gap`, `owner evidence required`, or `not applicable`",
                    "The only unassigned row status is JSON null for `AI-06` when newness is unresolved",
                    "Keep the listed missing-fact codes in `missing_fact` and rationale codes in `rationale_code`",
                    "unused code fields are JSON null",
                    "Each row's `evidence_ids` retains its applicability basis and its own supplied fact/context where required for that row, not a fixed citation count",
                    "Do not fabricate absent context",
                    "Keep explanations in the accompanying prose, not in code fields"
                })
        };
        foreach (var contract in contracts)
        {
            // fixture-tool.py remains co-staged/participant-visible; these guards do not prove
            // runtime isolation, exposure, or semantic nonleakage.
            var wiring = $$"""
                environment:
                  files:
                    - src: fixtures/terra-residuals/{{contract.Fixture}}
                      dest: fixture
                    - src: fixtures/fixture-tool.py
                      dest: fixture/fixture-tool.py
                graders:
                  - type: file-exists
                    config:
                      path: out/{{contract.Output}}
                  - type: run-command
                    config:
                      command: python3 fixture/fixture-tool.py {{contract.Command}} --fixture fixture/{{contract.Input}} --input out/{{contract.Output}}
                      expected_exit_code: 0
                      stdout_matches: {{contract.Success}}
                  - type: prompt
                  - type: exit-success
                """;
            var expectedWiring = string.Join("\n", wiring.Split('\n').Select(line => "    " + line));
            void Check(string candidate)
            {
                var selected = SelectWorksheetStimulus(candidate, contract.Name);
                var protocol = ReadWorksheetProtocol(selected);
                // These are declaration-presence and identified-answer filters, not prose adjudication.
                foreach (var clause in contract.Clauses.Concat(new[]
                {
                    "Include exactly the qualifying required evidence IDs",
                    "citation order may vary, but duplicate or extraneous IDs are invalid",
                    "For allowed nulls, omission is equivalent to JSON null",
                    "not the string \"null\" or prose"
                }))
                {
                    AssertContains(protocol, clause, $"{contract.Name} prompt protocol");
                }
                AssertWorksheetAnswerFilter(protocol, identifiers);
                var actualWiring = Regex.Match(selected,
                    @"(?ms)^    environment:\n.*?(?=^    rubric:|\z)").Value.TrimEnd('\n');
                Assert(actualWiring == expectedWiring,
                    $"{contract.Name} must retain its exact declared staging mappings and graders");
            }

            Check(eval);
            var stimulus = SelectWorksheetStimulus(eval, contract.Name);
            var declaration = ReadWorksheetProtocol(stimulus);
            var removed = stimulus.Replace(declaration, "", StringComparison.Ordinal);
            AssertGuidanceGuardRejects(() => Check(eval.Replace(stimulus, removed, StringComparison.Ordinal)),
                $"{contract.Name} rejects a removed prompt declaration");
            var rubricDeclaration = string.Concat(declaration.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => "  " + line + "\n"));
            var moved = removed.Replace("    rubric:\n", "    rubric:\n      - |\n" + rubricDeclaration,
                StringComparison.Ordinal);
            AssertGuidanceGuardRejects(() => Check(eval.Replace(stimulus, moved, StringComparison.Ordinal)),
                $"{contract.Name} rejects a declaration moved to the rubric");
            foreach (var payload in new[] { recordId, """{"rows":{"AI-06":{"status":"verified"}}}""" })
            {
                var injected = stimulus.Replace("      Worksheet output protocol:\n",
                    "      Worksheet output protocol:\n      " + payload + "\n", StringComparison.Ordinal);
                AssertGuidanceGuardRejects(() => Check(eval.Replace(stimulus, injected, StringComparison.Ordinal)),
                    $"{contract.Name} rejects identified answers in the new declaration");
            }
            AssertGuidanceGuardRejects(() => Check(eval.Replace(stimulus, "", StringComparison.Ordinal)),
                $"{contract.Name} rejects a missing stimulus");
            AssertGuidanceGuardRejects(() => Check(eval + "\n" + stimulus),
                $"{contract.Name} rejects an ambiguous stimulus identity");
        }
    }

    private static string SelectWorksheetStimulus(string eval, string name)
    {
        // Bounded to the existing block-scalar/indentation subset; YAML validity is checked separately.
        var matches = Regex.Matches(eval,
                @"(?ms)^  - name: (?<name>[^\n]+)\n.*?(?=^  - name: |\z)")
            .Where(match => match.Groups["name"].Value == name).ToArray();
        Assert(matches.Length == 1, $"expected exactly one worksheet stimulus named '{name}'");

        return matches[0].Value;
    }

    private static string ReadWorksheetProtocol(string stimulus)
    {
        var prompts = Regex.Matches(stimulus, @"(?m)^    prompt: \|\n(?<body>(?:      [^\n]*\n|\n)+)");
        Assert(prompts.Count == 1, "worksheet stimulus must have exactly one literal prompt block");
        var prompt = prompts[0].Groups["body"].Value;
        var declarations = Regex.Matches(prompt,
            @"(?ms)^      Worksheet output protocol:\n.*?^      End worksheet output protocol\.\n");
        Assert(declarations.Count == 1 && Regex.Matches(prompt,
                @"(?m)^      (?:Worksheet output protocol:|End worksheet output protocol\.)$").Count == 2,
            "worksheet output protocol must occur exactly once inside the selected prompt");

        return declarations[0].Value;
    }

    private static void CollectWorksheetIdentifiers(JsonElement element, HashSet<string> identifiers)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if ((property.Name is "id" or "evidence_id") && property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString()!;
                    if (!Regex.IsMatch(value, @"^[A-Z]+-\d{2}$"))
                    {
                        identifiers.Add(value);
                    }
                }
                CollectWorksheetIdentifiers(property.Value, identifiers);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectWorksheetIdentifiers(item, identifiers);
            }
        }
    }

    private static void AssertWorksheetAnswerFilter(string protocol, HashSet<string> identifiers)
    {
        foreach (var identifier in identifiers)
        {
            Assert(!Regex.IsMatch(protocol, $@"(?<![\w.-]){Regex.Escape(identifier)}(?![\w.-])",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                $"new worksheet protocol contains fixture identifier '{identifier}'");
        }
        Assert(!Regex.IsMatch(protocol,
                """(?i)"(?:status|family_applicability|scope|remaining_fact|missing_fact|rationale_code|evidence_ids)"\s*:\s*(?:"[^"\r\n]*"|null|\[[^\]\r\n]*\])""") &&
            !Regex.IsMatch(protocol,
                """(?im)^\s*(?:-\s*)?[`"']?AI-\d{2}[`"']?\s*(?::|=>|=|->|\|)\s*[`"']?(?:verified|gap|owner evidence required|not tested|not applicable|null)\b"""),
            "new worksheet protocol contains a recognizable canned decision payload");
    }

    private static void AssertGuidanceGuardRejects(Action action, string label)
    {
        var rejected = false;
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        Assert(rejected, label);
    }

    private static void AssertRubricIsSoleRequirementSource(string referencesRoot)
    {
        using var rubric = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(referencesRoot, "rubric.json")));
        var ids = rubric.RootElement.GetProperty("core").GetProperty("requirements")
            .EnumerateArray()
            .Select(row => row.GetProperty("id").GetString()!)
            .ToArray();
        Assert(ids.Length == 112 && ids.Distinct(StringComparer.Ordinal).Count() == 112,
            "rubric.json remains the sole current 112-ID source");
        Assert(Directory.GetFiles(referencesRoot, "rubric*.json").Select(Path.GetFileName)
                .SequenceEqual(["rubric.json"]) &&
            Directory.GetFiles(referencesRoot, "checklist*.md").Select(Path.GetFileName)
                .SequenceEqual(["checklist.md"]),
            "only current rubric and checklist resources are shipped");

        var checklist = File.ReadAllText(Path.Combine(referencesRoot, "checklist.md"));
        AssertContains(checklist, "Generated from rubric.json. Do not edit by hand.", "generated checklist marker");
        foreach (var path in Directory.GetFiles(referencesRoot, "*.md"))
        {
            if (Path.GetFileName(path) == "checklist.md")
            {
                continue;
            }

            var text = File.ReadAllText(path);
            var duplicatedDefinitions = ids.Count(id =>
                Regex.IsMatch(text, $@"(?m)^\s*-\s+\*\*{Regex.Escape(id)}\*\*\s+"));
            Assert(duplicatedDefinitions == 0, $"{Path.GetFileName(path)} must not redefine rubric rows");
        }
    }

    private static void AssertNoForbiddenCoupling(string pluginRoot)
    {
        var files = Directory.GetFiles(pluginRoot, "*.md", SearchOption.AllDirectories);
        foreach (var path in files)
        {
            var text = File.ReadAllText(path);
            foreach (var forbidden in new[]
            {
                "/Users/",
                ".copilot/",
                ".github/agents/blazor-component-readiness",
                "eng/skill-evals",
                "source activate",
                "--legacy-evidence",
                "--shared-row-projection",
                "--producer-validator"
            })
            {
                Assert(
                    !text.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    $"{Path.GetFileName(path)} contains forbidden coupling '{forbidden}'");
            }
        }
    }

    private static void AssertAgents(string pluginRoot)
    {
        var coordinator = File.ReadAllText(Path.Combine(
            pluginRoot, "agents", "blazor-component-readiness.agent.md"));
        var worker = File.ReadAllText(Path.Combine(
            pluginRoot, "agents", "blazor-component-readiness-worker.agent.md"));
        var referencesRoot = Path.Combine(pluginRoot, "skills", "blazor-component-readiness", "references");
        var execution = File.ReadAllText(Path.Combine(referencesRoot, "worker-execution.md"));
        var library = File.ReadAllText(Path.Combine(referencesRoot, "library-assessment.md"));
        var workflow = File.ReadAllText(Path.Combine(referencesRoot, "assessment-workflow.md"));

        AssertFrontmatter(coordinator, "18edba3c0611831e4ef19019d388023b63edf936462da4a32c77ac52db00a6fa", "coordinator");
        AssertFrontmatter(worker, "6a8fa2da87d8a37658fa5bbe8b55e7a9a8a7df6fe00cc46110c8bba8b08fa3c3", "worker");
        foreach (var persona in new[] { coordinator, worker })
        {
            foreach (var owner in new[] { "assessment-workflow.md", "scoped-component-profile.md", "blinded-comparison.md" })
            {
                AssertContains(persona, $"../skills/blazor-component-readiness/references/{owner}",
                    $"persona must route to {owner}");
            }
            AssertBefore(persona, "permissions or content exclusion",
                persona == coordinator ? "## Route before acting" : "## Execute the selected unit route",
                "trusted-root denial must precede persona execution");
        }

        AssertContains(coordinator, "user-invocable: true", "coordinator invocation");
        AssertContains(coordinator, "disable-model-invocation: true", "coordinator ambient routing");
        AssertContains(coordinator, "ask_user", "confirmation tool");
        AssertContains(
            coordinator,
            "Inspect every supplied local artifact before asking for missing information",
            "local discovery before clarification");
        AssertContains(
            coordinator,
            "explicitly requests separate execution",
            "explicit single-unit worker override");
        foreach (var tool in new[]
        {
            "\"skill\"", "\"read\"", "\"search\"", "\"edit\"", "\"execute\"",
            "\"web_search\"", "\"web_fetch\"", "\"Skill\"", "\"Read\"", "\"Glob\"",
            "\"Grep\"", "\"Edit\"", "\"Write\"", "\"Bash\"", "\"read_file\"", "\"replace\"",
            "\"write_file\"", "\"glob\"", "\"grep_search\"", "\"run_shell_command\""
        })
        {
            AssertContains(coordinator, tool, $"coordinator tool {tool}");
        }

        Assert(!coordinator.Contains("\"agent\"", StringComparison.Ordinal), "coordinator must not expose nested agent dispatch");
        Assert(!coordinator.Contains("\"Task\"", StringComparison.Ordinal), "coordinator must not expose nested task dispatch");
        AssertContains(coordinator, "Never invoke the worker through a nested agent/task tool.", "nested worker prohibition");
        AssertContains(coordinator, "package-only", "coordinator package-only mode");
        AssertContains(coordinator, "no component worker", "coordinator package-only worker boundary");
        AssertContains(coordinator, "assessment init --kind package", "code-owned package assessment kind");
        AssertContains(coordinator, "do not require inventory, workers, or an index", "package-only completion boundary");
        AssertContains(coordinator, "absolute trusted plugin root", "coordinator trusted plugin context");
        AssertContains(coordinator, "explicit trusted plugin root", "coordinator explicit source context");
        AssertContains(coordinator, "exact source path", "coordinator source identity receipt");
        AssertContains(coordinator, "permissions or content exclusion", "coordinator denial boundary");
        AssertContains(coordinator, "references/worker-execution.md", "coordinator launch prerequisite");
        AssertContains(execution, "set inherited `PWD` to that same absolute path", "coordinator cwd/PWD launch contract owner");
        AssertContains(execution, "full argv", "coordinator native launch receipt owner");
        AssertContains(coordinator, "A validated revision is not proof that the requested investigation finished.", "investigation completion boundary");
        AssertContains(workflow, "do not accept a blanket not-tested template as a completed assessment", "placeholder handoff rejection owner");
        AssertContains(execution, "Pass absolute `--root`, `--revision` and `--output` paths", "absolute verification arguments owner");
        AssertContains(execution, "`--package-revision` only for a declared relationship", "verification does not infer package work");
        AssertContains(library, "exactly one top-level `blazor-component-readiness-worker` session", "top-level worker isolation owner");
        AssertContains(coordinator, "unsupported host: full-library assessment requires isolated workers", "coordinator unsupported-host stop");
        AssertContains(coordinator, "decision-guidance.md", "coordinator decision guidance");
        AssertContains(coordinator, "reconcile", "coordinator interruption recovery");

        AssertContains(worker, "user-invocable: false", "worker invocation");
        AssertContains(worker, "selected as the top-level agent", "top-level worker requirement");
        AssertContains(worker, "A package unit may have", "worker empty package input");
        AssertContains(worker, "activation_method=explicit-file", "worker explicit file activation receipt");
        AssertContains(worker, "Do not invoke an unqualified `blazor-component-readiness` skill by name", "worker source-first activation");
        AssertContains(worker, "absolute trusted plugin root", "worker trusted plugin context");
        AssertContains(worker, "permissions or content exclusion", "worker denial boundary");
        AssertContains(worker, "unsupported host: readiness worker requires a top-level writable session", "nested worker stop");
        Assert(Regex.IsMatch(worker, @"(?m)^tools:\s*\[(?!\s*\])"), "worker tools must be non-empty");
        AssertContains(worker, "exactly one coordinator-assigned", "worker single-unit scope");
        AssertContains(worker, "Do not discover or add components", "worker scope prohibition");
        AssertContains(worker, "every claimed render mode", "worker mode coverage");
        AssertContains(worker, "Missing supplied probe results are work to perform", "active evidence collection");
        AssertContains(worker, "shared assessment workflow", "worker producer and status prerequisites");
        AssertContains(worker, "only when the requested investigation and final verification are finished", "meaningful worker completion");
        AssertContains(worker, "Do not use a blanket not-tested assignment as the final assessment.", "blanket placeholder rejection");
        Assert(!NormalizeWhitespace(worker).Contains(
            "For a complete validated revision, return `blockers: []`", StringComparison.Ordinal),
            "structural validation alone must not authorize completed handoff");
        AssertContains(worker, "comparison inputs-validate", "blind worker input gate");
        AssertBefore(worker, "references/scoped-component-profile.md", "Accept only",
            "profile exception before worker binding acceptance");
        AssertContains(worker, "references/artifact-acquisition.md", "worker source closure prerequisite");
        AssertContains(worker, "references/area-blazor-runtime.md", "worker component proof prerequisite");
        AssertContains(worker, "selected area owners before evidence/status decisions", "worker classification owner prerequisites");
        AssertContains(worker, "unit_id:", "worker handoff unit");
        AssertContains(worker, "revision_path:", "worker handoff revision");
        AssertContains(worker, "report_path:", "worker handoff report");
        AssertContains(worker, "validation_manifest_path:", "worker handoff manifest");
        AssertContains(worker, "validation_manifest_sha256:", "worker handoff digest");
        AssertContains(worker, "blockers:", "worker handoff blockers");
        AssertContains(worker, "Never modify reviewed source, inventory, sibling artifacts, or remotes.", "worker mutation boundary");
    }

    private static void AssertContracts(string referencesRoot)
    {
        var feedback = File.ReadAllText(Path.Combine(referencesRoot, "feedback-contract.md"));
        AssertContains(feedback, "# Assessment feedback", "feedback heading");
        AssertContains(feedback, "| Requirement IDs | Feedback |", "feedback header");
        AssertContains(feedback, "raw feedback cell payload is rendered verbatim", "feedback preservation");
        AssertContains(feedback, "never creates, rewrites, normalizes, or deletes it", "feedback ownership");

        var report = File.ReadAllText(Path.Combine(referencesRoot, "report-contract.md"));
        AssertContains(report, "| ID | Scope | Area | Requirement | Status | Factual observation | Evidence and provenance | Owner action | Assessment follow-up / rationale |", "report table contract");
        AssertContains(report, "Source validation manifest SHA-256:", "guidance digest header");
        AssertContains(report, "outside `revisions/`", "guidance placement");
        AssertContains(report, "unbound, regenerable", "guidance binding boundary");
        AssertContains(report, "Replace it only after another explicit guidance request.", "guidance replacement");
        AssertContains(report, "Only assessment schema 2 and rubric 2.1.0 are accepted", "current-only assessment policy");
        AssertContains(report, "identities are rejected, not migrated, reinterpreted or rendered",
            "unsupported assessments have no compatibility execution path");
        AssertContains(report, "the canonical `overlays` array must remain empty", "empty persisted overlay provenance");
        AssertContains(report, "Unrelated schema-1 evidence, inventory, comparison and library-state formats",
            "unrelated schema versions remain independent");

        var status = File.ReadAllText(Path.Combine(referencesRoot, "status-boundaries.md"));
        AssertContains(status, "direct-evidence-satisfies", "verified decision boundary");
        AssertContains(status, "direct-evidence-conflicts", "gap decision boundary");
        AssertContains(status, "owner-held-evidence-only", "owner decision boundary");
        AssertContains(status, "applicable-evidence-not-obtained", "not-tested decision boundary");
        AssertContains(status, "confirmed-not-applicable", "not-applicable decision boundary");
        AssertContains(status, "Only assessment schema version 2 is accepted", "current assessment schema");
        AssertContains(status, "Schema-1 assessments are rejected, not migrated", "unsupported assessment rejection");
        AssertContains(status, "only one directed-gap protocol family", "directed-gap exclusivity");
        AssertContains(status, "A keyed reorder behavior probe passes", "mechanism/outcome boundary example");
        AssertContains(status, "confirmed complete public-policy corpus", "public-absence boundary example");

        var ciRelease = File.ReadAllText(Path.Combine(referencesRoot, "area-ci-release.md"));
        AssertContains(ciRelease, "actual release job graph", "CI-07 job graph rule");
        AssertContains(ciRelease, "path equality and in-place ordering are not an immutable handoff", "CI-08 path identity rule");

        var runtime = File.ReadAllText(Path.Combine(referencesRoot, "area-blazor-runtime.md"));
        AssertContains(runtime, "event handlers, callbacks, conditional branches", "BEQ-09 assignment coverage");
        foreach (var rule in new[]
        {
            "When dynamic-child lifecycle is explicitly `not-applicable`",
            "`BEQ-12` callback or `BEQ-15` cleanup source `gap` without a lifecycle companion",
            "missing, unknown or malformed applicability",
            "same requirement-specific proof kinds and exact confirmed source path/digest",
            "typed, gap-only evidence, not a free-text workaround",
            "For lifecycle-required components, the companion, mapped outcomes and contradiction rules"
        })
        {
            AssertContains(runtime, rule, "shared non-dynamic source-proof guidance");
        }
        Assert(!runtime.Contains("Keep non-dynamic callback/cleanup source proof fail-closed", StringComparison.Ordinal),
            "ordinary component source conflicts are not blocked by the retired dynamic-only instruction");
        var renderModeDecision = Regex.Match(
            runtime, @"(?ms)^For documentation requirements,.*?(?=^For `BEQ-05`)").Value;
        AssertContains(renderModeDecision,
            "For current `2.1.0` `BEQ-03`, require supported-mode documentation **and** a clear " +
            "compile-time **or** runtime error for an applicable unsupported-mode diagnostic.",
            "current render-mode documentation-and-error conjunction");
        AssertContains(renderModeDecision, "Documentation alone does not discharge the error obligation",
            "render-mode documentation-only safeguard");
        AssertContains(renderModeDecision, "an error alone does not discharge documentation",
            "render-mode error-only safeguard");
        AssertContains(renderModeDecision, "A directly established missing required conjunct is a `gap`.",
            "render-mode direct missing-conjunct gap");
        AssertContains(renderModeDecision,
            "A blocked or unperformed applicable diagnostic with no direct conflict stays `not tested`, " +
            "not an inferred pass or absence gap.",
            "render-mode blocked diagnostic boundary");
        AssertContains(renderModeDecision,
            "Clause 4.2 retains the alternative of all modes working correctly versus a documented supported set " +
            "with clear errors elsewhere.",
            "render-mode coverage alternatives");
        AssertContains(renderModeDecision,
            "Do not invent an unsupported configuration or require support for an unsupported mode.",
            "render-mode unsupported configuration boundary");
        AssertContains(renderModeDecision,
            "Valid prerendering for supported interactive modes is not an unsupported-mode diagnostic.",
            "render-mode supported prerendering boundary");
        AssertContains(renderModeDecision,
            "Keep `BEQ-02` and `BEQ-04` independent; prerendering must not throw.",
            "render-mode independent documentation and prerendering rows");
        AssertContains(renderModeDecision,
            "For genuinely disjunctive requirements, do not call a gap until every remaining applicable " +
            "alternative is directly contradicted.",
            "render-mode decision preserves genuine disjunctions");

        var performance = File.ReadAllText(Path.Combine(referencesRoot, "area-trim-performance.md"));
        AssertContains(performance, "no repeated identity surface", "PERF-02 applicability boundary");
        AssertContains(performance, "missing `IsFixed` establishes an applicable rerender risk", "PERF-05 source risk boundary");
        AssertContains(performance, "Without descendant render counts", "PERF-05 measurement requirement");
        AssertContains(performance, "Current `TA-05` applicability is not waived by an absent vendor AOT claim",
            "AOT applicability is not claim-selected");
        AssertContains(performance, "only after the execution prerequisite is satisfied",
            "AOT applicability does not authorize execution");
        AssertContains(performance, "work that is not performed remains `not tested` with the actual blocker",
            "applicable unperformed AOT is not automatic non-applicability");

        var provenance = File.ReadAllText(Path.Combine(referencesRoot, "area-provenance-integrity.md"));
        AssertContains(provenance, "For `PI-06`, `PI-07`, `PI-10`, and `PI-11`, publication is the required surface", "SBOM/provenance absence boundary");
        AssertContains(provenance, "the absence is direct evidence, not an unrun probe", "SBOM/provenance direct evidence");
        AssertContains(provenance, "For `PI-08` and `PI-09`, missing SBOM bytes alone are insufficient", "asset/notice representation boundary");

        var comparison = File.ReadAllText(Path.Combine(referencesRoot, "blinded-comparison.md"));
        AssertContains(comparison, "Schema version 2", "comparison schema version");
        AssertContains(comparison, "browser-interop-and-style-assets", "browser interop coverage");
        AssertContains(comparison, "performance-measurements", "performance coverage");
        AssertContains(comparison, "`browser-interop-source`", "typed browser interop role");
        AssertContains(comparison, "`component-source-closure`", "typed component source role");
        AssertContains(comparison, "`origin_kind`", "coverage provenance origin");
        AssertContains(comparison, "Schema-version-1 confirmed comparison manifests remain canonical", "legacy comparison gate");

        var library = File.ReadAllText(Path.Combine(referencesRoot, "library-assessment.md"));
        AssertContains(library, "append-only state receipt", "library state receipts");
        AssertContains(library, "`pending`, `active`, `completed`, `blocked`, or `incomplete`", "library states");
        AssertContains(library, "one top-level writable worker session per unit", "library isolation");
        AssertContains(library, "coordinator-managed split request requires separate unit execution", "split execution isolation");
        AssertContains(library, "factual status counts", "factual library index");
        AssertContains(library, "current-generation.json", "generation index publication");
        AssertContains(library, "compatibility caches", "index cache recovery");

        var workerExecution = File.ReadAllText(Path.Combine(referencesRoot, "worker-execution.md"));
        AssertContains(workerExecution, "Nested custom agents are text-only", "nested agent boundary");
        AssertContains(workerExecution, "complete", "complete file activation");
        AssertContains(workerExecution, "Record the activation method", "activation receipt");
        AssertContains(workerExecution, "cd \"$UNIT\"", "launch directory setup");
        AssertContains(workerExecution, "export PWD=\"$UNIT\" TMPDIR=\"$UNIT/tmp\"", "unit-local PWD/TMPDIR setup");
        AssertContains(workerExecution, "--effort <owner-approved-effort>", "explicit effort");
        AssertContains(workerExecution, "set inherited `PWD` to that same absolute path", "cwd/PWD alignment");
        AssertContains(workerExecution, "Invoke `copilot` from `PATH`", "PATH launcher");
        AssertContains(workerExecution, "--session-id <new-session-uuid>", "native session identity");
        AssertContains(workerExecution, "--log-dir \"$UNIT/logs\"", "native log directory");
        AssertContains(workerExecution, "tool-call IDs/results", "raw tool receipt");
        AssertContains(workerExecution, "--agent <plugin-name>:blazor-component-readiness-worker", "qualified CLI worker");
        AssertContains(workerExecution, "prepare-worker-launch.sh", "validated worker launch helper");
        AssertContains(workerExecution, "use both `--plugin-dir \"$CLI_PLUGIN_DIR\"` and the generated `WORKER_PROMPT_FILE`", "shared plugin root handoff");
        AssertContains(workerExecution, "set -euo pipefail", "preparation failure stops launch");
        AssertContains(workerExecution, "-p \"$(cat \"$WORKER_PROMPT_FILE\")\"", "generated prompt used in launch");
        Assert(!workerExecution.Contains("-p <bounded-worker-prompt>", StringComparison.Ordinal), "no independent prompt launch recipe");
        AssertContains(
            File.ReadAllText(Path.Combine(referencesRoot, "..", "..", "..", "agents", "blazor-component-readiness.agent.md")),
            "prepare-worker-launch.sh",
            "coordinator uses shipped launch contract");
        AssertContains(workerExecution, "Never add `--allow-all-paths`", "worker path sandbox");
        AssertContains(workerExecution, "Recomputes the validation-manifest SHA-256", "worker digest acceptance");
        AssertContains(workerExecution, "resolve returned paths to absolute paths", "cross-root verification");
        AssertContains(workerExecution, "accessible requested probes were never attempted is unfinished work", "unattempted coverage rejection");
    }

    private static void AssertWorkerLaunchContract(string pluginRoot)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var helper = Path.Combine(
            pluginRoot,
            "skills",
            "blazor-component-readiness",
            "scripts",
            "prepare-worker-launch.sh");
        Assert(File.Exists(helper), "worker launch helper exists");

        var testRoot = Path.Combine(
            Directory.GetParent(pluginRoot)?.FullName
                ?? throw new InvalidOperationException("plugin parent is required"),
            $"readiness worker launch {Guid.NewGuid():N}");
        var unitRoot = Path.Combine(testRoot, "unit");
        var relocatedPlugin = Path.Combine(unitRoot, "tooling", "relocated-plugin");
        var outsidePlugin = Path.Combine(testRoot, "outside-plugin");
        var promptFile = Path.Combine(unitRoot, "prompt.txt");
        var contractFile = Path.Combine(unitRoot, "launch.env");

        try
        {
            Directory.CreateDirectory(unitRoot);
            CopyDirectory(pluginRoot, relocatedPlugin);
            CopyDirectory(pluginRoot, outsidePlugin);
            File.WriteAllText(promptFile, "Run the assigned readiness worker.");

            var valid = RunHelper(
                helper,
                "--unit", unitRoot,
                "--plugin-dir", "tooling/relocated-plugin",
                "--prompt-file", promptFile,
                "--output", contractFile);
            Assert(valid.ExitCode == 0, $"valid worker launch contract: {valid.StandardError}");

            var expectedRoot = Path.GetFullPath(relocatedPlugin);
            var sourced = SourceContract(contractFile);
            Assert(sourced[0] == expectedRoot, "worker plugin root is absolute");
            Assert(sourced[1] == expectedRoot, "CLI and worker roots are identical");
            var generatedPrompt = File.ReadAllText(sourced[2]);
            var expectedSkill = Path.Combine(
                expectedRoot,
                "skills",
                "blazor-component-readiness",
                "SKILL.md");
            Assert(
                generatedPrompt.Contains($"Authoritative bundled skill source: {expectedSkill}", StringComparison.Ordinal),
                "worker prompt names exact staged skill source");
            Assert(
                generatedPrompt.Contains("load the complete authoritative bundled skill source above first", StringComparison.Ordinal),
                "worker prompt requires source-first activation");
            Assert(
                generatedPrompt.Contains("Do not invoke an unqualified registered skill by name", StringComparison.Ordinal),
                "worker prompt avoids ambiguous global lookup");
            Assert(
                !generatedPrompt.Contains("host-registered", StringComparison.OrdinalIgnoreCase) &&
                !generatedPrompt.Contains("Skill not found", StringComparison.OrdinalIgnoreCase),
                "worker prompt does not make explicit-root activation depend on lookup results");

            var missing = RunHelper(
                helper,
                "--unit", unitRoot,
                "--plugin-dir", "tooling/missing-plugin",
                "--prompt-file", promptFile,
                "--output", Path.Combine(unitRoot, "missing.env"));
            Assert(missing.ExitCode != 0 && missing.StandardError.Contains("does not exist", StringComparison.Ordinal), "missing plugin rejected before launch");

            var outside = RunHelper(
                helper,
                "--unit", unitRoot,
                "--plugin-dir", outsidePlugin,
                "--prompt-file", promptFile,
                "--output", Path.Combine(unitRoot, "outside.env"));
            Assert(outside.ExitCode != 0 && outside.StandardError.Contains("inside worker unit", StringComparison.Ordinal), "out-of-unit plugin rejected before launch");

            var outsideOutput = RunHelper(
                helper,
                "--unit", unitRoot,
                "--plugin-dir", Path.GetFullPath(relocatedPlugin),
                "--prompt-file", promptFile,
                "--output", Path.Combine(testRoot, "outside.env"));
            Assert(outsideOutput.ExitCode != 0 && outsideOutput.StandardError.Contains("inside worker unit", StringComparison.Ordinal), "out-of-unit output rejected before launch");

            var missingOutputDirectory = Path.Combine(testRoot, "outside-missing");
            var missingOutput = RunHelper(
                helper,
                "--unit", unitRoot,
                "--plugin-dir", relocatedPlugin,
                "--prompt-file", promptFile,
                "--output", Path.Combine(missingOutputDirectory, "launch.env"));
            Assert(missingOutput.ExitCode != 0, "missing output parent rejected");
            Assert(!Directory.Exists(missingOutputDirectory), "rejected output does not create outside directory");

            var originalPrompt = File.ReadAllText(promptFile);
            var collidingPrompt = RunHelper(
                helper,
                "--unit", unitRoot,
                "--plugin-dir", Path.GetFullPath(relocatedPlugin),
                "--prompt-file", promptFile,
                "--output", promptFile);
            Assert(collidingPrompt.ExitCode != 0 && collidingPrompt.StandardError.Contains("overwrite the worker prompt", StringComparison.Ordinal), "worker prompt is protected from overwrite");
            Assert(File.ReadAllText(promptFile) == originalPrompt, "rejected output leaves worker prompt unchanged");

            var generatedPromptPath = Path.Combine(unitRoot, "worker-prompt.txt");
            var collidingGeneratedPrompt = RunHelper(
                helper,
                "--unit", unitRoot,
                "--plugin-dir", Path.GetFullPath(relocatedPlugin),
                "--prompt-file", promptFile,
                "--output", generatedPromptPath);
            Assert(collidingGeneratedPrompt.ExitCode != 0 && collidingGeneratedPrompt.StandardError.Contains("generated worker prompt", StringComparison.Ordinal), "generated worker prompt is protected from overwrite");
            Assert(File.ReadAllText(promptFile) == originalPrompt, "rejected generated-prompt collision leaves input unchanged");

            File.WriteAllText(generatedPromptPath, originalPrompt);
            var generatedPromptBefore = File.ReadAllText(generatedPromptPath);
            var collidingInput = RunHelper(
                helper,
                "--unit", unitRoot,
                "--plugin-dir", relocatedPlugin,
                "--prompt-file", generatedPromptPath,
                "--output", contractFile);
            Assert(collidingInput.ExitCode != 0, "generated prompt cannot be reused as base input");
            Assert(File.ReadAllText(generatedPromptPath) == generatedPromptBefore, "rejected input collision leaves generated prompt unchanged");

            File.WriteAllText(promptFile, $"Trusted plugin root: {outsidePlugin}");
            var mismatched = RunHelper(
                helper,
                "--unit", unitRoot,
                "--plugin-dir", relocatedPlugin,
                "--prompt-file", promptFile,
                "--output", Path.Combine(unitRoot, "mismatched.env"));
            Assert(mismatched.ExitCode != 0 && mismatched.StandardError.Contains("independent plugin root", StringComparison.Ordinal), "absolute mismatched worker root rejected");
            Assert(!File.Exists(Path.Combine(unitRoot, "mismatched.env")), "mismatched handoff produces no contract");

            File.WriteAllText(promptFile, "Plugin root tooling/dotnet-blazor; use fallback if no skill.");
            var relative = RunHelper(
                helper,
                "--unit", unitRoot,
                "--plugin-dir", Path.GetFullPath(relocatedPlugin),
                "--prompt-file", promptFile,
                "--output", Path.Combine(unitRoot, "relative.env"));
            Assert(relative.ExitCode != 0 && relative.StandardError.Contains("independent plugin root", StringComparison.Ordinal), "relative worker root rejected");
            Assert(!File.Exists(Path.Combine(unitRoot, "relative.env")), "relative handoff produces no contract");

            File.WriteAllText(promptFile, originalPrompt);
            var stagedSkill = Path.Combine(relocatedPlugin, "skills", "blazor-component-readiness", "SKILL.md");
            File.Delete(stagedSkill);
            var missingSkill = RunHelper(
                helper,
                "--unit", unitRoot,
                "--plugin-dir", relocatedPlugin,
                "--prompt-file", promptFile,
                "--output", Path.Combine(unitRoot, "missing-skill.env"));
            Assert(missingSkill.ExitCode != 0 && missingSkill.StandardError.Contains("missing blazor-component-readiness SKILL.md", StringComparison.Ordinal), "missing bundled skill rejected");
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    private static void AssertReadinessLauncherExample(string skillRoot)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var skill = File.ReadAllText(Path.Combine(skillRoot, "SKILL.md")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var functionStart = skill.IndexOf("readiness()\n{", StringComparison.Ordinal);
        Assert(functionStart >= 0, "documented readiness function exists");
        var functionEnd = skill.IndexOf("\n}", functionStart, StringComparison.Ordinal);
        Assert(functionEnd > functionStart, "documented readiness function closes");
        var function = skill[functionStart..(functionEnd + 2)];
        var testRoot = Path.Combine(Path.GetTempPath(), $"readiness launcher example {Guid.NewGuid():N}");
        var outputRoot = Path.Combine(testRoot, "output root with spaces");
        var validatorRoot = Path.Combine(testRoot, "validator root with spaces");
        var validatorDirectory = Path.Combine(validatorRoot, "scripts", "validator");
        var stub = Path.Combine(validatorDirectory, "run-validator.sh");
        var script = Path.Combine(testRoot, "invoke readiness.sh");
        var result = Path.Combine(outputRoot, "result.txt");

        try
        {
            Directory.CreateDirectory(validatorDirectory);
            Directory.CreateDirectory(outputRoot);
            File.WriteAllText(
                stub,
                """
                #!/usr/bin/env bash
                set -euo pipefail
                printf 'temp=%s\nargc=%s\n' "$READINESS_TEMP" "$#" > "$RESULT_FILE"
                for argument in "$@"; do
                  printf '<%s>\n' "$argument" >> "$RESULT_FILE"
                done
                exit 7
                """);
            File.WriteAllText(
                script,
                $$"""
                set -euo pipefail
                SKILL_DIR="$1"
                OUTPUT="$2"
                export RESULT_FILE="$3"
                shift 3
                mkdir -p "$OUTPUT/.validator-cache"
                {{function}}
                readiness "$@"
                """);

            var invocation = RunHelper(
                script,
                validatorRoot,
                outputRoot,
                result,
                "arg with spaces",
                "literal $dollar and apostrophe '");
            Assert(invocation.ExitCode == 7, "readiness forwards nonzero exit code");
            Assert(invocation.StandardError == string.Empty, "readiness example has no shell error");
            var lines = File.ReadAllLines(result);
            Assert(
                Path.Combine(outputRoot, ".validator-cache") == lines[0]["temp=".Length..],
                "readiness forwards environment value");
            Assert(lines[1] == "argc=2", "readiness forwards argument count");
            Assert(lines[2] == "<arg with spaces>", "readiness preserves spaced argument");
            Assert(lines[3] == "<literal $dollar and apostrophe '>", "readiness preserves literal argument");
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    private static void AssertOptionalJqInventoryProjection(string pluginRoot)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var jqCheck = RunHelper("-c", "command -v jq");
        if (jqCheck.ExitCode == 1)
        {
            Console.WriteLine("Skipping optional jq inventory projection: jq is unavailable.");
            return;
        }

        Assert(jqCheck.ExitCode == 0, $"jq availability check failed: {jqCheck.StandardError}");
        var testRoot = Path.Combine(Path.GetTempPath(), $"jq inventory projection {Guid.NewGuid():N}");
        var inventory = Path.Combine(testRoot, "retained inventory with spaces.json");
        var listing = Path.Combine(testRoot, "disposable listing with spaces.tsv");
        var inventoryBytes = Encoding.UTF8.GetBytes(
            """{"entries":[{"id":7,"source_path":"src/with spaces/$file.razor"},{"id":42,"source_path":"tests/vendor's sample.razor"},{"id":105,"source_path":"styles/theme.css"}]}""");
        var documentedCommand = File.ReadLines(Path.Combine(
                pluginRoot,
                "skills",
                "blazor-component-readiness",
                "references",
                "artifact-acquisition.md"))
            .Single(line => line.TrimStart().StartsWith(
                "(set -C; jq -r '.entries[] | [.id, .source_path] | @tsv'",
                StringComparison.Ordinal))
            .Trim();
        var script = $"""
            set -euo pipefail
            INVENTORY="$1"
            LISTING="$2"
            {documentedCommand}
            """;

        try
        {
            Directory.CreateDirectory(testRoot);
            File.WriteAllBytes(inventory, inventoryBytes);
            var invocation = RunHelper("-c", script, "jq-inventory-projection", inventory, listing);
            Assert(invocation.ExitCode == 0, $"jq inventory projection failed: {invocation.StandardError}");
            Assert(File.ReadAllBytes(inventory).SequenceEqual(inventoryBytes), "jq projection preserves inventory bytes");
            Assert(
                File.ReadAllText(listing) ==
                "7\tsrc/with spaces/$file.razor\n42\ttests/vendor's sample.razor\n105\tstyles/theme.css\n",
                "jq projection preserves every ID/path pair");
            var collision = RunHelper("-c", script, "jq-inventory-projection", inventory, inventory);
            Assert(collision.ExitCode != 0, "jq projection refuses to overwrite its inventory");
            Assert(File.ReadAllBytes(inventory).SequenceEqual(inventoryBytes), "rejected projection preserves inventory bytes");
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    private static ProcessResult RunHelper(string helper, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("/bin/bash")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(helper);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("could not start worker launch helper");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return new ProcessResult(
            process.ExitCode,
            standardOutput.GetAwaiter().GetResult(),
            standardError.GetAwaiter().GetResult());
    }

    private static string[] SourceContract(string contract)
    {
        var startInfo = new ProcessStartInfo("/bin/bash")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(". \"$1\"; printf '%s\\n' \"$PLUGIN_ROOT\" \"$CLI_PLUGIN_DIR\" \"$WORKER_PROMPT_FILE\"");
        startInfo.ArgumentList.Add("source-contract");
        startInfo.ArgumentList.Add(contract);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("could not source worker launch contract");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var output = standardOutput.GetAwaiter().GetResult();
        var error = standardError.GetAwaiter().GetResult();
        Assert(process.ExitCode == 0, $"sourcing worker launch contract: {error}");
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private static void AssertContains(string value, string expected, string name) =>
        Assert(
            NormalizeWhitespace(value).Contains(NormalizeWhitespace(expected), StringComparison.Ordinal),
            $"{name} is missing '{expected}'");

    private static string NormalizeWhitespace(string value) =>
        Regex.Replace(value, @"\s+", " ").Trim();

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
