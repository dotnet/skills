/**
 * Shared utilities for parsing JSON from LLM responses.
 *
 * LLMs often wrap JSON in markdown code blocks or produce invalid escape
 * sequences. These helpers handle both cases robustly.
 */

/**
 * Extract a JSON string from LLM response text.
 * Tries markdown code block first, then falls back to brace-matching.
 * When brace-matching, validates candidates with JSON.parse and skips
 * non-JSON brace groups (e.g., C# code snippets).
 */
export function extractJson(content: string): string | null {
  const codeBlockMatch = content.match(/```(?:json)?\s*(\{[\s\S]*?\})\s*```/);
  if (codeBlockMatch) return codeBlockMatch[1];

  // Try each top-level brace group until we find valid JSON
  let searchFrom = 0;
  while (searchFrom < content.length) {
    const candidate = extractOutermostJson(content, searchFrom);
    if (!candidate) return null;

    try {
      JSON.parse(candidate.text);
      return candidate.text;
    } catch {
      // Not valid JSON (e.g., C# code block) — skip past it
      try {
        // Also try with escape sanitization before giving up on this candidate
        JSON.parse(candidate.text.replace(/\\(?!["\\/bfnrtu])/g, ""));
        return candidate.text;
      } catch {
        searchFrom = candidate.endIndex + 1;
      }
    }
  }

  return null;
}

/**
 * Parse a JSON string, tolerating invalid escape sequences that LLMs
 * sometimes produce (e.g., \' or \a).
 *
 * @param jsonStr  The raw JSON string to parse
 * @param context  A label for error messages (e.g., "pairwise judge (forward)")
 */
export function parseLlmJson(jsonStr: string, context: string): any {
  try {
    return JSON.parse(jsonStr);
  } catch (err) {
    const originalError = err;
    const hasInvalidEscapes = /\\(?!["\\/bfnrtu])/.test(jsonStr);

    if (!hasInvalidEscapes) {
      const snippet = jsonStr.slice(0, 200);
      throw new Error(
        `Failed to parse ${context} JSON. Original error: ${String(
          originalError
        )}. JSON snippet: ${snippet}`
      );
    }

    // LLMs sometimes produce invalid JSON escape sequences (e.g., \' or \a).
    // Retry after removing backslashes before non-JSON-escape characters.
    try {
      return JSON.parse(jsonStr.replace(/\\(?!["\\/bfnrtu])/g, ""));
    } catch (retryErr) {
      const snippet = jsonStr.slice(0, 200);
      throw new Error(
        `Failed to parse ${context} JSON even after sanitizing invalid escapes. ` +
          `Original error: ${String(originalError)}. Retry error: ${String(
            retryErr
          )}. JSON snippet: ${snippet}`
      );
    }
  }
}

function extractOutermostJson(
  text: string,
  fromIndex: number = 0
): { text: string; endIndex: number } | null {
  const start = text.indexOf("{", fromIndex);
  if (start === -1) return null;

  let depth = 0;
  let inString = false;
  let escape = false;

  for (let i = start; i < text.length; i++) {
    const ch = text[i];
    if (escape) { escape = false; continue; }
    if (ch === "\\") { escape = true; continue; }
    if (ch === '"') { inString = !inString; continue; }
    if (inString) continue;
    if (ch === "{") depth++;
    if (ch === "}") {
      depth--;
      if (depth === 0) return { text: text.slice(start, i + 1), endIndex: i };
    }
  }

  return null;
}
