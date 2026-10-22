from __future__ import annotations

from typing import Sequence


def mean(values: Sequence[float]) -> float:
    if not values:
        raise ValueError("values must not be empty")
    return sum(values) / len(values)


def percentile(values: Sequence[float], percentile_value: float) -> float:
    if not values:
        raise ValueError("values must not be empty")
    if percentile_value < 0 or percentile_value > 100:
        raise ValueError("percentile must be between 0 and 100")

    ordered = sorted(values)
    index = round((percentile_value / 100) * (len(ordered) - 1))
    return ordered[index]
