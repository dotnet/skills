/**
 * Utilities for handling JSON produced by LLMs, which may contain
 * invalid escape sequences or other structural quirks that trip up
 * JSON.parse.
 */

const SIMPLE_ESCAPE_CHARS = new Set(['"', '\\', '/', 'b', 'f', 'n', 'r', 't']);
const HEX_CHARS = new Set('0123456789abcdefABCDEF');

/**
 * Fix invalid JSON escape sequences that LLMs sometimes produce.
 *
 * Valid JSON escapes are: \" \\ \/ \b \f \n \r \t \uXXXX.
 * Anything else (e.g. \M, \S, \p, or malformed \u sequences) is turned
 * into a double-backslash so that JSON.parse reads it as a literal backslash
 * + the following characters.
 *
 * The function walks character-by-character, tracking whether we are
 * inside a JSON string, so it never modifies structural characters
 * outside of strings.
 *
 * Uses an array-based builder (joined at the end) to keep runtime linear
 * and avoid quadratic string concatenation for large inputs.
 */
export function sanitizeJsonEscapes(jsonStr: string): string {
  const parts: string[] = [];
  let inString = false;
  let i = 0;

  while (i < jsonStr.length) {
    const ch = jsonStr[i];

    if (!inString) {
      parts.push(ch);
      if (ch === '"') inString = true;
      i++;
      continue;
    }

    // Inside a JSON string value
    if (ch === '\\') {
      const next = jsonStr[i + 1];
      if (next === 'u') {
        // \u must be followed by exactly 4 hex digits to be valid
        if (
          i + 5 < jsonStr.length &&
          HEX_CHARS.has(jsonStr[i + 2]) &&
          HEX_CHARS.has(jsonStr[i + 3]) &&
          HEX_CHARS.has(jsonStr[i + 4]) &&
          HEX_CHARS.has(jsonStr[i + 5])
        ) {
          parts.push(jsonStr.slice(i, i + 6));
          i += 6;
        } else {
          // Malformed \u — escape the backslash so it becomes literal
          parts.push('\\\\');
          i++;
        }
      } else if (next !== undefined && SIMPLE_ESCAPE_CHARS.has(next)) {
        // Valid simple escape sequence — keep as-is
        parts.push(ch, next);
        i += 2;
      } else {
        // Invalid escape (or trailing backslash) — emit an escaped
        // backslash so JSON.parse sees a literal "\"
        parts.push('\\\\');
        i++;
      }
    } else if (ch === '"') {
      parts.push(ch);
      inString = false;
      i++;
    } else {
      parts.push(ch);
      i++;
    }
  }

  return parts.join('');
}
