namespace UnskipClosedTests.Tool;

internal static class Program
{
    private const string HelpText =
        """
        Usage:
          UnskipClosedTests.Tool inventory --config <path> --output <path> [--repo-root <path>] [--repository <owner/repo>] [--source-commit <sha>]
          UnskipClosedTests.Tool resolve --manifest <path> --output <path> [--github-evidence <path>]
          UnskipClosedTests.Tool apply --config <path> --manifest <path> --agent-output <path> --output <path> [--repo-root <path>] [--github-evidence <path>]

        Exit codes:
          0  Successful inventory/resolve, or apply retained at least one verified edit.
          10 Apply retained no verified candidates and left candidate source files unchanged.
          20 Invalid or stale trusted input.
          30 Infrastructure or verification protocol failure.
        """;

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine(HelpText);
            return ExitCodes.Invalid;
        }

        if (args[0] is "--help" or "-h" or "help" ||
            args.Length == 2 && args[1] is "--help" or "-h")
        {
            Console.WriteLine(HelpText);
            return ExitCodes.Success;
        }

        try
        {
            CliOptions options = Parse(args);
            return options.Command switch
            {
                "inventory" => RunInventory(options),
                "resolve" => await RunResolveAsync(options),
                "apply" => await RunApplyAsync(options),
                _ => throw new ContractException($"Unknown command '{options.Command}'. Expected inventory, resolve, or apply."),
            };
        }
        catch (ContractException ex)
        {
            Console.Error.WriteLine($"invalid: {ex.Message}");
            return ExitCodes.Invalid;
        }
        catch (InfrastructureException ex)
        {
            Console.Error.WriteLine($"infrastructure: {ex.Message}");
            return ExitCodes.Infrastructure;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"infrastructure: unexpected failure: {ex}");
            return ExitCodes.Infrastructure;
        }
    }

    private static int RunInventory(CliOptions options)
    {
        string configPath = options.Required("config");
        string outputPath = options.Required("output");
        string repoRoot = options.Optional("repo-root") ?? Environment.CurrentDirectory;
        ToolConfig config = ConfigLoader.Load(configPath);
        Manifest manifest = InventoryEngine.Create(repoRoot, options.Optional("repository"), config);
        string? expectedSourceCommit = options.Optional("source-commit");
        if (expectedSourceCommit is not null &&
            !string.Equals(expectedSourceCommit, manifest.SourceCommit, StringComparison.OrdinalIgnoreCase))
        {
            throw new ContractException(
                $"Expected source commit '{expectedSourceCommit}' does not match checked-out commit '{manifest.SourceCommit}'.");
        }

        JsonSupport.Write(outputPath, manifest);
        return ExitCodes.Success;
    }

    private static async Task<int> RunResolveAsync(CliOptions options)
    {
        Manifest manifest = ManifestValidator.Read(options.Required("manifest"));
        string outputPath = options.Required("output");
        Manifest resolved = await IssueResolver.ResolveAsync(manifest, options.Optional("github-evidence"));
        JsonSupport.Write(outputPath, resolved);
        return ExitCodes.Success;
    }

    private static async Task<int> RunApplyAsync(CliOptions options)
    {
        string configPath = options.Required("config");
        ToolConfig config = ConfigLoader.Load(configPath);
        ApplyResult result = await ApplyEngine.ApplyAsync(
            options.Optional("repo-root") ?? Environment.CurrentDirectory,
            config,
            options.Required("manifest"),
            options.Required("agent-output"),
            options.Optional("github-evidence"));
        JsonSupport.Write(options.Required("output"), result);
        return result.HasChanges ? ExitCodes.Success : ExitCodes.CleanNoOp;
    }

    private static CliOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new ContractException("A command is required.");
        }

        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int i = 1; i < args.Length; i++)
        {
            string item = args[i];
            if (!item.StartsWith("--", StringComparison.Ordinal) || item.Length == 2)
            {
                throw new ContractException($"Unexpected argument '{item}'.");
            }

            string name = item[2..];
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ContractException($"Option --{name} requires a value.");
            }

            if (!values.TryAdd(name, args[++i]))
            {
                throw new ContractException($"Option --{name} was specified more than once.");
            }
        }

        HashSet<string> allowed = args[0] switch
        {
            "inventory" => ["config", "output", "repo-root", "repository", "source-commit"],
            "resolve" => ["manifest", "output", "github-evidence"],
            "apply" => ["config", "manifest", "agent-output", "output", "repo-root", "github-evidence"],
            _ => [],
        };
        foreach (string name in values.Keys)
        {
            if (!allowed.Contains(name))
            {
                throw new ContractException($"Option --{name} is not valid for command '{args[0]}'.");
            }
        }

        return new CliOptions { Command = args[0], Values = values };
    }
}
