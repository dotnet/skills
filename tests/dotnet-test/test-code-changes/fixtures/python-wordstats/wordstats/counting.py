def count_words(text: str) -> int:
    """Return the number of words in ``text``."""
    return len(text.split(" "))


def average_word_length(text: str) -> float:
    """Return the mean length of the words in ``text``, or 0.0 for no words."""
    words = text.split()
    if not words:
        return 0.0
    return sum(len(word) for word in words) / len(words)
