from __future__ import annotations

import re


def slugify(value: str, max_length: int = 48) -> str:
    if max_length <= 0:
        raise ValueError("max_length must be positive")

    slug = re.sub(r"[^a-z0-9]+", "-", value.lower()).strip("-")
    if len(slug) <= max_length:
        return slug

    shortened = slug[:max_length].rstrip("-")
    return shortened
