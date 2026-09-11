using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class WorkflowTests
{
    private static readonly string[] RequiredReferences =
    [
        "assessment-workflow.md",
        "offline-release-facts.md",
        "scoped-component-profile.md",
        "input-candidates.md",
        "partner-preview.md",
        "report-contract.md",
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
        AssertContains(skill, "121-ID operational crosswalk", "core count");
        AssertContains(skill, "60 `repository-wide` / 61 `component-specific`", "scope ownership");
        AssertContains(skill, "`source-available`, `closed-source`, or `unresolved`", "source availability");
        AssertContains(skill, "owner-supplied-internal-evidence", "owner evidence");
        AssertContains(skill, "owner-supplied-public-evidence", "public owner evidence");
        AssertOwnedRules(referencesRoot);
        AssertContains(skill, "Never run Git metadata commands inside an archive", "archive Git boundary");
        AssertContains(skill, "Package-only", "package-only mode");
        AssertContains(skill, "components: []", "empty package inventory");
        AssertContains(skill, "No component worker", "package-only worker boundary");
        AssertContains(skill, "Single component, unified", "unified mode");
        AssertContains(skill, "Single package, split", "split mode");
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
                @"assessment init --kind unified\b[^\r\n]*--component <component-id>"),
            "unified assessment init example requires the component identity");

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
        AssertWorkerLaunchContract(pluginRoot);
        AssertReadinessLauncherExample(skillRoot);
        AssertOptionalJqInventoryProjection(pluginRoot);
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
        var paths = new[] { skillPath }
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

    private static void AssertReadingRoutes(string skill, string referencesRoot)
    {
        var routes = new (string Name, string[] Owners)[]
        {
            ("scoped component", ["scoped-component-profile.md"]),
            ("Package-only", ["assessment-workflow.md"]),
            ("Single component, unified", ["assessment-workflow.md"]),
            ("Single package, split / ordinary bound component",
                ["assessment-workflow.md", "report-contract.md#ordinary-component-binding",
                 "library-assessment.md#split-coordination", "worker-execution.md"]),
            ("Full library", ["library-assessment.md", "worker-execution.md"]),
            ("Targeted follow-up / worksheet", ["targeted-profiles.md", "status-boundaries.md"]),
            ("Offline release facts / authorized identity-only handoff", ["offline-release-facts.md", "input-candidates.md"]),
            ("Existing reader, feedback or correction", ["report-contract.md", "partner-preview.md", "feedback-contract.md"]),
            ("Explicit blinded comparison", ["blinded-comparison.md", "worker-execution.md"])
        };
        foreach (var (name, owners) in routes)
        {
            var row = skill.Split('\n').Single(line => line.StartsWith($"| **{name}** |", StringComparison.Ordinal));
            foreach (var owner in owners)
            {
                AssertContains(row, $"(references/{owner})", $"explicit next-read owner for {name}");
            }
            if (name is "Package-only" or "Offline release facts / authorized identity-only handoff")
            {
                foreach (var forbidden in new[] { "library-assessment.md", "worker-execution.md",
                             "area-blazor-runtime.md", "area-accessibility.md" })
                {
                    Assert(!row.Contains($"(references/{forbidden})", StringComparison.Ordinal),
                        $"{name} must not require {forbidden}");
                }
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
            ("area-trim-performance.md", "artifact-acquisition.md#original-library-source-closure")
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
        AssertBefore(input, "(scoped-component-profile.md)", "assessment init --kind", "profile before producer initialization");
        var report = File.ReadAllText(Path.Combine(referencesRoot, "report-contract.md"));
        AssertBefore(report, "(scoped-component-profile.md)", "## Ordinary component binding", "profile before ordinary binding");
        var reader = File.ReadAllText(Path.Combine(referencesRoot, "partner-preview.md"));
        AssertBefore(reader, "(scoped-component-profile.md)", "reader render", "profile before reader generation");
        var feedback = File.ReadAllText(Path.Combine(referencesRoot, "feedback-contract.md"));
        AssertBefore(feedback, "(scoped-component-profile.md)", "Component feedback keys", "profile before ordinary feedback keys");
        var worker = File.ReadAllText(Path.Combine(referencesRoot, "worker-execution.md"));
        AssertBefore(worker, "(scoped-component-profile.md)", "1. Copy only", "profile before ordinary unit staging");
        AssertContains(report, "### Scoped component profile (V1)", "profile anchor preserved");
        AssertContains(File.ReadAllText(Path.Combine(referencesRoot, "artifact-acquisition.md")),
            "## Offline release facts", "old acquisition anchor preserved");
    }

    private static void AssertBefore(string text, string prerequisite, string action, string label)
    {
        var first = text.IndexOf(prerequisite, StringComparison.Ordinal);
        var next = text.IndexOf(action, StringComparison.Ordinal);
        Assert(first >= 0 && next > first, label);
    }

    private static void AssertOwnedRules(string referencesRoot)
    {
        var ownership = new (string Owner, string[] Rules)[]
        {
            ("artifact-acquisition.md", ["package inspect", "source capture-archive", "actual library project or solution",
                "does not establish complete project/import closure", "style/theme assets"]),
            ("input-candidates.md", ["inputs discover", "inputs confirm", "inputs validate",
                "evidence draft-add", "assessment export-identity", "evidence ledger-build",
                "evidence ledger-validate", "evidence bundle", "explicitly `inputs confirm`", "EV1"]),
            ("assessment-workflow.md", ["assessment init --kind unified", "assessment init --kind package",
                "assessment canonicalize", "assessment validate", "report render", "report verify",
                "legacy v1", "structural validation", "Missing supplied probe results",
                "blanket not-tested template", "Low record count alone", "timebox",
                "no component selection or component-specific source closure"]),
            ("report-contract.md", ["assessment init --kind component", "assessment revise",
                "Source validation manifest SHA-256:", "explicit", "decision-guidance.md"]),
            ("library-assessment.md", ["inventory discover", "inventory confirm", "inventory status",
                "library reconcile", "library validate", "library index", "current-generation.json",
                "unsupported host: full-library assessment requires isolated workers"]),
            ("blinded-comparison.md", ["comparison inputs-freeze", "comparison inputs-validate",
                "coverage surface", "before reading evidence"]),
            ("status-boundaries.md", ["public-absence-v1", "direct-failure-v1",
                "`RepositoryUrl` is not `ProjectUrl`", "Absence of a feature claim"]),
            ("area-ci-release.md", ["actual release job graph", "privileged", "path equality and in-place ordering"]),
            ("area-blazor-runtime.md", ["every claimed render mode", "dynamic_child_lifecycle.applicability", "`source-proof-v1`",
                "public `[Parameter]`", "event handlers, callbacks, conditional branches",
                "not runtime proof", "delivered input event", "supported-context basis", "net10.0"]),
            ("area-trim-performance.md", ["mutable", "applicable rerender risk", "Without descendant render counts",
                "no repeated identity surface", "owner evidence required"]),
            ("offline-release-facts.md", ["never authenticated", "RDF equivalence was not assessed",
                "not RDF equivalence or", "not-comparable", "all ten required", "64 MiB",
                "pre-output confirmed manifest", "separate explicit", "result-basename"]),
            ("scoped-component-profile.md", ["all 51", "48-check", "entire `source` record",
                "--package-context-revision", "--package-context-feedback", "V1 rejects ordinary",
                "feedback-bound predecessor", "supersession ancestor", "not a self-contained",
                "package_reference", "package_validation_sha256", "no raw registered inputs"])
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

    private static void AssertRubricIsSoleRequirementSource(string referencesRoot)
    {
        using var rubric = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(referencesRoot, "rubric.json")));
        var ids = rubric.RootElement.GetProperty("core").GetProperty("requirements")
            .EnumerateArray()
            .Select(row => row.GetProperty("id").GetString()!)
            .ToArray();
        Assert(ids.Length == 121 && ids.Distinct(StringComparer.Ordinal).Count() == 121,
            "rubric.json remains the sole current 121-ID source");

        var checklist = File.ReadAllText(Path.Combine(referencesRoot, "checklist.md"));
        AssertContains(checklist, "Generated from rubric.json. Do not edit by hand.", "generated checklist marker");
        foreach (var path in Directory.GetFiles(referencesRoot, "*.md"))
        {
            if (Path.GetFileName(path) is "checklist.md" or "checklist.v1.3.0.md")
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
        AssertContains(execution, "Pass absolute `--root`, `--revision`, `--output`, and `--package-revision` paths", "absolute verification arguments owner");
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
        AssertContains(report, "Schema-version-1 assessments remain parseable and verifiable", "legacy assessment policy");

        var status = File.ReadAllText(Path.Combine(referencesRoot, "status-boundaries.md"));
        AssertContains(status, "direct-evidence-satisfies", "verified decision boundary");
        AssertContains(status, "direct-evidence-conflicts", "gap decision boundary");
        AssertContains(status, "owner-held-evidence-only", "owner decision boundary");
        AssertContains(status, "applicable-evidence-not-obtained", "not-tested decision boundary");
        AssertContains(status, "confirmed-not-applicable", "not-applicable decision boundary");
        AssertContains(status, "New assessments use schema version 2", "current assessment schema");
        AssertContains(status, "Schema version 1 is retained only for immutable", "legacy assessment schema");
        AssertContains(status, "only one directed-gap protocol family", "directed-gap exclusivity");
        AssertContains(status, "A keyed reorder behavior probe passes", "mechanism/outcome boundary example");
        AssertContains(status, "confirmed complete public-policy corpus", "public-absence boundary example");

        var ciRelease = File.ReadAllText(Path.Combine(referencesRoot, "area-ci-release.md"));
        AssertContains(ciRelease, "actual release job graph", "CI-07 job graph rule");
        AssertContains(ciRelease, "path equality and in-place ordering are not an immutable handoff", "CI-08 path identity rule");

        var runtime = File.ReadAllText(Path.Combine(referencesRoot, "area-blazor-runtime.md"));
        AssertContains(runtime, "event handlers, callbacks, conditional branches", "BEQ-09 assignment coverage");
        var renderModeDecision = Regex.Match(
            runtime, @"(?ms)^For documentation requirements,.*?(?=^For a `BEQ-05`)").Value;
        AssertContains(renderModeDecision,
            "For current `2.0.1` `BEQ-03`, require supported-mode documentation **and** a clear " +
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
            "Only for explicitly selected legacy reproduction under the existing `SKILL.md` route, " +
            "apply frozen `1.3.0` `BEQ-03`: \"Unsupported modes fail safely or are clearly documented.\"",
            "render-mode explicitly gated legacy alternative");
        AssertContains(renderModeDecision, "Do not select legacy to bypass a current requirement.",
            "render-mode current scope cannot select legacy");
        AssertContains(renderModeDecision,
            "For genuinely disjunctive requirements, do not call a gap until every remaining applicable " +
            "alternative is directly contradicted.",
            "render-mode decision preserves genuine disjunctions");

        var performance = File.ReadAllText(Path.Combine(referencesRoot, "area-trim-performance.md"));
        AssertContains(performance, "no repeated identity surface", "PERF-02 applicability boundary");
        AssertContains(performance, "missing `IsFixed` establishes an applicable rerender risk", "PERF-05 source risk boundary");
        AssertContains(performance, "Without descendant render counts", "PERF-05 measurement requirement");

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
