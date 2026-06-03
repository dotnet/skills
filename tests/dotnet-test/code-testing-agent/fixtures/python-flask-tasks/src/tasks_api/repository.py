"""Repository abstraction for tasks."""

from __future__ import annotations

from typing import Iterable, Optional, Protocol

from .models import Tag, Task


class TaskRepository(Protocol):
    def add(self, task: Task) -> None: ...
    def get(self, task_id: int) -> Optional[Task]: ...
    def list(self) -> Iterable[Task]: ...
    def update(self, task: Task) -> None: ...
    def delete(self, task_id: int) -> bool: ...
    def next_id(self) -> int: ...


class InMemoryTaskRepository:
    def __init__(self) -> None:
        self._tasks: dict[int, Task] = {}
        self._next_id = 1

    def add(self, task: Task) -> None:
        if task.id in self._tasks:
            raise ValueError(f"task id {task.id} already exists")
        self._tasks[task.id] = task

    def get(self, task_id: int) -> Optional[Task]:
        return self._tasks.get(task_id)

    def list(self) -> Iterable[Task]:
        # Return a snapshot list so callers can mutate freely.
        return list(self._tasks.values())

    def update(self, task: Task) -> None:
        if task.id not in self._tasks:
            raise KeyError(task.id)
        self._tasks[task.id] = task

    def delete(self, task_id: int) -> bool:
        return self._tasks.pop(task_id, None) is not None

    def next_id(self) -> int:
        value = self._next_id
        self._next_id += 1
        return value


def normalize_tags(tags: Iterable[Tag | str]) -> list[Tag]:
    """Deduplicate and canonicalize a tag iterable.

    - String values are wrapped into ``Tag`` instances (applying name validation).
    - Duplicates (by normalized name) are collapsed, preserving first-seen order.
    """
    seen: dict[str, Tag] = {}
    for raw in tags:
        tag = raw if isinstance(raw, Tag) else Tag(raw)
        if tag.name not in seen:
            seen[tag.name] = tag
    return list(seen.values())
