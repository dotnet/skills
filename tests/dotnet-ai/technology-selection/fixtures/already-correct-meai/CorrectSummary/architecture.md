# Current architecture

- The endpoint depends on `IChatClient`.
- Composition code registers the provider through `AddChatClient`.
- `ChatOptions` sets `Temperature` and `MaxOutputTokens`.
- Retry and timeout behavior is bounded in the resilience pipeline, and cancellation flows from the HTTP request.
- Credentials come from environment-backed configuration.
- The endpoint makes one prompt-response call and uses no tools.
