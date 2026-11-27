"""Staged only for grading: proves count_words handles runs of whitespace without changing other results."""
import sys

sys.path.insert(0, ".")

from wordstats import average_word_length, count_words  # noqa: E402

checks = {
    "double space between words": count_words("hello  world") == 2,
    "leading and trailing spaces": count_words("  one  ") == 1,
    "empty text has no words": count_words("") == 0,
    "single spaces unchanged": count_words("the quick brown fox") == 4,
    "punctuation stays with its word": count_words("well, hello there!") == 3,
    "average length unchanged": average_word_length("ab abcd") == 3.0,
}
failed = [name for name, ok in checks.items() if not ok]
if failed:
    print(f"{len(failed)} of {len(checks)} fix checks failed: {', '.join(failed)}", file=sys.stderr)
    sys.exit(1)
print(f"fix checks passed: {len(checks)}")
