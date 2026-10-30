using System.Globalization;
using System.Net;
using System.Text.Json;

namespace BlazorPackageResolver;

public static class Lp02SourceAcquisition
{
    public static HttpClientHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseDefaultCredentials = false,
        Credentials = null,
        UseProxy = false,
        AutomaticDecompression = DecompressionMethods.None,
        CheckCertificateRevocationList = true,
        MaxResponseHeadersLength = 16
    };

    public static async Task<byte[]> CaptureAsync(string packagePath, string destination,
        string projectPath, string implementationPath, string rationale)
    {
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new InvalidDataException("LP-02 capture destination must not exist.");
        byte[] bytes;
        using (var file = File.OpenRead(packagePath))
        {
            if (file.Length > Lp02PublicSourceResolver.MaxArchiveBytes)
                throw new InvalidDataException("LP-02 package exceeds the bounded profile.");
            bytes = new byte[(int)file.Length];
            await file.ReadExactlyAsync(bytes);
            if (file.ReadByte() != -1)
                throw new InvalidDataException("LP-02 package changed during acquisition.");
        }
        using var handler = CreateHandler();
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        var (observation, archive) = await RetrieveAsync(client, bytes, projectPath, implementationPath, rationale);
        Directory.CreateDirectory(destination);
        if (archive is not null)
            await File.WriteAllBytesAsync(Path.Combine(destination, "source.zip"), archive);
        var receipt = JsonSerializer.SerializeToUtf8Bytes(observation, Lp02PublicSourceResolver.JsonOptions);
        await File.WriteAllBytesAsync(Path.Combine(destination, "observation.json"), receipt);
        return receipt;
    }

    internal static async Task<(Lp02Observation Observation, byte[]? Archive)> RetrieveAsync(
        HttpClient client, ReadOnlyMemory<byte> bytes, string projectPath, string implementationPath, string rationale)
    {
        var (state, package) = Lp02PublicSourceResolver.InspectPackage(bytes);
        if (state != "ready" || package is null ||
            !Lp02PublicSourceResolver.TryRepository(package.RepositoryUrl, package.Commit, out var repository, out var url))
            throw new InvalidDataException("LP-02 acquisition requires a supported exact package declaration.");
        if (!Lp02PublicSourceResolver.SafePath(projectPath) ||
            !Lp02PublicSourceResolver.SafePath(implementationPath) ||
            projectPath == implementationPath || string.IsNullOrWhiteSpace(rationale) ||
            rationale.Length > 1000 || rationale.Any(c => c is < ' ' or > '~'))
            throw new InvalidDataException("LP-02 requires two safe selected paths and a short ASCII mapping rationale.");
        var result = "unavailable";
        int? status = null;
        byte[]? archive = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("lp02-anonymous-github/1.0.0");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            status = (int)response.StatusCode;
            result = status switch
            {
                200 => "retrieved",
                404 => "not-found",
                401 or 403 => "auth-required",
                429 or 451 => "blocked",
                >= 500 and <= 599 => "unavailable",
                _ => "unsupported"
            };
            if (status == 200)
            {
                if (response.Content.Headers.ContentEncoding.Count != 0 ||
                    response.Content.Headers.ContentLength > Lp02PublicSourceResolver.MaxArchiveBytes)
                    result = "unsupported";
                else
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                    using var buffer = new MemoryStream();
                    var chunk = new byte[8192];
                    int count;
                    while ((count = await stream.ReadAsync(chunk, timeout.Token)) > 0)
                    {
                        if (buffer.Length + count > Lp02PublicSourceResolver.MaxArchiveBytes)
                        {
                            result = "unsupported";
                            break;
                        }
                        buffer.Write(chunk, 0, count);
                    }
                    if (result == "retrieved") archive = buffer.ToArray();
                }
            }
        }
        catch (HttpRequestException) { result = "unavailable"; archive = null; }
        catch (IOException) { result = "unavailable"; archive = null; }
        catch (OperationCanceledException) { result = "unavailable"; archive = null; }
        if (result == "unavailable" && status == 200) status = null;
        var inventory = new List<Lp02SourceFile>();
        if (archive is not null && Lp02PublicSourceResolver.TryArchive(archive, out var files, package.Commit))
        {
            foreach (var (kind, path) in new[] { ("project", projectPath), ("implementation", implementationPath) })
                if (files.TryGetValue(path, out var file))
                    inventory.Add(new(kind, path, Catalog.Digest(file)));
        }
        var observation = new Lp02Observation(1, Lp02PublicSourceResolver.Producer,
            package.Id, package.Version, Catalog.Digest(bytes.Span), repository, package.Commit,
            new(url, DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                Lp02PublicSourceResolver.Transport, result, status),
            archive is null ? null : new("source-archive", Catalog.Digest(archive)), inventory.ToArray(), rationale);
        return (observation, archive);
    }
}
