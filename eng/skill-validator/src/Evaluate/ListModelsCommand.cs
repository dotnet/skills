using System.CommandLine;
using SkillValidator.Shared;

namespace SkillValidator.Evaluate;

/// <summary>
/// Preflight command: enumerate the models the current Copilot token can use and,
/// optionally, assert that a required set of model ids is available.
///
/// This runs the SAME <c>client.ListModelsAsync()</c> call the evaluate command
/// makes before every run, so it is a faithful, up-front check. Wiring it into a
/// dedicated CI job — before the eval matrix fans out across every plugin × model —
/// turns a mid-fan-out "Invalid model" failure (repeated per shard) into a single
/// fast failure that names exactly which configured ids are missing.
/// </summary>
public static class ListModelsCommand
{
    public static Command Create()
    {
        var requireOpt = new Option<string?>("--require")
        {
            Description = "Comma- or space-separated model ids that MUST be available. " +
                "Exit non-zero listing any that are missing. Omit to just print the available models.",
        };
        var jsonOpt = new Option<bool>("--json")
        {
            Description = "Emit the available model ids as a JSON array (local use). " +
                "Ignored when --require is set, which never prints the full roster.",
        };
        var verboseOpt = new Option<bool>("--verbose") { Description = "Show detailed client output." };

        var command = new Command(
            "list-models",
            "List the models available to the current Copilot token (preflight). " +
                "With --require, fail fast if any required model id is unavailable.")
        {
            requireOpt,
            jsonOpt,
            verboseOpt,
        };

        command.SetAction(async (parseResult, _) =>
        {
            var require = parseResult.GetValue(requireOpt);
            var asJson = parseResult.GetValue(jsonOpt);
            var verbose = parseResult.GetValue(verboseOpt);
            return await Run(require, asJson, verbose);
        });

        return command;
    }

    public static async Task<int> Run(string? require, bool asJson, bool verbose)
    {
        List<string> modelIds;
        try
        {
            var client = await AgentRunner.GetSharedClient(verbose);
            var models = await RetryHelper.ExecuteWithRetry(
                async _ => await client.ListModelsAsync(),
                label: "ListModels",
                maxRetries: 3,
                baseDelayMs: 2_000,
                totalTimeoutMs: 60_000);
            modelIds = models.Select(m => m.Id).ToList();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Failed to list models: {error}");
            return 1;
        }

        var sortedIds = modelIds.OrderBy(id => id, StringComparer.Ordinal).ToList();
        var required = ParseRequired(require);

        // Privacy: the full set of available model ids is sensitive and must NOT be
        // dumped into CI logs. The CI preflight always passes --require, so in that
        // path we only ever confirm the (already-public, repo-configured) required
        // ids and, on failure, name just the MISSING ones plus an available count —
        // never the full roster. The full list is emitted only on explicit local
        // invocations: --json, or a bare `list-models` with no --require.
        if (required.Count == 0)
        {
            if (asJson)
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(sortedIds));
            }
            else
            {
                Console.WriteLine($"Available models ({sortedIds.Count}):");
                foreach (var id in sortedIds) Console.WriteLine($"  {id}");
            }

            return 0;
        }

        var available = new HashSet<string>(modelIds, StringComparer.Ordinal);
        var missing = required.Where(r => !available.Contains(r)).ToList();
        if (missing.Count > 0)
        {
            // ::error:: renders as a GitHub Actions annotation; the plain lines keep
            // the failure legible in local runs and raw logs. Deliberately does NOT
            // print the full available roster — only the missing required ids and a
            // count — to avoid exposing the model list.
            Console.Error.WriteLine(
                $"::error::Required model id(s) unavailable to this Copilot token: {string.Join(", ", missing)}");
            Console.Error.WriteLine($"Missing model(s): {string.Join(", ", missing)}");
            Console.Error.WriteLine($"Available model count: {sortedIds.Count} (list withheld).");
            return 1;
        }

        Console.WriteLine($"All {required.Count} required model(s) available: {string.Join(", ", required)}");
        return 0;
    }

    /// <summary>
    /// Accepts a comma- and/or whitespace-separated list, and tolerates a JSON
    /// array (e.g. the output of <c>resolve-judge.mjs required</c>) by stripping
    /// brackets and quotes. De-duplicates while preserving first-seen order.
    /// </summary>
    private static List<string> ParseRequired(string? require)
    {
        if (string.IsNullOrWhiteSpace(require)) return new List<string>();

        var cleaned = require.Trim();
        if (cleaned.StartsWith('[') && cleaned.EndsWith(']'))
        {
            cleaned = cleaned.Substring(1, cleaned.Length - 2);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var raw in cleaned.Split(new[] { ',', ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var id = raw.Trim().Trim('"', '\'');
            if (id.Length > 0 && seen.Add(id)) result.Add(id);
        }
        return result;
    }
}
