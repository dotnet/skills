/**
 * Utilities for handling JSON produced by LLMs, which may contain
 * invalid escape sequences or other structural quirks that trip up
 * JSON.parse.
 */

const VALID_ESCAPE_CHARS = new Set(['"', '\\', '/', 'b', 'f', 'n', 'r', 't', 'u']);

/**
 * Fix invalid JSON escape sequences that LLMs sometimes produce.
 *
 * Valid JSON escapes are: \" \\ \/ \b \f \n \r \t \uXXXX.
 * Anything else (e.g. \M, \S, \p) is turned into a double-backslash
 * so that JSON.parse reads it as a literal backslash + letter.
 *
 * The function walks character-by-character, tracking whether we are
 * inside a JSON string, so it never modifies structural characters
 * outside of strings.
 */
export function sanitizeJsonEscapes(jsonStr: string): string {
  let result = '';
  let inString = false;
  let i = 0;

  while (i < jsonStr.length) {
    const ch = jsonStr[i];

    if (!inString) {
      result += ch;
      if (ch === '"') inString = true;
      i++;
      continue;
    }

    // Inside a JSON string value
    if (ch === '\\') {
      const next = jsonStr[i + 1];
      if (next !== undefined && VALID_ESCAPE_CHARS.has(next)) {
        // Valid escape sequence — keep as-is
        result += ch + next;
        i += 2;
      } else {
        // Invalid escape (or trailing backslash) — emit an escaped
        // backslash so JSON.parse sees a literal "\"
        result += '\\\\';
        i++;
      }
    } else if (ch === '"') {
      result += ch;
      inString = false;
      i++;
    } else {
      result += ch;
      i++;
    }
  }

  return result;
}
