"""Domain models for the tasks API."""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime, timezone
from enum import Enum
from typing import List, Optional


class TaskStatus(str, Enum):
    PENDING = "pending"
    DONE = "done"


class TaskPriority(str, Enum):
    LOW = "low"
    MEDIUM = "medium"
    HIGH = "high"

    @classmethod
    def order_key(cls, value: "TaskPriority") -> int:
        # Higher priority sorts earlier; useful for the service's sort=priority option.
        return {cls.HIGH: 0, cls.MEDIUM: 1, cls.LOW: 2}[value]


@dataclass
class Tag:
    """A normalized tag value. Tag names are lowercased and stripped at construction."""

    name: str

    def __post_init__(self) -> None:
        if not isinstance(self.name, str):
            raise TypeError("tag name must be a string")
        normalized = self.name.strip().lower()
        if not normalized:
            raise ValueError("tag name must not be empty")
        if len(normalized) > 32:
            raise ValueError("tag name must be 32 characters or fewer")
        # Allow letters, digits, hyphen, underscore.
        for ch in normalized:
            if not (ch.isalnum() or ch in ("-", "_")):
                raise ValueError(f"tag name contains illegal character: {ch!r}")
        object.__setattr__(self, "name", normalized)


@dataclass
class Task:
    id: int
    title: str
    status: TaskStatus = TaskStatus.PENDING
    priority: TaskPriority = TaskPriority.MEDIUM
    due_at: Optional[datetime] = None
    completed_at: Optional[datetime] = None
    created_at: datetime = field(default_factory=lambda: datetime.now(timezone.utc))
    tags: List[Tag] = field(default_factory=list)

    def is_overdue(self, now: datetime) -> bool:
        """Return True if the task has a due date in the past and is not yet done."""
        if self.due_at is None or self.status == TaskStatus.DONE:
            return False
        return self.due_at < now
