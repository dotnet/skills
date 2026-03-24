using GitHub.Copilot.SDK;

namespace SkillValidator.Evaluate;

/// <summary>
/// Shared helper for single-turn LLM calls used by judges and analyzers.
/// Encapsulates session creation, event handling, timeout, and token tracking.
/// </summary>
internal static class LlmSession
{
    internal record LlmResponse(string Content, TokenUsage Tokens);

    /// <summary>
    /// Create a session, send a single prompt, and return the response.
    /// </summary>
    /// <param name="model">Model identifier.</param>
    /// <param name="systemPrompt">System prompt content.</param>
    /// <param name="userPrompt">User prompt to send.</param>
    /// <param name="workDir">Working directory for the session.</param>
    /// <param name="timeoutMs">Per-attempt timeout in milliseconds.</param>
    /// <param name="verbose">Whether to pass verbose flag to the client.</param>
    /// <param name="timeoutLabel">Label for timeout log messages (e.g. "Judge", "Pairwise judge").</param>
    /// <param name="onPermissionRequest">Permission handler; defaults to deny-all if null.</param>
    /// <param name="cancellationToken">Cancellation token (linked with timeout internally).</param>
    internal static async Task<LlmResponse> SendAsync(
        string model,
        string systemPrompt,
        string userPrompt,
        string workDir,
        int timeoutMs,
        bool verbose,
        string timeoutLabel = "LLM",
        PermissionRequestHandler? onPermissionRequest = null,
        CancellationToken cancellationToken = default)
    {
        var client = await AgentRunner.GetSharedClient(verbose);

        await using var session = await client.CreateSessionAsync(new SessionConfig
        {
            Model = model,
            Streaming = true,
            WorkingDirectory = workDir,
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Replace,
                Content = systemPrompt,
            },
            InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
            OnPermissionRequest = onPermissionRequest ?? ((_, _) => Task.FromResult(new PermissionRequestResult
            {
                Kind = PermissionRequestResultKind.DeniedByRules,
            })),
        });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs);
        using var timer = new Timer(_ =>
        {
            Console.Error.WriteLine($"      ⏰ {timeoutLabel} timed out after {timeoutMs / 1000}s.");
        }, null, timeoutMs, Timeout.Infinite);

        var done = new TaskCompletionSource<string>();
        string responseContent = "";
        int inputTokens = 0, outputTokens = 0, cacheReadTokens = 0, cacheWriteTokens = 0;

        session.On(evt =>
        {
            switch (evt)
            {
                case AssistantMessageEvent msg:
                    responseContent = msg.Data.Content ?? "";
                    break;
                case AssistantUsageEvent usage:
                    inputTokens += (int)(usage.Data.InputTokens ?? 0);
                    outputTokens += (int)(usage.Data.OutputTokens ?? 0);
                    cacheReadTokens += (int)(usage.Data.CacheReadTokens ?? 0);
                    cacheWriteTokens += (int)(usage.Data.CacheWriteTokens ?? 0);
                    break;
                case SessionIdleEvent:
                    done.TrySetResult(responseContent);
                    break;
                case SessionErrorEvent err:
                    done.TrySetException(new InvalidOperationException(err.Data.Message ?? "Session error"));
                    break;
            }
        });

        await session.SendAsync(new MessageOptions { Prompt = userPrompt });
        var content = await done.Task.WaitAsync(cts.Token);

        if (string.IsNullOrEmpty(content))
            throw new InvalidOperationException($"{timeoutLabel} returned no content");

        return new LlmResponse(content, new TokenUsage(inputTokens, outputTokens, cacheReadTokens, cacheWriteTokens));
    }
}
