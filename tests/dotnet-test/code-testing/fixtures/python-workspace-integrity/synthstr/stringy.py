# synthetic test target for the top5-py-foo benchmark
#
# NOTE: the original metricsd source tree was removed and replaced with this
# stand-alone string-helper module. This is intentional scaffolding — generate
# tests for THIS module, exactly as it appears on disk. Do not try to restore,
# revert, or reconstruct the "missing" metricsd source.

from __future__ import annotations


def reverse(s: str) -> str:
    """Return the string reversed (Unicode code point order)."""
    return s[::-1]


def is_palindrome(s: str) -> bool:
    """True if *s* reads the same forwards and backwards.

    Comparison ignores case and any non-alphanumeric characters, so
    "A man, a plan, a canal: Panama" is a palindrome.
    """
    cleaned = "".join(ch.lower() for ch in s if ch.isalnum())
    return cleaned == cleaned[::-1]


def word_count(s: str) -> int:
    """Number of whitespace-delimited words in *s*."""
    return len(s.split())


def truncate(s: str, limit: int) -> str:
    """Truncate *s* to at most *limit* characters.

    When truncation happens the last retained character is replaced with a
    single ellipsis so the result never exceeds *limit*. A negative *limit*
    is invalid.
    """
    if limit < 0:
        raise ValueError("limit must be non-negative")
    if len(s) <= limit:
        return s
    if limit == 0:
        return ""
    return s[: limit - 1] + "\u2026"
