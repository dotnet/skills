"""Filter/sort/pagination logic for listing tasks.

The query layer is kept separate from :mod:`tasks_api.service` so it can be
exhaustively unit-tested in isolation against fixed Task lists.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from typing import Iterable, List, Optional

from .models import Tag, Task, TaskPriority, TaskStatus


class TaskQueryError(ValueError):
    """Raised for malformed query parameters."""


_SORT_FIELDS = ("created_at", "due_at", "priority", "title")


@dataclass
class TaskQuery:
    status: Optional[TaskStatus] = None
    tag: Optional[str] = None
    search: Optional[str] = None
    overdue: Optional[bool] = None
    sort: str = "created_at"
    descending: bool = False
    limit: int = 20
    offset: int = 0

    def __post_init__(self) -> None:
        if self.sort not in _SORT_FIELDS:
            raise TaskQueryError(
                f"sort must be one of {_SORT_FIELDS} (got {self.sort!r})"
            )
        if not isinstance(self.limit, int) or self.limit <= 0 or self.limit > 100:
            raise TaskQueryError("limit must be an integer between 1 and 100")
        if not isinstance(self.offset, int) or self.offset < 0:
            raise TaskQueryError("offset must be a non-negative integer")
        if self.tag is not None:
            # Validate by constructing a Tag (normalizes/raises ValueError on bad input).
            self.tag = Tag(self.tag).name
        if self.search is not None:
            stripped = self.search.strip()
            self.search = stripped or None


@dataclass
class TaskPage:
    items: List[Task]
    total: int
    limit: int
    offset: int

    @property
    def has_more(self) -> bool:
        return self.offset + len(self.items) < self.total


def _matches(task: Task, query: TaskQuery, now: datetime) -> bool:
    if query.status is not None and task.status != query.status:
        return False
    if query.tag is not None and not any(t.name == query.tag for t in task.tags):
        return False
    if query.search is not None and query.search.lower() not in task.title.lower():
        return False
    if query.overdue is True and not task.is_overdue(now):
        return False
    if query.overdue is False and task.is_overdue(now):
        return False
    return True


def _sort_key(task: Task, field: str):
    if field == "created_at":
        return task.created_at
    if field == "due_at":
        # None due_at sorts last (ascending). Use a (is_none, sortable) tuple
        # so we never compare across the None / not-None boundary, and key on
        # `isoformat()` rather than the raw datetime so a stray naive datetime
        # mixed with timezone-aware ones cannot raise a TypeError mid-sort.
        return (task.due_at is None, task.due_at.isoformat() if task.due_at else "")
    if field == "priority":
        return TaskPriority.order_key(task.priority)
    if field == "title":
        return task.title.lower()
    raise TaskQueryError(f"unsupported sort field: {field}")


def apply_query(tasks: Iterable[Task], query: TaskQuery, now: datetime) -> TaskPage:
    filtered = [t for t in tasks if _matches(t, query, now)]
    filtered.sort(key=lambda t: _sort_key(t, query.sort), reverse=query.descending)
    total = len(filtered)
    window = filtered[query.offset : query.offset + query.limit]
    return TaskPage(items=window, total=total, limit=query.limit, offset=query.offset)
