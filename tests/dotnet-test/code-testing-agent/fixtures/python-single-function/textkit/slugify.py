"""Slug generation for URL paths."""

import re
from typing import Optional

_SEPARATORS = re.compile(r"[^a-z0-9]+")


def slugify(text: str, max_length: Optional[int] = None) -> str:
    """Return a lowercase, hyphen-separated slug for ``text``.

    Runs of non-alphanumeric characters collapse to a single hyphen and
    leading/trailing hyphens are trimmed. When ``max_length`` is given the slug
    is truncated at a hyphen boundary when one is available, so a slug never
    ends mid-word.

    Raises:
        ValueError: if ``max_length`` is not positive.
    """
    if max_length is not None and max_length <= 0:
        raise ValueError("max_length must be positive")

    slug = _SEPARATORS.sub("-", text.strip().lower()).strip("-")

    if max_length is None or len(slug) <= max_length:
        return slug

    truncated = slug[:max_length]
    boundary = truncated.rfind("-")
    if boundary > 0:
        return truncated[:boundary]
    return truncated.strip("-")
