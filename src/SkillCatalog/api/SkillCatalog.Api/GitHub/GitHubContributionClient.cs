using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SkillCatalog.Api.Options;

namespace SkillCatalog.Api.GitHub;

public sealed class GitHubContributionClient : IGitHubContributionClient
{
    private readonly HttpClient _http;
    private readonly GitHubSubmissionOptions _options;
    private readonly TimeProvider _time;
    public GitHubContributionClient(HttpClient http,IOptions<GitHubSubmissionOptions> options,TimeProvider time)
    {
        _http=http;_options=options.Value;_time=time;
        var uri=new Uri(_options.ApiBaseUrl);
        if(uri.Scheme!="https" || !string.Equals(uri.Host,"api.github.com",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("GitHub API host is not allowlisted.");
        _http.BaseAddress=uri;_http.DefaultRequestHeaders.UserAgent.ParseAdd("SkillCatalog/1.0");_http.DefaultRequestHeaders.Accept.Add(new("application/vnd.github+json"));_http.DefaultRequestHeaders.Add("X-GitHub-Api-Version",_options.ApiVersion);
    }
    private HttpRequestMessage Request(HttpMethod method,string path,string token,object? body=null){var r=new HttpRequestMessage(method,path);r.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);if(body is not null)r.Content=JsonContent.Create(body);return r;}
    private async Task<T> Send<T>(HttpRequestMessage request,CancellationToken ct)
    {
        using (request)
        {
            for(var attempt=0;;attempt++)
            {
                using var current=await CloneAsync(request,ct);
                using var response=await _http.SendAsync(current,ct);
                if((int)response.StatusCode==429 || response.Headers.Contains("X-RateLimit-Remaining")&&response.Headers.GetValues("X-RateLimit-Remaining").FirstOrDefault()=="0")
                    throw new GitHubRateLimitException(response.Headers.RetryAfter?.Delta);
                var transient=response.StatusCode is System.Net.HttpStatusCode.RequestTimeout or System.Net.HttpStatusCode.BadGateway or System.Net.HttpStatusCode.ServiceUnavailable or System.Net.HttpStatusCode.GatewayTimeout;
                if(transient&&attempt<_options.MaxRetries)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50*(attempt+1)),_time,ct);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                return (await response.Content.ReadFromJsonAsync<T>(cancellationToken:ct))!;
            }
        }
    }
    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage source,CancellationToken ct)
    {
        var clone=new HttpRequestMessage(source.Method,source.RequestUri);
        foreach(var header in source.Headers) clone.Headers.TryAddWithoutValidation(header.Key,header.Value);
        if(source.Content is not null)
        {
            var bytes=await source.Content.ReadAsByteArrayAsync(ct);
            clone.Content=new ByteArrayContent(bytes);
            foreach(var header in source.Content.Headers) clone.Content.Headers.TryAddWithoutValidation(header.Key,header.Value);
        }
        return clone;
    }
    public async Task<GitHubIdentity> GetIdentityAsync(string token,CancellationToken ct){var x=await Send<JsonElement>(Request(HttpMethod.Get,"/user",token),ct);return new(x.GetProperty("id").GetInt64(),x.GetProperty("login").GetString()!);}
    public async Task<IReadOnlyList<GitHubInstallation>> GetInstallationsAsync(string token,CancellationToken ct){var x=await Send<JsonElement>(Request(HttpMethod.Get,"/user/installations?per_page=100",token),ct);return x.GetProperty("installations").EnumerateArray().Select(e=>new GitHubInstallation(e.GetProperty("id").GetInt64(),e.GetProperty("account").GetProperty("login").GetString()!,e.GetProperty("permissions").EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.GetString()!,StringComparer.OrdinalIgnoreCase))).ToArray();}
    public async Task<GitHubRepository?> GetEligibleForkAsync(string token,string login,CancellationToken ct){try{var x=await Send<JsonElement>(Request(HttpMethod.Get,$"/repos/{Uri.EscapeDataString(login)}/{Uri.EscapeDataString(_options.TargetRepository)}",token),ct);if(!x.GetProperty("fork").GetBoolean())return null;var expectedParent=$"{_options.TargetOwner}/{_options.TargetRepository}";if(!x.TryGetProperty("parent",out var parent)||!string.Equals(parent.GetProperty("full_name").GetString(),expectedParent,StringComparison.OrdinalIgnoreCase))return null;var branch=x.GetProperty("default_branch").GetString()!;var reference=await Send<JsonElement>(Request(HttpMethod.Get,$"/repos/{Uri.EscapeDataString(login)}/{Uri.EscapeDataString(_options.TargetRepository)}/git/ref/heads/{Uri.EscapeDataString(branch)}",token),ct);return new(login,_options.TargetRepository,branch,reference.GetProperty("object").GetProperty("sha").GetString()!,true);}catch(HttpRequestException){return null;}}
    public async Task CreateBranchAsync(string token,string owner,string repository,string branch,string sha,CancellationToken ct)=>_=await Send<JsonElement>(Request(HttpMethod.Post,$"/repos/{owner}/{repository}/git/refs",token,new{@ref=$"refs/heads/{branch}",sha}),ct);
    public async Task UpdateBranchAsync(string token,string owner,string repository,string branch,string sha,CancellationToken ct)=>_=await Send<JsonElement>(Request(HttpMethod.Patch,$"/repos/{owner}/{repository}/git/refs/heads/{branch}",token,new{sha,force=false}),ct);
    public async Task<string> CreateCommitAsync(string token,string owner,string repository,string branch,string baseTree,IReadOnlyList<GitHubFileChange> changes,string message,CancellationToken ct){var treeItems=new List<object>();foreach(var change in changes.OrderBy(x=>x.Path,StringComparer.Ordinal)){if(change.Content is null){treeItems.Add(new{path=change.Path,mode="100644",type="blob",sha=(string?)null});continue;}var blob=await Send<JsonElement>(Request(HttpMethod.Post,$"/repos/{owner}/{repository}/git/blobs",token,new{content=Convert.ToBase64String(change.Content),encoding="base64"}),ct);treeItems.Add(new{path=change.Path,mode="100644",type="blob",sha=blob.GetProperty("sha").GetString()});}var baseCommit=await Send<JsonElement>(Request(HttpMethod.Get,$"/repos/{owner}/{repository}/git/commits/{baseTree}",token),ct);var baseTreeSha=baseCommit.GetProperty("tree").GetProperty("sha").GetString();var tree=await Send<JsonElement>(Request(HttpMethod.Post,$"/repos/{owner}/{repository}/git/trees",token,new{base_tree=baseTreeSha,tree=treeItems}),ct);var commit=await Send<JsonElement>(Request(HttpMethod.Post,$"/repos/{owner}/{repository}/git/commits",token,new{message,tree=tree.GetProperty("sha").GetString(),parents=new[]{baseTree}}),ct);return commit.GetProperty("sha").GetString()!;}
    public async Task<GitHubRepositorySnapshot> GetTargetSnapshotAsync(string token,CancellationToken ct){var reference=await Send<JsonElement>(Request(HttpMethod.Get,$"/repos/{_options.TargetOwner}/{_options.TargetRepository}/git/ref/heads/{Uri.EscapeDataString(_options.BaseBranch)}",token),ct);var commitSha=reference.GetProperty("object").GetProperty("sha").GetString()!;var commit=await Send<JsonElement>(Request(HttpMethod.Get,$"/repos/{_options.TargetOwner}/{_options.TargetRepository}/git/commits/{commitSha}",token),ct);var treeSha=commit.GetProperty("tree").GetProperty("sha").GetString()!;var tree=await Send<JsonElement>(Request(HttpMethod.Get,$"/repos/{_options.TargetOwner}/{_options.TargetRepository}/git/trees/{treeSha}?recursive=1",token),ct);if(tree.TryGetProperty("truncated",out var truncated)&&truncated.GetBoolean())throw new InvalidOperationException("GitHub returned a truncated repository tree.");var entries=tree.GetProperty("tree").EnumerateArray().Select(e=>new GitHubTreeEntry(e.GetProperty("path").GetString()!,e.GetProperty("type").GetString()!,e.GetProperty("sha").GetString()!,e.TryGetProperty("size",out var size)?size.GetInt64():null)).ToArray();return new(commitSha,entries);}
    public async Task<GitHubPullRequest> CreatePullRequestAsync(string token,string headOwner,string headBranch,string title,string body,CancellationToken ct){var x=await Send<JsonElement>(Request(HttpMethod.Post,$"/repos/{_options.TargetOwner}/{_options.TargetRepository}/pulls",token,new{title,body,head=$"{headOwner}:{headBranch}",@base=_options.BaseBranch}),ct);return Pull(x);}
    public async Task<GitHubPullRequest> GetPullRequestAsync(string token,int number,CancellationToken ct)=>Pull(await Send<JsonElement>(Request(HttpMethod.Get,$"/repos/{_options.TargetOwner}/{_options.TargetRepository}/pulls/{number}",token),ct));
    public async Task<IReadOnlyList<GitHubCheck>> GetChecksAsync(string token,string sha,CancellationToken ct){var x=await Send<JsonElement>(Request(HttpMethod.Get,$"/repos/{_options.TargetOwner}/{_options.TargetRepository}/commits/{sha}/check-runs?per_page=100",token),ct);return x.GetProperty("check_runs").EnumerateArray().Select(e=>new GitHubCheck(e.GetProperty("name").GetString()!,e.GetProperty("status").GetString()!,e.TryGetProperty("conclusion",out var c)?c.GetString():null,e.GetProperty("html_url").GetString()!)).ToArray();}
    public async Task<IReadOnlyList<GitHubReview>> GetReviewsAsync(string token,int pullRequestNumber,CancellationToken ct){var x=await Send<JsonElement>(Request(HttpMethod.Get,$"/repos/{_options.TargetOwner}/{_options.TargetRepository}/pulls/{pullRequestNumber}/reviews?per_page=100",token),ct);return x.EnumerateArray().Select(e=>new GitHubReview(e.GetProperty("id").GetInt64(),e.GetProperty("state").GetString()!,e.TryGetProperty("html_url",out var url)?url.GetString():null)).ToArray();}
    private static GitHubPullRequest Pull(JsonElement x)=>new(x.GetProperty("number").GetInt32(),x.GetProperty("html_url").GetString()!,x.GetProperty("state").GetString()!,x.GetProperty("head").GetProperty("sha").GetString()!,x.TryGetProperty("merged_at",out var merged)&&merged.ValueKind!=JsonValueKind.Null);
}
public sealed class GitHubRateLimitException(TimeSpan? retryAfter):Exception("GitHub rate limit exceeded."){public TimeSpan? RetryAfter{get;}=retryAfter;}




