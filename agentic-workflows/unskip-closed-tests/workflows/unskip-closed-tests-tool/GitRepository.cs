using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace UnskipClosedTests.Tool;

internal sealed class GitRepository
{
    private static readonly Regex GitHubRemotePattern = new(
        @"(?:github\.com[:/])(?<owner>[^/:\s]+)/(?<repo>[^/\s]+?)(?:\.git)?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);

    private GitRepository(string root, string commit, string objectFormat, string repository)
    {
        Root = root;
        Commit = commit;
        ObjectFormat = objectFormat;
        Repository = repository;
    }

    public string Root { get; }
    public string Commit { get; }
    public string ObjectFormat { get; }
    public string Repository { get; }

    public static GitRepository Open(string requestedRoot, string? repositoryOverride)
    {
        string root = RunGit(requestedRoot, ["rev-parse", "--show-toplevel"]).Trim();
        if (root.Length == 0)
        {
            throw new ContractException("Could not determine the repository root.");
        }

        root = Path.GetFullPath(root);
        string requested = Path.GetFullPath(requestedRoot);
        if (!string.Equals(root, requested, StringComparison.OrdinalIgnoreCase))
        {
            throw new ContractException($"--repo-root must be the exact git repository root '{root}'.");
        }

        string commit = RunGit(root, ["rev-parse", "HEAD"]).Trim();
        string objectFormat = RunGit(root, ["rev-parse", "--show-object-format"]).Trim();
        if (objectFormat is not ("sha1" or "sha256"))
        {
            throw new ContractException($"Unsupported git object format '{objectFormat}'.");
        }

        string repository = repositoryOverride is null
            ? InferRepository(root)
            : ValidateRepository(repositoryOverride);
        return new GitRepository(root, commit, objectFormat, repository);
    }

    public string HeadBlobOid(string path)
    {
        string normalized = PathRules.ValidateRelativePath(path, "source path");
        string output = RunGit(Root, ["ls-tree", Commit, "--", normalized]).Trim();
        if (output.Length == 0)
        {
            throw new ContractException($"Source path '{normalized}' is not tracked at commit {Commit}.");
        }

        string[] tabParts = output.Split('\t');
        string[] metadata = tabParts[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (metadata.Length != 3 || metadata[1] != "blob")
        {
            throw new ContractException($"Source path '{normalized}' is not a regular tracked blob.");
        }

        if (metadata[0] == "120000")
        {
            throw new ContractException($"Source path '{normalized}' is a git symlink.");
        }

        return metadata[2];
    }

    public byte[] HeadBytes(string path)
    {
        string normalized = PathRules.ValidateRelativePath(path, "source path");
        return RunGitBytes(Root, ["show", $"{Commit}:{normalized}"]);
    }

    public void RequireWorktreeMatchesHead(string path, byte[] bytes)
    {
        _ = bytes;
        string normalized = PathRules.ValidateRelativePath(path, "source path");
        string status = RunGit(
            Root,
            ["status", "--porcelain=v1", "--untracked-files=no", "--", normalized]).Trim();
        if (status.Length != 0)
        {
            throw new ContractException($"Source path '{path}' does not match checked-out commit {Commit}.");
        }
    }

    public string MetadataDirectory()
    {
        string value = RunGit(Root, ["rev-parse", "--git-dir"]).Trim();
        return Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(Root, value));
    }

    private static string InferRepository(string root)
    {
        string remote = RunGit(root, ["config", "--get", "remote.origin.url"], allowFailure: true).Trim();
        Match match = GitHubRemotePattern.Match(remote);
        if (!match.Success)
        {
            throw new ContractException("Could not infer owner/repo from remote.origin.url; pass --repository.");
        }

        return ValidateRepository($"{match.Groups["owner"].Value}/{match.Groups["repo"].Value}");
    }

    private static string ValidateRepository(string repository)
    {
        string[] parts = repository.Split('/');
        if (parts.Length != 2 || parts.Any(static part => part.Length == 0 || part is "." or ".."))
        {
            throw new ContractException($"Repository '{repository}' must be owner/repo.");
        }

        return $"{parts[0].ToLowerInvariant()}/{parts[1].ToLowerInvariant()}";
    }

    private static string RunGit(string workingDirectory, IReadOnlyList<string> arguments, bool allowFailure = false) =>
        Encoding.UTF8.GetString(RunGitBytes(workingDirectory, arguments, allowFailure));

    private static byte[] RunGitBytes(string workingDirectory, IReadOnlyList<string> arguments, bool allowFailure = false)
    {
        ProcessStartInfo startInfo = new("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using Process process = Process.Start(startInfo)
                ?? throw new InfrastructureException("Could not start git.");
            using MemoryStream output = new();
            process.StandardOutput.BaseStream.CopyTo(output);
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0 && !allowFailure)
            {
                throw new ContractException($"git {string.Join(' ', arguments)} failed: {error.Trim()}");
            }

            return output.ToArray();
        }
        catch (ContractException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new InfrastructureException("Could not invoke git.", ex);
        }
    }
}
