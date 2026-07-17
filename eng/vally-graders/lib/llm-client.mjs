// llm-client.mjs — thin, lazy wrapper over @github/copilot-sdk for the
// `eval-review` LLM grader's judge calls.
//
// Mirrors vally's packages/core/src/graders/llm/llm-client.ts (createCopilotLlmClient):
// a tool-call based `judge()` API so the grader never couples to the SDK directly
// and can be unit-tested with a fake client. The SDK (+ zod) are OPTIONAL
// dependencies, imported dynamically here so the free, no-model static gate
// (`review.mjs` with no --llm) never needs them installed.
//
// Every failure mode is surfaced as a typed Error (err.code) so callers can map
// it to an "inconclusive" result — this grader must NEVER produce a false
// BLOCKER because a model call could not be made.

/** Error with a stable `.code` for caller-side classification. */
export class LlmUnavailableError extends Error {
  constructor(code, message) {
    super(message);
    this.name = "LlmUnavailableError";
    this.code = code; // "SDK_MISSING" | "NO_TOKEN" | "START_FAILED"
  }
}

function resolveToken() {
  return (
    process.env.GITHUB_COPILOT_API_TOKEN ||
    process.env.GITHUB_TOKEN ||
    process.env.GH_TOKEN ||
    ""
  );
}

/**
 * Provision an LlmClient backed by the Copilot SDK.
 *
 * @returns {Promise<{ judge: Function, shutdown: Function, tokenSource: string }>}
 * @throws {LlmUnavailableError} if the SDK isn't installed or the runtime can't start.
 */
export async function createCopilotLlmClient() {
  let sdk;
  try {
    sdk = await import("@github/copilot-sdk");
  } catch (err) {
    throw new LlmUnavailableError(
      "SDK_MISSING",
      `@github/copilot-sdk is not installed. Install the optional deps to enable --llm ` +
        `(cd eng/vally-graders && npm install). Original: ${err?.message ?? err}`,
    );
  }
  const { CopilotClient, defineTool } = sdk;

  const token = resolveToken();
  const tokenSource = process.env.GITHUB_COPILOT_API_TOKEN
    ? "GITHUB_COPILOT_API_TOKEN"
    : process.env.GITHUB_TOKEN
      ? "GITHUB_TOKEN"
      : process.env.GH_TOKEN
        ? "GH_TOKEN"
        : "none";

  const client = new CopilotClient({
    env: { ...process.env, NODE_NO_WARNINGS: "1" },
    gitHubToken: token || undefined,
  });

  try {
    await client.start();
  } catch (err) {
    const hint =
      tokenSource === "none"
        ? " No Copilot token found (set GITHUB_COPILOT_API_TOKEN or GITHUB_TOKEN with Copilot access)."
        : ` (token source: ${tokenSource}${
            tokenSource === "GH_TOKEN"
              ? "; a plain gh CLI token may lack Copilot scope"
              : ""
          }).`;
    throw new LlmUnavailableError(
      "START_FAILED",
      `Copilot runtime failed to start.${hint} Original: ${err?.message ?? err}`,
    );
  }

  // Deny every permission request; only the registered submit tool (which uses
  // skipPermission) is allowed. Blocks prompt-injection from untrusted skill text.
  const denyAll = () => ({
    kind: "reject",
    feedback: "eval-review judge sessions deny all tool/file access except the grading tool",
  });

  return {
    tokenSource,

    async judge(options) {
      const start = Date.now();
      const timeoutMs = options.timeoutMs ?? 120_000;
      const maxReminders = options.maxReminders ?? 2;

      let captured;
      let lastValidationError;
      let cumulativeInput = 0;
      let cumulativeOutput = 0;

      const submitTool = defineTool(options.tool.name, {
        description: options.tool.description,
        parameters: options.tool.parameters,
        skipPermission: true,
        handler: (args) => {
          const result = options.tool.parameters.safeParse(args);
          if (!result.success) {
            lastValidationError = result.error.message;
            return `Invalid arguments: ${result.error.message}`;
          }
          captured = result.data;
          lastValidationError = undefined;
          return "Review submitted. Do not respond further.";
        },
      });

      const session = await client.createSession({
        model: options.model,
        systemMessage: { mode: "replace", content: options.systemMessage },
        tools: [submitTool],
        availableTools: [options.tool.name],
        onPermissionRequest: denyAll,
        infiniteSessions: { enabled: false },
      });

      session.on("tool.execution_complete", (event) => {
        if (event?.data?.success === false) {
          lastValidationError =
            event.data.error?.message ?? "the SDK rejected the arguments as not matching the schema";
        }
      });
      session.on("assistant.usage", (event) => {
        cumulativeInput += event?.data?.inputTokens ?? 0;
        cumulativeOutput += event?.data?.outputTokens ?? 0;
      });

      try {
        await session.sendAndWait({ prompt: options.userMessage }, timeoutMs);

        let remindersUsed = 0;
        while (!captured && remindersUsed < maxReminders) {
          const reminder = lastValidationError
            ? `Your last \`${options.tool.name}\` call had invalid arguments: ${lastValidationError}. Call \`${options.tool.name}\` again with corrected arguments.`
            : `You did not call \`${options.tool.name}\`. You MUST call it with your findings.`;
          lastValidationError = undefined;
          remindersUsed++;
          await session.sendAndWait({ prompt: reminder }, timeoutMs);
        }

        if (!captured) {
          throw new Error(`Judge did not invoke ${options.tool.name} after ${maxReminders} reminder(s)`);
        }

        const tokenUsage =
          cumulativeInput + cumulativeOutput > 0
            ? { inputTokens: cumulativeInput, outputTokens: cumulativeOutput, model: options.model }
            : undefined;

        return { args: captured, tokenUsage, latencyMs: Date.now() - start, remindersUsed };
      } finally {
        await session.disconnect();
      }
    },

    async shutdown() {
      try {
        await client.stop();
      } catch {
        /* best effort */
      }
    },
  };
}
