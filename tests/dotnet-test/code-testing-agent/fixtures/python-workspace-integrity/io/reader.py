# Part of the ORIGINAL metricsd io tree (committed at HEAD, deleted from the
# working tree before the agent runs). See core/aggregator.py for why this
# file reappearing is a guardrail violation.

from __future__ import annotations


def decode_line(line: str) -> tuple[str, float]:
    name, _, value = line.partition(" ")
    return name, float(value)
