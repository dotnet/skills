using System.Text.RegularExpressions;

namespace SkillValidator.Check;

/// <summary>
/// Scans skill, agent, and plugin markdown/HTML files for external references
/// and potentially dangerous patterns.
/// </summary>
public static partial class ReferenceScanner
{
    // --- Finding model ---

    public sealed record RefFinding(
        string Path,
        int LineNum,
        string Code,
        string Message);

    // --- Known domain loading ---

    /// <summary>
    /// Load a known-domains file. Lines starting with # are comments, blank lines are ignored.
    /// Entries may be bare domains (e.g. "microsoft.com") or path-scoped
    /// (e.g. "github.com/dotnet/runtime").
    /// </summary>
    public static IReadOnlyList<string> LoadKnownDomains(string path)
    {
        if (!File.Exists(path))
            return [];

        var domains = new List<string>();
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length > 0 && line[0] != '#')
                domains.Add(line.ToLowerInvariant());
        }
        return domains;
    }

    // --- Domain matching ---

    public static bool IsKnownDomain(string url, IReadOnlyList<string> knownDomains)
    {
        var urlLower = url.ToLowerInvariant();
        foreach (var domain in knownDomains)
        {
            if (domain.Contains('/'))
            {
                // Path-scoped: require /, ?, #, or end-of-string after the prefix
                var escaped = Regex.Escape(domain);
                if (Regex.IsMatch(urlLower, $@"^https?://(www\.)?{escaped}([/?#]|$)"))
                    return true;
            }
            else
            {
                // Extract host from URL
                var host = StripScheme(urlLower);
                host = StripPathAndPort(host);
                if (host == domain || host.EndsWith($".{domain}", StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }

    public static bool IsLocalUrl(string url)
    {
        var lower = url.ToLowerInvariant();
        return LocalhostRegex().IsMatch(lower) ||
               LoopbackRegex().IsMatch(lower) ||
               WildcardListenRegex().IsMatch(lower);
    }

    public static bool IsHttpNotHttps(string url)
    {
        var lower = url.ToLowerInvariant();
        if (!lower.StartsWith("http://", StringComparison.Ordinal))
            return false;
        if (IsLocalUrl(url))
            return false;

        var host = StripScheme(lower);
        host = StripPathAndPort(host);
        return host != "schemas.microsoft.com" &&
               !host.EndsWith(".schemas.microsoft.com", StringComparison.Ordinal);
    }

    // --- File scanning ---

    /// <summary>
    /// Scan a single file for reference issues. Returns findings (errors).
    /// </summary>
    public static IReadOnlyList<RefFinding> ScanFile(string filePath, IReadOnlyList<string> knownDomains, string? knownDomainsFilePath = null)
    {
        var knownDomainsLabel = knownDomainsFilePath ?? "the known-domains file";
        var findings = new List<RefFinding>();

        string[] lines;
        try
        {
            lines = File.ReadAllLines(filePath);
        }
        catch (Exception ex)
        {
            findings.Add(new RefFinding(filePath, 0, "FILE-READ-ERROR", $"Failed to read file: {ex.Message}"));
            return findings;
        }

        // Multi-line <script> tag detection for SRI checks
        var fullContent = string.Join("\n", lines);

        foreach (Match m in ScriptTagMultiLineRegex().Matches(fullContent))
        {
            var tag = m.Value;
            var hasSri = SriIntegrityRegex().IsMatch(tag);
            var tagLineNum = fullContent[..m.Index].Split('\n').Length;

            if (ExternalSrcRegex().IsMatch(tag))
            {
                string? scriptUrl = null;
                var srcMatch = ScriptSrcExtractRegex().Match(tag);
                if (srcMatch.Success)
                    scriptUrl = srcMatch.Groups[1].Value;

                // SRI suppresses the SCRIPT-NO-SRI error only; domain/HTTPS
                // checks still apply via the line-by-line URL scan below.
                if (!hasSri && (scriptUrl is null || !IsLocalUrl(scriptUrl)))
                {
                    findings.Add(new RefFinding(filePath, tagLineNum, "SCRIPT-NO-SRI",
                        "External script tag without integrity (SRI) attribute"));
                }
            }
        }

        // Line-by-line scanning
        bool inFencedBlock = false;
        char openFenceChar = '`';
        int openFenceLen = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNum = i + 1;

            // Track fenced code blocks
            var fenceMatch = FenceOpenRegex().Match(line);
            if (fenceMatch.Success)
            {
                var fenceStr = fenceMatch.Groups[1].Value;
                var fenceChar = fenceStr[0];
                var fenceLen = fenceStr.Length;

                if (!inFencedBlock)
                {
                    inFencedBlock = true;
                    openFenceChar = fenceChar;
                    openFenceLen = fenceLen;
                }
                else if (fenceChar == openFenceChar && fenceLen >= openFenceLen &&
                         FenceCloseRegex().IsMatch(line))
                {
                    inFencedBlock = false;
                }
                continue;
            }

            // Pipe-to-shell detection
            if (PipeToShellRegex().IsMatch(line))
            {
                bool isPipeAllowed = AllowedPipeUrls.Any(allowed =>
                    Regex.IsMatch(line, Regex.Escape(allowed) + @"(\s|['""|]|$)",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

                if (!isPipeAllowed)
                {
                    findings.Add(new RefFinding(filePath, lineNum, "PIPE-TO-SHELL",
                        "Pipe-to-shell pattern: content is downloaded and piped directly to a shell interpreter"));
                }
            }

            // URL scanning
            foreach (Match urlMatch in UrlRegex().Matches(line))
            {
                var url = urlMatch.Value.TrimEnd('.', ',', ';', ':', ')', '\'', '"');

                // Skip placeholder/template URLs (check host portion only)
                var urlHost = StripScheme(url.ToLowerInvariant());
                urlHost = StripPathAndPort(urlHost);
                if (PlaceholderHostRegex().IsMatch(urlHost))
                    continue;

                if (inFencedBlock)
                {
                    // Inside fenced code blocks: skip HTTP-not-HTTPS but still check external domains
                    if (!IsKnownDomain(url, knownDomains) && !IsLocalUrl(url))
                    {
                        findings.Add(new RefFinding(filePath, lineNum, "EXTERNAL-DOMAIN",
                            $"Domain not in known-domains file -- add it to {knownDomainsLabel} if this reference is intentional: {url}"));
                    }
                    continue;
                }

                if (IsHttpNotHttps(url))
                {
                    findings.Add(new RefFinding(filePath, lineNum, "HTTP-NOT-HTTPS",
                        $"Insecure http:// URL (use https://): {url}"));
                }
                else if (!IsKnownDomain(url, knownDomains) && !IsLocalUrl(url))
                {
                    findings.Add(new RefFinding(filePath, lineNum, "EXTERNAL-DOMAIN",
                        $"Domain not in known-domains file -- add it to {knownDomainsLabel} if this reference is intentional: {url}"));
                }
            }
        }

        return findings;
    }

    /// <summary>
    /// Discover scannable files under the given directories. Finds SKILL.md,
    /// *.agent.md, and references/*.md files recursively.
    /// </summary>
    public static IReadOnlyList<string> DiscoverFiles(IReadOnlyList<string> directories)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in directories)
        {
            var fullPath = Path.GetFullPath(dir);
            if (!Directory.Exists(fullPath))
                continue;

            // SKILL.md files
            foreach (var f in Directory.GetFiles(fullPath, "SKILL.md", SearchOption.AllDirectories))
                files.Add(f);

            // *.agent.md files
            foreach (var f in Directory.GetFiles(fullPath, "*.agent.md", SearchOption.AllDirectories))
                files.Add(f);

            // *.md files directly inside any references/ subdirectory
            foreach (var refDir in Directory.GetDirectories(fullPath, "references", SearchOption.AllDirectories))
            {
                foreach (var f in Directory.GetFiles(refDir, "*.md"))
                    files.Add(f);
            }
        }

        return files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Scan all provided files and return aggregated findings.
    /// </summary>
    public static IReadOnlyList<RefFinding> ScanFiles(IReadOnlyList<string> filePaths, IReadOnlyList<string> knownDomains, string? knownDomainsFilePath = null)
    {
        var allFindings = new List<RefFinding>();
        foreach (var file in filePaths)
        {
            var results = ScanFile(file, knownDomains, knownDomainsFilePath);
            allFindings.AddRange(results);
        }
        return allFindings;
    }

    // --- Helpers ---

    private static string StripScheme(string url) =>
        url.StartsWith("https://", StringComparison.Ordinal) ? url[8..] :
        url.StartsWith("http://", StringComparison.Ordinal) ? url[7..] : url;

    private static string StripPathAndPort(string hostWithPath)
    {
        var idx = hostWithPath.IndexOfAny(['/', ':', '?', '#']);
        return idx >= 0 ? hostWithPath[..idx] : hostWithPath;
    }

    private static readonly string[] AllowedPipeUrls =
    [
        "https://dot.net/v1/dotnet-install.sh",
        "https://aka.ms/dotnet-install.sh",
    ];

    // --- Regex patterns ---

    [GeneratedRegex(@"https?://[^\s\)\]""'<>;]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"curl\s[^|]*\|\s*(ba)?sh\b|wget\s[^|]*\|\s*(ba)?sh\b", RegexOptions.IgnoreCase)]
    private static partial Regex PipeToShellRegex();

    [GeneratedRegex(@"(?i)integrity\s*=")]
    private static partial Regex SriIntegrityRegex();

    [GeneratedRegex(@"(?i)src\s*=\s*[""']https?://")]
    private static partial Regex ExternalSrcRegex();

    [GeneratedRegex(@"(?is)<script\s[^>]*src\s*=\s*[""'][^""']*[""'][^>]*>")]
    private static partial Regex ScriptTagMultiLineRegex();

    [GeneratedRegex(@"(?i)src\s*=\s*[""']([^""']+)[""']")]
    private static partial Regex ScriptSrcExtractRegex();

    [GeneratedRegex(@"(?i)(\{[^}]+\}|your[-_]?\w*name\w*|your[-_]\w+|example\.(com|org|net)|contoso\.com)")]
    private static partial Regex PlaceholderHostRegex();

    [GeneratedRegex(@"^\s{0,3}(`{3,}|~{3,})")]
    private static partial Regex FenceOpenRegex();

    [GeneratedRegex(@"^\s{0,3}[`~]+\s*$")]
    private static partial Regex FenceCloseRegex();

    [GeneratedRegex(@"^https?://localhost([:/]|$)")]
    private static partial Regex LocalhostRegex();

    [GeneratedRegex(@"^https?://127\.0\.0\.1([:/]|$)")]
    private static partial Regex LoopbackRegex();

    [GeneratedRegex(@"^https?://[+*]([:/]|$)")]
    private static partial Regex WildcardListenRegex();
}
