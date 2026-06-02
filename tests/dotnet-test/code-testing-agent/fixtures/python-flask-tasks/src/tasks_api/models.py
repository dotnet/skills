"""Domain models for the tasks API."""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime, timezone
from enum import Enum
from typing import Optional


class TaskStatus(str, Enum):
    PENDING = "pending"
    DONE = "done"


@dataclass
class Task:
    id: int
    title: str
    status: TaskStatus = TaskStatus.PENDING
    completed_at: Optional[datetime] = None
    created_at: datetime = field(default_factory=lambda: datetime.now(timezone.utc))
