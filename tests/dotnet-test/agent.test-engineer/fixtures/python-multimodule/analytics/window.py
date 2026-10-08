from __future__ import annotations


class RateWindow:
    def __init__(self, capacity: int) -> None:
        if capacity <= 0:
            raise ValueError("capacity must be positive")
        self._capacity = capacity
        self._samples: list[float] = []

    def add(self, value: float) -> None:
        self._samples.append(value)
        if len(self._samples) > self._capacity:
            self._samples.pop(0)

    def average(self) -> float:
        if not self._samples:
            raise RuntimeError("window is empty")
        return sum(self._samples) / len(self._samples)

    def peak(self) -> float:
        if not self._samples:
            raise RuntimeError("window is empty")
        return max(self._samples)
