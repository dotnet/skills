using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Validation;

namespace BlazorComponentReadiness.Validator.Preparation;

public static class SourceLayoutResolver
{
    public static SourceLayout Resolve(FullSourceInventory inventory, InputManifest input, string? selectedId)
    {
        var files = inventory.Members.Where(member => member.Kind == "file").Select(member => member.Path).ToArray();
        var prefixes = new HashSet<string>(StringComparer.Ordinal) { "" };
        if (files.Length != 0)
        {
            var path = files[0];
            for (var slash = path.IndexOf('/'); slash >= 0; slash = path.IndexOf('/', slash + 1))
            {
                var prefix = path[..slash];
                if (files.All(file => file.StartsWith(prefix + "/", StringComparison.Ordinal)))
                    prefixes.Add(prefix);
            }
        }
        var candidates = prefixes.Order(StringComparer.Ordinal).Select(prefix => new SourceRootCandidate(
            "root-" + ContractJson.RawDigest(PreparationJson.Bytes(new[] { inventory.ArchiveSha256, prefix })).Value,
            prefix)).ToArray();
        var recognized = RecognizedPrefix(inventory, input);
        var selected = selectedId is null ? null : candidates.SingleOrDefault(candidate => candidate.Id == selectedId);
        string? chosen = null;
        var cause = "ambiguous-source-root";
        if (selectedId is not null && (selected is null || recognized is not null && selected.Prefix != recognized ||
                                      files.Any(file => !file.Contains('/')) && selected.Prefix != ""))
            cause = "invalid-source-root-id";
        else if (files.Length == 0)
            cause = "empty-source-archive";
        else if (recognized is not null)
            chosen = recognized;
        else if (selected is not null)
            chosen = selected.Prefix;
        else if (files.Any(file => !file.Contains('/')))
            chosen = "";
        return new(1, inventory.ArchiveSha256, chosen is null ? "failed" : "succeeded",
            chosen is null ? cause : "completed", chosen, selectedId, recognized, candidates);
    }

    private static string? RecognizedPrefix(FullSourceInventory inventory, InputManifest input)
    {
        if (input.Source is not { Availability: "source-available", RepositoryUri: { } repository, Commit: { } commit } ||
            !Uri.TryCreate(repository, UriKind.Absolute, out var uri) || uri.Host != "github.com")
            return null;
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 2) return null;
        var repo = parts[1].EndsWith(".git", StringComparison.Ordinal) ? parts[1][..^4] : parts[1];
        var locator = $"https://codeload.github.com/{parts[0]}/{repo}/{inventory.ArchiveFormat}/{commit}";
        if (!input.RetrievalAttempts.Any(attempt => attempt.Subject == "source" && attempt.Result == "succeeded" &&
            attempt.RetrievalMethod == "direct-download" && string.Equals(attempt.Locator, locator, StringComparison.OrdinalIgnoreCase)))
            return null;
        var prefix = repo + "-" + commit;
        return inventory.Members.Count > 0 && inventory.Members.All(member =>
            member.Path.StartsWith(prefix + "/", StringComparison.Ordinal) ||
            member.Kind == "directory" && member.Path.TrimEnd('/') == prefix) ? prefix : null;
    }
}
